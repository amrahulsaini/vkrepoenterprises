# Server Infrastructure & Deployment Handbook

> Source of truth for the **live production server** as of this writing.
> Prepared from direct read-only inspection of the server (not from older
> design documents). Hand this to anyone (human or AI) who needs to operate
> or extend the infrastructure.

---

## 1. Snapshot — what is actually running

### 1.1 Server
| Item | Value |
|---|---|
| Provider | Google Cloud Platform |
| VM name | `vkenterprises` |
| Zone | `us-central1-c` |
| Public IP (IPv4) | **`34.59.75.191`** |
| Operating system | Ubuntu 22.04 LTS |
| Web server / panel | **CyberPanel 2.4.4 + OpenLiteSpeed** |
| .NET runtime | .NET 6 (`/usr/share/dotnet`) |
| Database | MySQL (MariaDB-compatible) on `127.0.0.1:3306` |
| Database name | `vkre_db1` (user `vkre_db1`, password `db1`) |

### 1.2 Domains served
| Hostname | Purpose | TLS termination | Origin port behind LiteSpeed |
|---|---|---|---|
| `api.crmrecoverysoftware.com` | **Primary API** (current) | Cloudflare (Flexible, no origin SSL) | HTTP/80 → OLS |
| `api.characterverse.tech` | **Legacy API** (kept alive during rollout) | Let's Encrypt on origin | HTTP+HTTPS → OLS |
| `crmrecoverysoftware.com` (apex) | Empty CyberPanel site (not used by app) | — | — |
| `characterverse.tech` (apex) | Empty CyberPanel site (not used by app) | — | — |
| Other CyberPanel sites | `samarthrealty.properties`, `vkrepoenterprises.in`, `Example` (default) | — | — |

> Both API hostnames proxy to the **same** backend services and the **same** MySQL
> database, so they are interchangeable. The desktop app and Android app both
> point at `api.crmrecoverysoftware.com` from build `a8ec74f` onwards.

### 1.3 The two backend services
| systemd unit | Listens on | Built into | Source folder |
|---|---|---|---|
| `vkapi.service` | `127.0.0.1:5002` | `/opt/vkapi` | `/home/vkapp/VKApiServer` |
| `vkmobileapi.service` | `127.0.0.1:5001` | `/opt/vkmobileapi` | `/home/vkapp/VKmobileapi` |

Both:
- Run as user **`www-data`**.
- Restart automatically (`Restart=always`, `RestartSec=10`).
- Bind to **localhost only** — never exposed to the internet directly. All
  external traffic enters OpenLiteSpeed on 80/443 and OLS reverse-proxies in.

### 1.4 systemd unit files (verbatim)

**`/etc/systemd/system/vkapi.service`**
```ini
[Unit]
Description=VK Enterprises API
After=network.target

[Service]
WorkingDirectory=/opt/vkapi
ExecStart=/usr/share/dotnet/dotnet /opt/vkapi/VKApiServer.dll
Environment=DOTNET_ROOT=/usr/share/dotnet
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DESKTOP_LOGIN_PASSWORD=12
Environment=PRIVATEKEY=vk_enterprises_local_jwt_key
Environment=MYSQL_HOST=127.0.0.1
Environment=MYSQL_USER=vkre_db1
Environment=MYSQL_PASSWORD=db1
Environment=MYSQL_DATABASE=vkre_db1
Environment=MYSQL_PORT=3306
Environment=PORT=5002
Restart=always
RestartSec=10
User=www-data

[Install]
WantedBy=multi-user.target
```

**`/etc/systemd/system/vkmobileapi.service`**
```ini
[Unit]
Description=VK Mobile API
After=network.target

[Service]
WorkingDirectory=/opt/vkmobileapi
ExecStart=/usr/share/dotnet/dotnet /opt/vkmobileapi/VKmobileapi.dll
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_URLS=http://127.0.0.1:5001
Environment=MYSQL_HOST=127.0.0.1
Environment=MYSQL_USER=vkre_db1
Environment=MYSQL_PASSWORD=db1
Environment=MYSQL_DATABASE=vkre_db1
Environment=MYSQL_PORT=3306

[Install]
WantedBy=multi-user.target
```

### 1.5 Environment file
`/home/vkapp/db/.env.local` — copied into both `/opt/vkapi/db/` and
`/opt/vkmobileapi/db/` by `deploy.sh`. The systemd `Environment=` lines above
ALSO supply the same values, so the apps work even if the file is missing.

```
PORT=5002
MYSQL_HOST=127.0.0.1
MYSQL_USER=vkre_db1
MYSQL_PASSWORD=db1
MYSQL_DATABASE=vkre_db1
MYSQL_PORT=3306
PRIVATEKEY=your_jwt_secret_key_here
DESKTOP_LOGIN_PASSWORD=12
```

### 1.6 Repository on the server
- Path: **`/home/vkapp`** — *this folder IS the git repository* (not a subfolder).
- Remote: `https://github.com/amrahulsaini/vkrepoenterprises`
- Branch: `main`
- Pull: `cd /home/vkapp && sudo git pull origin main`

---

## 2. Traffic flow

```
Field phone / desktop app
        │  HTTPS
        ▼
   Cloudflare (only for crmrecoverysoftware.com)
   ─ TLS termination
   ─ DDoS / WAF
        │  HTTP / HTTPS to origin 34.59.75.191
        ▼
OpenLiteSpeed (CyberPanel)  listeners :80 and :443
        │  Host header → vhost map
        ▼
vhost: api.crmrecoverysoftware.com
        │
        ├─ /api/mobile/*  → reverse-proxy → 127.0.0.1:5001  (VKmobileapi)
        ├─ /downloads/    → static files on disk
        ├─ /public/       → static files on disk
        ├─ /uploads/      → static files (/opt/vkmobileapi/uploads/)
        └─ /  (anything else) → reverse-proxy → 127.0.0.1:5002  (VKApiServer)
                                                       │
                                                       ▼
                                              MySQL  vkre_db1
                                              127.0.0.1:3306
```

---

## 3. OpenLiteSpeed configuration

### 3.1 Listener mappings (`/usr/local/lsws/conf/httpd_config.conf`)

Three listeners exist — HTTP `*:80`, HTTPS `*:443` (IPv4), HTTPS `[ANY]:443`
(IPv6). Each has a `map` block listing the domains it serves. For every
domain there are entries on **all three listeners**:

```
listener  Default { ...
  map   api.crmrecoverysoftware.com  api.crmrecoverysoftware.com
  map   crmrecoverysoftware.com      crmrecoverysoftware.com
  map   api.characterverse.tech      api.characterverse.tech
  map   characterverse.tech          characterverse.tech
  map   samarthrealty.properties     samarthrealty.properties
  map   vkrepoenterprises.in         vkrepoenterprises.in
}
listener  SSL    { ... map ... (same set) ... }
listener  SSL_v6 { ... map ... (same set) ... }
```

And each domain has a `virtualHost` block pointing at its per-vhost config
file:
```
virtualHost api.crmrecoverysoftware.com {
  vhRoot         /home/crmrecoverysoftware.com
  configFile     $SERVER_ROOT/conf/vhosts/$VH_NAME/vhost.conf
  allowSymbolLink 1
  enableScript    1
  restrained      1
}
```
`restrained 1` means the vhost can only access files under `vhRoot` — note
that the `/uploads/` context points outside this (to `/opt/vkmobileapi/uploads/`),
which works because LiteSpeed treats absolute `location` paths in contexts
as explicit grants.

### 3.2 Per-vhost configs

Each domain has a folder at `/usr/local/lsws/conf/vhosts/<domain>/`
containing `vhost.conf` (owned `nobody:nogroup`, mode `750`).
CyberPanel keeps a backup `vhost.conf0` and an RCS history `vhost.conf0,v`.

**`/usr/local/lsws/conf/vhosts/api.crmrecoverysoftware.com/vhost.conf`** — the
canonical reverse-proxy config used by the apps. Behind Cloudflare with TLS
terminated at the edge, so **no `vhssl` block is needed at origin**:

```
docRoot                   /home/crmrecoverysoftware.com/api.crmrecoverysoftware.com
vhDomain                  $VH_NAME
vhAliases                 www.$VH_NAME
adminEmails               admin@crmrecoverysoftware.com
enableGzip                1
enableIpGeo               1

index  {
  useServer               0
  indexFiles              index.html
}

errorlog $VH_ROOT/logs/api.crmrecoverysoftware.com.error_log {
  useServer               0
  logLevel                WARN
  rollingSize             10M
}

accesslog $VH_ROOT/logs/api.crmrecoverysoftware.com.access_log {
  useServer               0
  logFormat               "%h %l %u %t \"%r\" %>s %b \"%{Referer}i\" \"%{User-Agent}i\""
  logHeaders              5
  rollingSize             10M
  keepDays                10
  compressArchive         1
}

extprocessor dotnetapi {
  type                    proxy
  address                 127.0.0.1:5002
  maxConns                100
  pcKeepAliveTimeout      60
  initTimeout             60
  retryTimeout            0
  respBuffer              0
}

extprocessor mobileapi {
  type                    proxy
  address                 127.0.0.1:5001
  maxConns                100
  pcKeepAliveTimeout      60
  initTimeout             60
  retryTimeout            0
  respBuffer              0
}

context /api/mobile/ {
  type                    proxy
  handler                 mobileapi
  addDefaultCharset       off
}

context /downloads/ {
  location                /home/crmrecoverysoftware.com/api.crmrecoverysoftware.com/downloads/
  allowBrowse             1
  addDefaultCharset       off
}

context /public/ {
  location                /home/crmrecoverysoftware.com/api.crmrecoverysoftware.com/public/
  allowBrowse             1
  addDefaultCharset       off
}

context /uploads/ {
  location                /opt/vkmobileapi/uploads/
  allowBrowse             1
  addDefaultCharset       off
}

context / {
  type                    proxy
  handler                 dotnetapi
  addDefaultCharset       off
}

rewrite  {
  enable                  1
  autoLoadHtaccess        1
}
```

> CyberPanel auto-appends a `vhssl { … }` block (referencing
> `/etc/letsencrypt/live/api.crmrecoverysoftware.com/`) even though we did
> not issue an origin certificate. That block is **harmless** because no
> :443 traffic ever reaches the origin (Cloudflare proxies HTTP on :80).
> If you ever switch off Cloudflare proxy, you must either issue a real
> Let's Encrypt cert at that path or delete the `vhssl` block.

**`/usr/local/lsws/conf/vhosts/api.characterverse.tech/vhost.conf`** — same
structure but with origin-side TLS:
- `docRoot /home/characterverse.tech/api.characterverse.tech`
- Static-file contexts point at that docRoot
- Has a real `vhssl { keyFile /etc/letsencrypt/live/api.characterverse.tech/privkey.pem … }` block (DNS for this domain is NOT on Cloudflare; it resolves directly to the origin, so Let's Encrypt issued a real cert)
- An extra `context /.well-known/acme-challenge { location /usr/local/lsws/Example/html/.well-known/acme-challenge … }` for cert renewal

### 3.3 OpenLiteSpeed control commands
```bash
sudo systemctl restart lsws            # full restart (use after vhost changes)
sudo systemctl status  lsws
sudo /usr/local/lsws/bin/lswsctrl reload   # graceful reload (rare)
```
**Important runtime note** — observed during the migration: when the vhost
config is written *before* the static-file directories on disk exist,
LiteSpeed caches a "missing directory" state and keeps returning 404 even
after the files are created. The reliable order is:
1. write vhost.conf
2. populate the static directories
3. `systemctl restart lsws`

---

## 4. Domain folder layout

CyberPanel-OLS uses this convention:

```
/home/<parent-domain>/
    ├── logs/                        # error/access logs for all sub-sites
    ├── public_html/                 # docRoot for the parent (apex) site
    └── <child-domain>/              # docRoot of a child / sub-domain
            ├── downloads/           # we sync the installer here
            ├── public/              # we sync map_live.html + Leaflet here
            └── (other files served as static)
```

Live example:
```
/home/crmrecoverysoftware.com/
    ├── logs/
    │     ├── api.crmrecoverysoftware.com.access_log
    │     ├── api.crmrecoverysoftware.com.error_log
    │     ├── crmrecoverysoftware.com.access_log
    │     └── crmrecoverysoftware.com.error_log
    ├── public_html/                                       # apex (empty/default)
    └── api.crmrecoverysoftware.com/                       # docRoot of the API child
            ├── downloads/  (index.html + VKEnterprises_Setup.exe)
            └── public/     (map_live.html, map_point.html, leaflet/)
```

**Ownership matters**: CyberPanel creates the child-domain folder owned by
the per-website Linux user (e.g. `crmre8591:nogroup`, `chara4599:nogroup`).
`deploy.sh` detects this owner via `stat -c '%U:%G'` on the docRoot and
`chown -R` the synced files back to that owner, otherwise OLS refuses to
serve them.

**Traverse bits**: LiteSpeed runs as user `nobody`, which must have execute
("x" / traverse) permission on every directory in the path. `deploy.sh`
applies `chmod o+x` on the website parent and the docRoot. Inside docRoot,
files are mode `644` and directories `755`.

---

## 5. The deploy script (`/home/vkapp/deploy.sh`)

Run once after pushing code changes:
```bash
sudo env PATH=/usr/share/dotnet:$PATH bash /home/vkapp/deploy.sh
```

What it does, in order:

1. **`git pull origin main`** in `/home/vkapp`.
2. **`dotnet publish`** `VKApiServer` into `/opt/vkapi`.
3. Copy `db/.env.local` into the build output.
4. **Sync `/downloads/` and `/public/` into every domain's docRoot.** The
   list of docroots lives in a single array at the top of the sync block —
   add a new entry to enable a new domain:
   ```bash
   DOMAIN_DOCROOTS=(
       "/home/characterverse.tech/api.characterverse.tech"
       "/home/crmrecoverysoftware.com/api.crmrecoverysoftware.com"
   )
   ```
   For each docroot it:
   - Skips silently if the folder doesn't exist (so adding a domain to the
     array before creating it in CyberPanel doesn't break things).
   - Adds traverse perms on the parent + docRoot for `nobody`.
   - Detects the CyberPanel domain owner and `chown -R`s the synced files.
   - Copies the latest `VKEnterprises_Setup.exe` and `downloads/index.html`
     into `<docRoot>/downloads/`.
   - Rebuilds `<docRoot>/public/` (`rm -rf` + `cp -r`) with map HTML + Leaflet.
5. `systemctl restart vkapi` + verify `is-active`.
6. **`dotnet publish`** `VKmobileapi` into `/opt/vkmobileapi`, copy env,
   `mkdir -p uploads/{pfp,kyc}`, `chown www-data`, `chmod o+rX`.
7. `systemctl restart vkmobileapi` + verify.

The script is in source control — never edit it directly on the server
(the next `git pull` will conflict or overwrite). Edit locally, commit, push,
then run on the server.

---

## 6. How to deploy from a developer machine

### 6.1 SSH to the server
```bash
gcloud compute ssh vkenterprises --zone=us-central1-c
# or run one command:
gcloud compute ssh vkenterprises --zone=us-central1-c \
  --command="sudo env PATH=/usr/share/dotnet:\$PATH bash /home/vkapp/deploy.sh"
```
If `gcloud auth` is expired the user must run `gcloud auth login` locally
first — it cannot be done non-interactively.

### 6.2 Standard release flow
1. Edit code locally → `git commit` → `git push origin main`.
2. Run `deploy.sh` on the server (SSH command above). It pulls + rebuilds
   both APIs and re-syncs static files for both domains.
3. Verify externally:
   ```bash
   curl -s https://api.crmrecoverysoftware.com/ | head
   curl -s https://api.crmrecoverysoftware.com/api/mobile/stats | head
   ```

### 6.3 Rebuilding the Windows installer + shipping it to clients
The desktop installer is **built locally** with Inno Setup (the server has
no Windows toolchain). Flow:
```powershell
# 1. Build a self-contained .NET publish locally
dotnet publish VKdesktopapp\VRASDesktopApp.csproj -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=false `
    -o VKdesktopapp\publish-fresh

# 2. Compile the Inno installer
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
# → installer-output\VKEnterprises_Setup.exe
```
Commit the resulting `.exe`, push, run `deploy.sh` on the server. The script
syncs it to every domain's `/downloads/`. Clients download it from
`https://api.crmrecoverysoftware.com/downloads/`.

### 6.4 Adding a new domain to serve the API
1. **DNS** — point the new hostname (Cloudflare or normal DNS) at
   `34.59.75.191`. For Cloudflare-proxied (Flexible mode), no origin SSL is
   needed.
2. **CyberPanel** — Websites → List Websites → parent site → *Create Child
   Domain* (or Create Website for a standalone domain). Leave SSL unchecked
   if Cloudflare proxies.
3. **vhost.conf** — overwrite the auto-generated
   `/usr/local/lsws/conf/vhosts/<new-domain>/vhost.conf` with the
   reverse-proxy config in §3.2 (adjust the four hardcoded docRoot paths and
   the `errorlog`/`accesslog` filenames; *omit* the `vhssl` block if behind
   Cloudflare proxy). `chown nobody:nogroup` the file and `chmod 750`.
4. **deploy.sh** — add the new docRoot path to `DOMAIN_DOCROOTS`. Commit + push.
5. On the server: `cd /home/vkapp && sudo git pull && sudo bash deploy.sh`.
6. **`sudo systemctl restart lsws`** — required after vhost changes, AND a
   second time after the first deploy populates the static directories
   (LiteSpeed caches missing-directory state).
7. Verify with internal curl using the Host header:
   ```bash
   curl -sSI -H "Host: <newhost>" http://127.0.0.1/
   ```

---

## 7. Useful inspection one-liners (run via gcloud ssh)

```bash
# Service status / recent logs
sudo systemctl status vkapi vkmobileapi
sudo journalctl -u vkapi -n 50 --no-pager
sudo journalctl -u vkmobileapi -n 50 --no-pager

# Domain layout
sudo ls -la /home/
sudo ls -la /home/crmrecoverysoftware.com/api.crmrecoverysoftware.com/

# OLS vhost mapping + per-vhost config
sudo grep -nE "^\s*map\s" /usr/local/lsws/conf/httpd_config.conf
sudo grep -B1 -A4 "<domain>" /usr/local/lsws/conf/httpd_config.conf
sudo cat /usr/local/lsws/conf/vhosts/<domain>/vhost.conf

# Vhost-specific logs (errors / access)
sudo tail -30 /home/<parent-domain>/logs/<vhost>.error_log
sudo tail -30 /home/<parent-domain>/logs/<vhost>.access_log

# Domain owner (used by deploy.sh)
sudo stat -c '%U:%G' /home/<parent-domain>/<vhost>

# Internal API check (bypasses Cloudflare/DNS)
curl -sSI -H "Host: api.crmrecoverysoftware.com" http://127.0.0.1/
curl -sS   -H "Host: api.crmrecoverysoftware.com" http://127.0.0.1/api/mobile/stats

# MySQL access
sudo mysql -u vkre_db1 -pdb1 vkre_db1 -e "SHOW TABLES;"
sudo mysql -u vkre_db1 -pdb1 vkre_db1 -e "SELECT COUNT(*) FROM vehicle_records;"
```

---

## 8. Cloudflare configuration (for `api.crmrecoverysoftware.com`)

The domain is on Cloudflare. Required settings for the current setup:

| Setting | Value |
|---|---|
| DNS — `api` record | A → `34.59.75.191`, **proxied (orange cloud)** |
| SSL/TLS mode | **Flexible** (Cloudflare → HTTP origin) — no cert on origin needed |
| Always Use HTTPS | On |
| Page Rules (optional) | Bypass cache for `api.*` (the API responses must not be cached) |

To issue a real Let's Encrypt cert at the origin (recommended long-term so
the SSL mode can be raised to *Full (strict)*):
1. In Cloudflare temporarily set the `api` record to **DNS only (grey cloud)**.
2. In CyberPanel → *SSL → Manage SSL → api.crmrecoverysoftware.com → Issue SSL*.
3. Re-enable the orange cloud, set SSL/TLS mode to **Full (strict)**.
4. Replace the empty `vhssl` block in the vhost.conf with the real Let's
   Encrypt paths and `systemctl restart lsws`.

---

## 9. Client-side configuration

### 9.1 Desktop application (WPF, .NET 6)
- **Default API URL**: hard-coded in
  `VKdesktopapp/Properties/Settings.settings` and
  `VKdesktopapp/Properties/Settings.Designer.cs` →
  `https://api.crmrecoverysoftware.com/`.
- **Runtime override**: each installed copy has a per-user
  `Settings.Default.ApiBaseUrl` (stored in `%LOCALAPPDATA%`) which can be
  changed in the app via the **Server Settings** window — no rebuild required.
- **One-shot migration**: `VKdesktopapp/App.xaml.cs` checks on startup and
  auto-bumps a saved value of `https://api.characterverse.tech/` to the new
  domain, so upgrading an existing install does not require manual action.

### 9.2 Android application (Kotlin, Jetpack Compose)
- **API URL** is compile-time in `android/app/build.gradle.kts`:
  ```kotlin
  buildConfigField("String", "BASE_URL", "\"https://api.crmrecoverysoftware.com/\"")
  ```
  Used via `BuildConfig.BASE_URL` in `data/api/ApiClient.kt`. There is no
  in-app override; every APK must be rebuilt to change the host.
- Build: `cd android && .\gradlew.bat assembleDebug` → APK at
  `android/app/build/outputs/apk/debug/app-debug.apk`. Send that file to
  field phones (sideload).

### 9.3 Auth headers the apps send
| Header | Sent by | Value |
|---|---|---|
| `X-Api-Key` | Desktop app | `12` (also the `DESKTOP_LOGIN_PASSWORD`) |
| `X-User-Id` | Mobile app | user id from local session storage |

The `/api/mgr/*` routes require `X-Api-Key`. `/api/mobile/*` routes require
`X-User-Id`. Anything else is open.

---

## 10. Where the per-domain knowledge lives in the codebase

| Concern | File |
|---|---|
| Desktop default URL | `VKdesktopapp/Properties/Settings.settings` + `Settings.Designer.cs` |
| Desktop URL override + migration | `VKdesktopapp/App.xaml.cs` |
| Android compile-time URL | `android/app/build.gradle.kts` |
| Server docroots to sync to | `deploy.sh` → `DOMAIN_DOCROOTS` array |
| Per-vhost reverse-proxy config | (server only) `/usr/local/lsws/conf/vhosts/<domain>/vhost.conf` — not in git |
| Inno installer manifest | `installer.iss` |

---

## 11. Disaster recovery

### Source code
Fully recoverable from the remote git repository
(`https://github.com/amrahulsaini/vkrepoenterprises`). Cloning into a fresh
Ubuntu+CyberPanel server reproduces the application; the steps in §6 are the
runbook.

### Database
The MySQL database `vkre_db1` is on the same VM. **Currently the only backup
is whatever Google Cloud snapshot policy is enabled on the VM** —
verify/enable scheduled snapshots in the GCP console. For application-level
backup, a daily `mysqldump` cron is recommended:
```bash
sudo crontab -e
# 0 3 * * *  mysqldump -u vkre_db1 -pdb1 vkre_db1 | gzip > /home/vkapp/db/backups/vkre_db1.$(date +\%F).sql.gz
```

### Configuration
`/home/vkapp/db/.env.local`, the two systemd unit files, and the vhost.conf
files are **not in git** — back them up separately. They are small enough
to keep in a private gist or password manager.

---

## 12. Currently known weak spots (security)

A bank security audit may flag these — see `BANK_VENDOR_SECURITY_RESPONSE.md`
for the formal questionnaire response and `BANK_VENDOR_SECURITY_ANNEXURES.md`
for policy documents. The genuine technical gaps:

1. `DESKTOP_LOGIN_PASSWORD=12` and `MYSQL_PASSWORD=db1` are weak.
2. The Control-Panel password (`app_users.admin_pass`) and shared
   subscription password are stored as **plain text** and compared as plain
   strings server-side; they should be salted/hashed (bcrypt/PBKDF2).
3. No automated MySQL backup (see §11).
4. No dependency vulnerability scanning is enabled on the repository.

None of these affect availability today; all are fixable.

---

## 13. Quick reference summary

| You want to… | Do this |
|---|---|
| Restart the API services | `sudo systemctl restart vkapi vkmobileapi` |
| Restart the web server | `sudo systemctl restart lsws` |
| Pull + redeploy everything | `sudo bash /home/vkapp/deploy.sh` (run as one gcloud ssh command, with `PATH=/usr/share/dotnet:$PATH`) |
| Open the database | `sudo mysql -u vkre_db1 -pdb1 vkre_db1` |
| Edit a vhost | `sudo nano /usr/local/lsws/conf/vhosts/<domain>/vhost.conf` then `sudo systemctl restart lsws` |
| Look at recent service errors | `sudo journalctl -u vkapi -n 50 --no-pager` |
| Look at recent web errors | `sudo tail -50 /home/<parent>/logs/<vhost>.error_log` |
| Check what domain serves what | `sudo grep -nE "^\s*map\s" /usr/local/lsws/conf/httpd_config.conf` |
| Add a new public hostname | §6.4 |

---

*Prepared from a live, read-only inspection of the production server. Keep
this file as `SERVER_INFRASTRUCTURE.md` in the repo root and update whenever
something on the server changes.*
