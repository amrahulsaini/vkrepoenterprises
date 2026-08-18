# CRMRS — xirevoa Production Runbook

Last updated: 2026-07-10.

Operational runbook for the **xirevoa** production deployment. This is the live primary as of the 2026-07-10 cutover (migrated from the `work1-482817` GCP box — see `MIGRATION-CUTOVER.md` "Migration #2" for how it was built and the gotchas). For app architecture/component details shared with the old box, see `RUNBOOK.md`. **Any AI or operator: read this file top to bottom before touching the server.**

---

## 0. TL;DR — where things are

| Thing | Value |
|-------|-------|
| GCP project | `xirevoa` (project number 857158793437) |
| Instance | `vk`, zone `asia-south1-c` (Mumbai) |
| Machine type | e2-highmem-4 (4 vCPU, 32 GB, 250 GB pd-ssd) |
| OS | Ubuntu 22.04 |
| Public IP | **35.200.233.147** (static, reserved as `crmrs-xirevoa-ip`) |
| Panel | CyberPanel + OpenLiteSpeed |
| Repo on server | `/home/vkapp` (git clone of the GitHub repo) |
| .NET | `/usr/bin/dotnet` (6.0.x) |
| gcloud account | `ammrahulsaini@gmail.com` |

---

## 1. SSH in

```bash
gcloud compute ssh vk --zone=asia-south1-c --project=xirevoa
```
- The active gcloud account must be `ammrahulsaini@gmail.com` (`gcloud config set account ammrahulsaini@gmail.com`).
- On Windows, gcloud needs its bundled Python: `export CLOUDSDK_PYTHON="C:/Users/<you>/AppData/Local/Google/Cloud SDK/google-cloud-sdk/platform/bundledpython/python.exe"` (Git Bash) if you see "Python was not found".
- Login user is `ammra`; **sudo is passwordless**.
- The **old box** (rollback + never write to it) is reachable via the SSH alias `ssh crmrs-gcp` (HostName 34.14.215.101, project `work1-482817`). Read-only only.

---

## 2. Services and ports (app/DB bind to 127.0.0.1 only)

| Service | systemd unit | Port | Published dir | Source project |
|---------|--------------|------|---------------|----------------|
| Main API | `vkapi` | 5002 | `/opt/vkapi` | VKApiServer |
| Mobile API | `vkmobileapi` | 5001 | `/opt/vkmobileapi` | VKmobileapi |
| MariaDB 10.11 | `mariadb` | 3306 | — | — |
| Redis | `redis-server` | 6379 | — | — |
| OpenLiteSpeed | `lshttpd` / `lsws` | 80/443/7080 | — | — |
| CyberPanel | `lscpd` | 8090 | — | — |

Both APIs run as `User=www-data`, `ExecStart=/usr/bin/dotnet /opt/vk(api|mobileapi)/*.dll --urls http://127.0.0.1:50xx`.

```bash
sudo systemctl restart vkapi vkmobileapi
sudo systemctl status  vkapi vkmobileapi
sudo journalctl -u vkapi -n 200 --no-pager
```

---

## 3. Domains & DNS (Cloudflare)

Cloudflare zone `crmrecoverysoftware.com` = zone id `a3bae98cdb8e855369e5c7d027705f8d`.

| Domain | → | Cloudflare proxy |
|--------|---|------------------|
| crmrecoverysoftware.com / www | 35.200.233.147 | grey (DNS-only) |
| api.crmrecoverysoftware.com | 35.200.233.147 | **grey (DNS-only) — MUST stay grey**, apps break behind CF proxy |
| agency.crmrecoverysoftware.com | 35.200.233.147 | proxied (orange) |
| manage.crmrecoverysoftware.com | 35.200.233.147 | grey |
| mail / webmail | 103.67.239.102 (OLD ServerBasket) | **not migrated — never repoint** |

**Rollback:** repoint api/agency/manage/root/www A-records to `34.14.215.101` (old GCP box, intact).

---

## 4. Deploy (push code → live)

### 4.1 From your machine
```bash
git add -A && git commit -m "your change" && git push origin main
```

### 4.2 On the server (backend)
```bash
gcloud compute ssh vk --zone=asia-south1-c --project=xirevoa
sudo git -C /home/vkapp pull origin main
# Main API
sudo systemctl stop vkapi; sudo rm -f /opt/vkapi/VKApiServer.pdb
cd /home/vkapp/VKApiServer && sudo /usr/bin/dotnet publish -c Release -o /opt/vkapi --nologo -v quiet
sudo cp -r /home/vkapp/VKApiServer/public/. /opt/vkapi/public/
sudo cp /home/vkapp/db/.env.local /opt/vkapi/db/.env.local
sudo mkdir -p /opt/vkapi/webhook-files /opt/vkapi/agency-uploads /opt/vkapi/integration-uploads
sudo chown -R www-data:www-data /opt/vkapi/webhook-files /opt/vkapi/agency-uploads /opt/vkapi/integration-uploads
sudo systemctl start vkapi
# Mobile API
sudo systemctl stop vkmobileapi; sudo rm -f /opt/vkmobileapi/VKmobileapi.pdb
cd /home/vkapp/VKmobileapi && sudo /usr/bin/dotnet publish -c Release -o /opt/vkmobileapi --nologo -v quiet
sudo cp /home/vkapp/db/.env.local /opt/vkmobileapi/db/.env.local
sudo mkdir -p /opt/vkmobileapi/uploads/{pfp,kyc}; sudo chown -R www-data:www-data /opt/vkmobileapi/uploads
sudo systemctl start vkmobileapi
```

### 4.3 Static portals
```bash
sudo cp -r /home/vkapp/agency-portal/. /home/agency.crmrecoverysoftware.com/public_html/
sudo cp -r /home/vkapp/manage-portal/. /home/manage.crmrecoverysoftware.com/public_html/
sudo cp -r /home/vkapp/main-site/.   /home/crmrecoverysoftware.com/public_html/
# fix ownership to each site's user
for d in crmrecoverysoftware.com agency.crmrecoverysoftware.com manage.crmrecoverysoftware.com; do
  o=$(stat -c '%U:%G' /home/$d/public_html); sudo chown -R "$o" /home/$d/public_html; done
```

### 4.4 Verify
```bash
curl -s https://api.crmrecoverysoftware.com/api/health          # expect overall: operational
curl -s http://localhost:5002/                                  # {"name":"CRMS API Server",...}
curl -s http://localhost:5001/                                  # {"status":"VK Mobile API running"}
# HTTPS test WITHOUT touching DNS (pin to this box):
curl -s --resolve api.crmrecoverysoftware.com:443:35.200.233.147 https://api.crmrecoverysoftware.com/api/health
```

---

## 5. Databases

Database-per-tenant on **MariaDB 10.11**. Local DB (not remote). Env vars use `MYSQL_*` names.
- `crm_master` — registry of every agency.
- Tenant DBs: `crmr_v_k_enterprises`, `crmr_rk_enterprises`.
- DB users: `crm_master_app` (master), `tu_<slug>` per tenant, each on hosts `127.0.0.1` and `localhost`.
- Tenant DB password is **derived**: `"T1!" + base64url(HMAC_SHA256(TENANT_DB_SECRET, "tenant:"+slug))[..25]` — so `TENANT_DB_SECRET` MUST match or tenant queries 500 while health stays green.

### 5.1 Tuning (persisted in `/etc/mysql/mariadb.conf.d/99-crmrs-tuning.cnf`)
```
innodb_buffer_pool_size = 8G
innodb_io_capacity = 1000
innodb_io_capacity_max = 2000
innodb_buffer_pool_dump_at_shutdown = ON
innodb_buffer_pool_load_at_startup  = ON
default-time-zone = +05:30          # IST — must match data or upload timestamps skew
```
System timezone is `Asia/Kolkata`.

### 5.2 Latency after a restart = cold buffer pool
The 3.5 GB tenant DB fits in the 8 GB pool. After a MariaDB restart the pool is cold → tenant searches hit disk. It reloads warm automatically from the saved dump (`load_at_startup`). To warm manually / re-save:
```bash
# warm hot tables + search indexes
sudo mysql crmr_v_k_enterprises -e "SELECT COUNT(*) FROM vehicle_records FORCE INDEX(PRIMARY);
 SELECT COUNT(*) FROM rc_info FORCE INDEX(PRIMARY); SELECT COUNT(*) FROM chassis_info FORCE INDEX(PRIMARY);
 SELECT COUNT(*) FROM vehicle_records FORCE INDEX(idx_vehicle_best);
 SELECT COUNT(*) FROM vehicle_records FORCE INDEX(idx_chassis_best);
 SELECT COUNT(*) FROM rc_info FORCE INDEX(idx_rc); SELECT COUNT(*) FROM chassis_info FORCE INDEX(idx_chassis);"
# persist the warm set so restarts reload it
sudo mysql -e "SET GLOBAL innodb_buffer_pool_dump_now=ON;"
```
Health only checks `crm_master` (tiny, always cached) — it stays fast even when tenant tables are cold, so don't trust health latency alone.

### 5.3 Backup (set one up — the single VM is the only copy)
Take dumps and ship them off-box; snapshot the GCP disk on a schedule.
```bash
sudo mysqldump --single-transaction --routines --triggers --events \
  --databases crm_master crmr_v_k_enterprises crmr_rk_enterprises | gzip > ~/crmrs_$(date +%F).sql.gz
```

---

## 6. TLS

Real Let's Encrypt SAN cert (api/agency/manage/root/www) via **acme.sh DNS-01** (Cloudflare token). Cert files: `/usr/local/lsws/conf/cert/crmrs.{crt,key}`, wired to a manually-added OpenLiteSpeed **SSL listener** on `*:443` in `/usr/local/lsws/conf/httpd_config.conf` plus a `vhssl {}` block in each vhost.

Auto-renews via acme.sh cron (`7 0 * * *`, DNS-01, needs the CF token to stay valid). Reissue manually:
```bash
sudo env CF_Token='<cloudflare-token>' /root/.acme.sh/acme.sh --issue --dns dns_cf --server letsencrypt --keylength ec-256 \
  -d api.crmrecoverysoftware.com -d agency.crmrecoverysoftware.com -d manage.crmrecoverysoftware.com \
  -d crmrecoverysoftware.com -d www.crmrecoverysoftware.com
sudo /root/.acme.sh/acme.sh --install-cert -d api.crmrecoverysoftware.com --ecc \
  --key-file /usr/local/lsws/conf/cert/crmrs.key --fullchain-file /usr/local/lsws/conf/cert/crmrs.crt \
  --reloadcmd "systemctl restart lshttpd"
```
**Gotcha:** binding a new OLS port needs a HARD restart — `sudo /usr/local/lsws/bin/lswsctrl stop && sudo /usr/local/lsws/bin/lswsctrl start` (graceful won't open a new listener).

---

## 7. Reverse proxy (api vHost)

The api vHost is a **bare reverse-proxy** (gzip OFF — required; gzip/cache break the streaming records-upload). File: `/usr/local/lsws/conf/vhosts/api.crmrecoverysoftware.com/vhost.conf` — `context /` → `extprocessor vkapi5002` → `http://127.0.0.1:5002`; keep the `/.well-known/acme-challenge` context. VKApiServer forwards `/api/mobile/*` in-process to `:5001`.

---

## 8. Firewall (GCP network)

Rule `crmrs-web-panel` (network `default`) opens `tcp:80,443,8090` + `udp:443`.
```bash
gcloud compute firewall-rules list --project=xirevoa
```
**HARDENING TODO:** `tcp:8090` (CyberPanel admin) is currently open to `0.0.0.0/0`. Restrict to the admin IP:
```bash
gcloud compute firewall-rules update crmrs-web-panel --project=xirevoa --source-ranges=<ADMIN_IP>/32
# (or split 8090 into its own rule scoped to the admin IP and keep 80/443 public)
```

---

## 9. Credentials

| What | Where / value |
|------|---------------|
| SSH | `gcloud compute ssh vk --zone=asia-south1-c --project=xirevoa` (account ammrahulsaini@gmail.com, passwordless sudo) |
| CyberPanel | `https://35.200.233.147:8090` — `admin` / `tU4505NM3yF4e2L8` (change + restrict 8090) |
| App secrets (MYSQL_*, MASTER_DB_*, DESKTOP_LOGIN_PASSWORD, MANAGE_PASSWORD, SMTP_*) | `/home/vkapp/db/.env.local` (copied to `/opt/vkapi/db/` + `/opt/vkmobileapi/db/` on deploy) |
| Desktop `X-Api-Key` (`DESKTOP_LOGIN_PASSWORD`) | `CUCfaQdVHN0bKbNPnvZ8PloDrIzbiNwnUcDFoBKk` — rotated 2026-08-18 off the old `12`. Must match `ApiKey` in the desktop app's `Settings.settings`. |
| Manage portal (`MANAGE_PASSWORD`) | `UrTqRUsDAoW7iwNAgQMP9lEh` — rotated 2026-08-18 off the committed `crmrs@kc.12` |
| Tenant/sandbox/MSG91 secrets (TENANT_DB_SECRET, SANDBOX_*, MSG91_*) | systemd drop-ins: `/etc/systemd/system/vkapi.service.d/{sandbox,tenant}.conf` and `vkmobileapi.service.d/{demo,msg91,sandbox,tenant}.conf` — **NOT in the .service file; easy to miss** |
| Cloudflare API token (cert renewal, DNS edits) | saved in acme.sh for auto-renew; DNS-edit scope `cfut_…`; zone id `a3bae98cdb8e855369e5c7d027705f8d` |
| Android keystore | `android/keystore/release.keystore`, alias `crms` / `crms@kc.12` (gitignored — back up) |
| Old box (rollback, read-only) | `ssh crmrs-gcp` → 34.14.215.101 (project work1-482817) |

**Secrets are now mandatory, not defaulted.** Both APIs throw at startup if `MYSQL_USER/PASSWORD/DATABASE`, `MASTER_DB_USER/PASSWORD`, `DESKTOP_LOGIN_PASSWORD`, `MANAGE_PASSWORD` or `TENANT_DB_SECRET` is missing (`RequiredEnv.Get`). A deploy that loses `.env.local` now fails loudly instead of silently falling back to a password committed to the repo. If `vkapi` won't start after a deploy, check `journalctl -u vkapi` for an `InvalidOperationException` naming the missing variable.

**Desktop key rollout (in progress, started 2026-08-18):** `DESKTOP_LOGIN_PASSWORD_OLD=12` is set so desktop clients still on the old key keep working. Once every client is on a build ≥ the 2026-08-18 release, delete that line from `/home/vkapp/db/.env.local` and restart `vkapi` to close the old key.

**Never rotate `TENANT_DB_SECRET`** — it derives every tenant's MariaDB password (§5) *and* signs live agency/mobile session tokens. Changing it locks out every tenant DB and every signed-in user at once.

**Hardening still outstanding:** restrict 8090 to admin IP; rotate the CyberPanel default password; set up off-box DB backups + GCP disk snapshots; move drop-in secrets to chmod-600 EnvironmentFiles / a vault (secrets in this committed file are for operational convenience only). `/api/Overview`, `/api/Records/Search` and `/api/AppUsers` take no auth at all — harmless today only because without an agency Bearer token they resolve to `crm_master`, which lacks those tables, so they return zeros.

---

## 10. Data-migration / reconciliation warnings

If you ever sync data from the old box again, read `MIGRATION-CUTOVER.md` "Migration #2" first. Key traps:
- **Never `REPLACE INTO branches`** — ON DELETE CASCADE (`branches → vehicle_records → rc_info/chassis_info`) will wipe records. Use `UPDATE`.
- `vehicle_records.completeness` is a **generated column** → load dumps with `SET SESSION sql_mode=''` (else ERROR 1906).
- `mysqldump --where` with a subquery needs `--single-transaction` (else ERROR 1100).
- Row-count/max-id checks don't prove data equality (in-place re-uploads and dedup deletes hide differences).
