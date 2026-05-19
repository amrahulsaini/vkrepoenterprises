# How to Move the Software to `crmrecoverysoftware.com`

> Written from a live inspection of the current production server — not from old docs.
> Nothing in this file has been changed on the server or in the code. It is a
> step-by-step guide for **you** to perform the domain migration.

---

## 1. What you have RIGHT NOW (verified on the server)

**Server**
- Google Cloud VM, name `vkenterprises`, public IP **`34.59.75.191`**
- Ubuntu + CyberPanel + OpenLiteSpeed web server
- MySQL running locally on port `3306` (database `vkre_db1`)

**The code / repo**
- Git repo lives at **`/home/vkapp`** (this folder *is* the repo — `git pull origin main` runs here)
- Deploy script: **`/home/vkapp/deploy.sh`**
- Env file: `/home/vkapp/db/.env.local`

**The two running apps (.NET background services)**
| Service | systemd unit | Listens on | Built into |
|---|---|---|---|
| Desktop/Web API (`VKApiServer`) | `vkapi.service` | `127.0.0.1:5002` | `/opt/vkapi` |
| Mobile API (`VKmobileapi`) | `vkmobileapi.service` | `127.0.0.1:5001` | `/opt/vkmobileapi` |

Both run as user `www-data`, auto-restart, and start on boot. **These never change with a domain switch** — they only listen on localhost; the web server in front of them is what carries the domain.

**Current domain**
- API is served at **`api.characterverse.tech`**
- That domain's web folder (docRoot): `/home/characterverse.tech/api.characterverse.tech`
  - `downloads/` — the installer + download page
  - `public/`  — the live-map HTML/assets
  - `index.html`
- OpenLiteSpeed vhost config file: `/usr/local/lsws/conf/vhosts/api.characterverse.tech/vhost.conf`
- SSL certificate: `/etc/letsencrypt/live/api.characterverse.tech/`
- The domain's Linux user (CyberPanel) is `chara4599`

**How traffic flows today**
```
Phone / Desktop app
        │  https://api.characterverse.tech
        ▼
OpenLiteSpeed (ports 80/443)  ── vhost: api.characterverse.tech
        │
        ├── /api/mobile/   → proxy → 127.0.0.1:5001  (VKmobileapi)
        ├── /downloads/    → static files on disk
        ├── /public/       → static files on disk
        ├── /uploads/      → static files (/opt/vkmobileapi/uploads)
        └── /  (everything else) → proxy → 127.0.0.1:5002  (VKApiServer)
```

**The current vhost config** (`vhost.conf`) — for reference, this is what you will copy + adapt:
```
docRoot                   /home/characterverse.tech/api.characterverse.tech
vhDomain                  $VH_NAME
vhAliases                 www.$VH_NAME
adminEmails               fnder@myaidry.me
enableGzip                1
enableIpGeo               1

index  { useServer 0  indexFiles index.html }

errorlog  $VH_ROOT/logs/characterverse.tech.error_log  { useServer 0  logLevel WARN  rollingSize 10M }
accesslog $VH_ROOT/logs/characterverse.tech.access_log { useServer 0  rollingSize 10M  keepDays 10  compressArchive 1 }

extprocessor dotnetapi {
  type        proxy
  address     127.0.0.1:5002
  maxConns    100
  pcKeepAliveTimeout 60
  initTimeout 60
  retryTimeout 0
  respBuffer  0
}
extprocessor mobileapi {
  type        proxy
  address     127.0.0.1:5001
  maxConns    100
  pcKeepAliveTimeout 60
  initTimeout 60
  retryTimeout 0
  respBuffer  0
}

context /api/mobile/ { type proxy  handler mobileapi  addDefaultCharset off }
context /downloads/  { location /home/characterverse.tech/api.characterverse.tech/downloads/  allowBrowse 1  addDefaultCharset off }
context /public/     { location /home/characterverse.tech/api.characterverse.tech/public/     allowBrowse 1  addDefaultCharset off }
context /uploads/    { location /opt/vkmobileapi/uploads/  allowBrowse 1  addDefaultCharset off }
context /            { type proxy  handler dotnetapi  addDefaultCharset off }
context /.well-known/acme-challenge { location /usr/local/lsws/Example/html/.well-known/acme-challenge  allowBrowse 1  rewrite { enable 0 }  addDefaultCharset off }

rewrite { enable 1  autoLoadHtaccess 1 }

vhssl  {
  keyFile     /etc/letsencrypt/live/api.characterverse.tech/privkey.pem
  certFile    /etc/letsencrypt/live/api.characterverse.tech/fullchain.pem
  certChain   1
  sslProtocol 24
  enableECDHE 1  renegProtection 1  sslSessionCache 1  enableSpdy 15  enableStapling 1  ocspRespMaxAge 86400
}
```

---

## 2. The plan

You will add a **new domain alongside the old one** — nothing is deleted, so the
old `api.characterverse.tech` keeps working until you are happy and ready to retire it.

**Recommended:** use a subdomain **`api.crmrecoverysoftware.com`** for the API.
This mirrors the proven current structure exactly (lowest risk). The bare
`crmrecoverysoftware.com` is then free for a marketing/landing website later.

> If you would rather use the bare `crmrecoverysoftware.com` for the API itself,
> every step below still applies — just use `crmrecoverysoftware.com` everywhere
> this guide says `api.crmrecoverysoftware.com`.

**The 8 things that must change** (full checklist — details follow):
1. DNS — point the new domain at `34.59.75.191`
2. CyberPanel — create the website / child-domain
3. SSL — issue a Let's Encrypt certificate
4. OpenLiteSpeed vhost — add the reverse-proxy config (copy of the current one)
5. `deploy.sh` — update the two docRoot paths
6. Run `deploy.sh` once so `/downloads/` and `/public/` populate on the new domain
7. Desktop app — point it at the new URL
8. Android app — rebuild with the new URL

---

## 3. Step A — DNS

In whatever panel manages the `crmrecoverysoftware.com` domain (GoDaddy /
Namecheap / Cloudflare / etc.), add an **A record**:

| Type | Name | Value | Notes |
|---|---|---|---|
| A | `api` | `34.59.75.191` | creates `api.crmrecoverysoftware.com` |
| A | `@`   | `34.59.75.191` | optional — bare domain, only if you want it too |

- If you use **Cloudflare**: set the record to **DNS only (grey cloud)** while
  issuing SSL; you can turn the orange cloud on afterwards. If you keep the
  orange cloud on, set SSL/TLS mode to **Full (strict)**.
- Wait until `ping api.crmrecoverysoftware.com` returns `34.59.75.191` before
  continuing (DNS can take 5–60 minutes).

---

## 4. Step B — Create the site in CyberPanel

Log in to CyberPanel (`https://34.59.75.191:8090`).

The current setup has `characterverse.tech` as a **website** and
`api.characterverse.tech` as a **child domain** under it. Mirror that:

1. **Websites → Create Website**
   - Domain: `crmrecoverysoftware.com`
   - Pick the PHP version / email / package as offered; click **Create Website**.
2. **Websites → List Websites → `crmrecoverysoftware.com` → Manage → Create Child Domain**
   - Child domain: `api.crmrecoverysoftware.com`
   - Click **Create Child Domain**.

After this, note the **docRoot** CyberPanel assigned — open
**List Websites → `api.crmrecoverysoftware.com` → vHost Conf** and read the
`docRoot` line. It will be something like:
```
/home/crmrecoverysoftware.com/api.crmrecoverysoftware.com
```
**Write this exact path down** — you need it in Steps D and E.

> If instead you created `api.crmrecoverysoftware.com` as a standalone website
> (not a child domain), the docRoot will be
> `/home/api.crmrecoverysoftware.com/public_html`. Either is fine — just use the
> real path the panel shows you.

---

## 5. Step C — Issue the SSL certificate

In CyberPanel: **SSL → Manage SSL → select `api.crmrecoverysoftware.com` → Issue SSL.**

Wait for "SSL issued". This creates the cert at:
```
/etc/letsencrypt/live/api.crmrecoverysoftware.com/
```
(If it fails, DNS has not propagated yet — wait and retry.)

---

## 6. Step D — OpenLiteSpeed vhost (reverse proxy to the .NET apps)

This is the key step. A freshly created site only serves static files — you must
add the proxy config so it forwards to `VKApiServer` (5002) and `VKmobileapi` (5001).

In CyberPanel: **List Websites → `api.crmrecoverysoftware.com` → vHost Conf**.

Replace the **entire** contents with the block below.
**Before saving, change two things** to match Step B:
- every `<<DOCROOT>>` → the real docRoot path you noted
- the cert paths already use `api.crmrecoverysoftware.com` — fine if you used that name

```
docRoot                   <<DOCROOT>>
vhDomain                  $VH_NAME
vhAliases                 www.$VH_NAME
adminEmails               your@email.com
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
  location                <<DOCROOT>>/downloads/
  allowBrowse             1
  addDefaultCharset       off
}

context /public/ {
  location                <<DOCROOT>>/public/
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

context /.well-known/acme-challenge {
  location                /usr/local/lsws/Example/html/.well-known/acme-challenge
  allowBrowse             1
  rewrite  {
    enable                0
  }
  addDefaultCharset       off
}

rewrite  {
  enable                  1
  autoLoadHtaccess        1
}

vhssl  {
  keyFile                 /etc/letsencrypt/live/api.crmrecoverysoftware.com/privkey.pem
  certFile                /etc/letsencrypt/live/api.crmrecoverysoftware.com/fullchain.pem
  certChain               1
  sslProtocol             24
  enableECDHE             1
  renegProtection         1
  sslSessionCache         1
  enableSpdy              15
  enableStapling          1
  ocspRespMaxAge          86400
}
```

Click **Save**, then **restart OpenLiteSpeed**:
- CyberPanel: **Server Status → Services → Restart LiteSpeed**, **or**
- SSH: `sudo systemctl restart lsws`

**Quick test:** open `https://api.crmrecoverysoftware.com/` in a browser. It must show:
```json
{"name":"VK Enterprises API Server","mode":"mysql","port":"5002"}
```
If you see that JSON, the proxy works. `/downloads/` and `/public/` will be empty until Step F.

---

## 7. Step E — Update `deploy.sh`

`deploy.sh` has the old domain's docRoot path written into it in **two places**.
Edit `/home/vkapp/deploy.sh` (e.g. `sudo nano /home/vkapp/deploy.sh`).

Find these two lines and change the path to your new docRoot:

| Old line | Change to |
|---|---|
| `DOCROOT_DOWNLOADS="/home/characterverse.tech/api.characterverse.tech/downloads"` | `DOCROOT_DOWNLOADS="<<DOCROOT>>/downloads"` |
| `DOCROOT_PUBLIC="/home/characterverse.tech/api.characterverse.tech/public"` | `DOCROOT_PUBLIC="<<DOCROOT>>/public"` |

Also update the **parent-folder permission line** (it lets the web server read
into the folder). Find:
```
chmod o+x /home/characterverse.tech /home/characterverse.tech/api.characterverse.tech 2>/dev/null || true
```
Change it to your new parent + docRoot, e.g.:
```
chmod o+x /home/crmrecoverysoftware.com <<DOCROOT>> 2>/dev/null || true
```

And the owner-detection line — find:
```
DOMAIN_OWNER=$(stat -c '%U:%G' /home/characterverse.tech/api.characterverse.tech 2>/dev/null)
```
Change the path to `<<DOCROOT>>`.

> **Cleaner option (recommended):** at the top of `deploy.sh`, next to
> `VKAPI_OUT="/opt/vkapi"`, add one line:
> `DOCROOT="<<DOCROOT>>"`
> then in the body use `${DOCROOT}/downloads` and `${DOCROOT}/public`, and
> `chmod o+x "$(dirname "$DOCROOT")" "$DOCROOT"`. One place to edit next time.

(The `https://api.characterverse.tech/...` lines in `deploy.sh` are only
log messages — cosmetic. You can update them too, but they don't affect anything.)

---

## 8. Step F — Run the deploy once

This copies the installer + download page + map assets into the new domain's
folders:
```
sudo bash /home/vkapp/deploy.sh
```
(If `dotnet` is not found, run it as:
`sudo env PATH=/usr/share/dotnet:$PATH bash /home/vkapp/deploy.sh`)

After it finishes, check:
- `https://api.crmrecoverysoftware.com/downloads/` — should list the installer
- `https://api.crmrecoverysoftware.com/public/map_live.html` — should load the map

---

## 9. Step G — Point the Desktop app at the new domain

**For computers that already have the app installed — no rebuild needed:**
Open the app → the **Settings** (gear) button → set **API Base URL** to:
```
https://api.crmrecoverysoftware.com/
```
Save. Done — that machine now uses the new domain. The desktop app stores this
per-machine, so each installed copy can be switched individually.

**To change the default for all FUTURE installs (one code change + new installer):**
- File `VKdesktopapp/Properties/Settings.settings` — the `ApiBaseUrl` value
  `https://api.characterverse.tech/` → `https://api.crmrecoverysoftware.com/`
- File `VKdesktopapp/Properties/Settings.Designer.cs` — the same default value
  in the `DefaultSettingValueAttribute` for `ApiBaseUrl`
- Then rebuild the app + rebuild the installer (`installer.iss` via Inno Setup).
- (The app server is host-agnostic — `VKApiServer` builds profile-picture and
  upload URLs from the incoming request host, so no API code change is needed.)

---

## 10. Step H — Rebuild the Android app with the new domain

The Android app's API URL is **compiled in** — there is no in-app setting, so the
APK **must be rebuilt**.

- File `android/app/build.gradle.kts`, change this one line:
  ```
  buildConfigField("String", "BASE_URL", "\"https://api.characterverse.tech/\"")
  ```
  to:
  ```
  buildConfigField("String", "BASE_URL", "\"https://api.crmrecoverysoftware.com/\"")
  ```
- Rebuild the APK (`gradlew assembleDebug` / your release build) and install it
  on the field phones. Until a phone gets the new APK it keeps using the old
  domain — so keep the old domain alive during the rollout (see Step J).

---

## 11. Step I — Verification checklist

- [ ] `ping api.crmrecoverysoftware.com` → `34.59.75.191`
- [ ] `https://api.crmrecoverysoftware.com/` → returns the API JSON, padlock valid
- [ ] `https://api.crmrecoverysoftware.com/downloads/` → installer listed
- [ ] `https://api.crmrecoverysoftware.com/public/map_live.html` → map loads
- [ ] Desktop app (Server Settings → new URL) → login works, dashboard loads
- [ ] Rebuilt Android app → login + a vehicle search works
- [ ] `/api/mobile/...` calls succeed (mobile login proves the 5001 proxy works)

---

## 12. Step J — Keep the old domain running during the switch (important)

**Do NOT delete `api.characterverse.tech` immediately.** Every field phone still
on the old APK, and every desktop not yet re-pointed, keeps using it. Both
domains can serve the same backend at the same time — they both just proxy to
the same `5002` / `5001` services.

Retire `api.characterverse.tech` only once:
- every desktop machine has been re-pointed (Step G), and
- every field phone has the rebuilt APK (Step H).

To retire it later: in CyberPanel delete the `api.characterverse.tech` child
domain, and remove the old `DOCROOT` lines from `deploy.sh`.

---

## 13. Things that DO NOT change (leave them alone)

- The two systemd services `vkapi` / `vkmobileapi`, ports `5002` / `5001`
- `/opt/vkapi` and `/opt/vkmobileapi` build folders
- The MySQL database, `/home/vkapp/db/.env.local`, the repo at `/home/vkapp`
- The `git pull` / `dotnet publish` parts of `deploy.sh`
- The server IP `34.59.75.191`

A domain switch is entirely a **web-server (OpenLiteSpeed vhost) + client-URL**
change. The application backend is unaffected.

---

## 14. One-look summary

| # | Where | Change |
|---|---|---|
| 1 | DNS provider | A record `api` → `34.59.75.191` |
| 2 | CyberPanel | Create website + child domain `api.crmrecoverysoftware.com` |
| 3 | CyberPanel | Issue SSL for it |
| 4 | OpenLiteSpeed vHost Conf | Paste the proxy config (Step D), restart LiteSpeed |
| 5 | `/home/vkapp/deploy.sh` | Update the 2 docRoot paths + 2 permission lines |
| 6 | SSH | Run `deploy.sh` once |
| 7 | Desktop app | Server Settings → new URL (per machine); optionally change the build default |
| 8 | Android app | `build.gradle.kts` `BASE_URL` → new URL, rebuild + reinstall APK |

---

*Prepared from a live read-only inspection of the production server. No server
files or application code were modified in producing this document.*
