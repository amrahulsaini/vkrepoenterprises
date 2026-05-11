# VK Enterprises — Server Setup Guide

Complete steps to deploy VKApiServer on a new server with CyberPanel + Cloudflare.

---

## Prerequisites

- Ubuntu 22.04 server
- CyberPanel installed
- Domain pointed to server IP (DNS A record)
- Cloudflare SSL mode set to **Full** (not Flexible)

---

## Step 1 — Install .NET 6

```bash
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install -y dotnet-sdk-6.0
dotnet --version
```

---

## Step 2 — Clone the repo

```bash
cd /home
git clone https://github.com/amrahulsaini/vkrepoenterprises.git vkapp
```

---

## Step 3 — Create env file

```bash
mkdir -p /home/vkapp/db
nano /home/vkapp/db/.env.local
```

Paste and fill in your values:

```
PORT=5002
MYSQL_HOST=YOUR_MYSQL_HOST
MYSQL_USER=YOUR_MYSQL_USER
MYSQL_PASSWORD=YOUR_MYSQL_PASSWORD
MYSQL_DATABASE=YOUR_MYSQL_DATABASE
MYSQL_PORT=3306
PRIVATEKEY=your_strong_secret_key
DESKTOP_LOGIN_PASSWORD=your_admin_password
```

> **Current production values** are in `/home/vkapp/db/.env.local` on the main server.
> Copy them when setting up a second server.

---

## Step 4 — Build and publish

```bash
cd /home/vkapp/VKApiServer
dotnet publish -c Release -o /opt/vkapi
```

---

## Step 5 — Create systemd service

```bash
nano /etc/systemd/system/vkapi.service
```

```ini
[Unit]
Description=VK Enterprises API
After=network.target

[Service]
WorkingDirectory=/opt/vkapi
ExecStart=/usr/bin/dotnet /opt/vkapi/VKApiServer.dll
Restart=always
RestartSec=10
User=www-data

[Install]
WantedBy=multi-user.target
```

```bash
systemctl daemon-reload
systemctl enable vkapi
systemctl start vkapi
systemctl status vkapi
```

Must show **active (running)**. If not, check logs:

```bash
journalctl -u vkapi -n 50
```

---

## Step 6 — Test locally on server

```bash
curl http://localhost:5002/
```

Expected response:
```json
{"name":"VK Enterprises API Server","mode":"mysql","port":"5002"}
```

---

## Step 7 — CyberPanel vHost config

Go to: **CyberPanel → Websites → List Websites → YOUR_SUBDOMAIN → vHost Conf**

Replace the entire config with this (change `YOUR_DOMAIN` to your actual domain):

```apache
docRoot                   /home/YOUR_DOMAIN/api.YOUR_DOMAIN
vhDomain                  $VH_NAME
vhAliases                 www.$VH_NAME
adminEmails               your@email.com
enableGzip                1
enableIpGeo               1

index  {
  useServer               0
  indexFiles              index.html
}

errorlog $VH_ROOT/logs/YOUR_DOMAIN.error_log {
  useServer               0
  logLevel                WARN
  rollingSize             10M
}

accesslog $VH_ROOT/logs/YOUR_DOMAIN.access_log {
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

context /api/mobile {
  type                    proxy
  handler                 mobileapi
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
  keyFile                 /etc/letsencrypt/live/api.YOUR_DOMAIN/privkey.pem
  certFile                /etc/letsencrypt/live/api.YOUR_DOMAIN/fullchain.pem
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

Click **Save** → **Graceful Restart**

---

## Step 8 — Final test

Open browser → `https://api.YOUR_DOMAIN/`

Should return:
```json
{"name":"VK Enterprises API Server","mode":"mysql","port":"5002"}
```

---

## Changing the domain in the desktop app

Open the WPF app → **Server Settings** → update **API Base URL** to:
```
https://api.YOUR_DOMAIN/
```

Or to permanently change the default for new installs, edit:
`VKdesktopapp/Properties/Settings.settings` and `Settings.Designer.cs`
— change `https://api.characterverse.tech/` to your new domain.

---

## DB migration — add GPS columns to app_users

Run this once on the server if `last_lat`, `last_lng`, `last_seen` columns don't exist yet:

```bash
mysql -u vkre_db1 -pdb1 vkre_db1 -e "
ALTER TABLE app_users
  ADD COLUMN IF NOT EXISTS last_seen DATETIME NULL,
  ADD COLUMN IF NOT EXISTS last_lat  DOUBLE    NULL,
  ADD COLUMN IF NOT EXISTS last_lng  DOUBLE    NULL;
"
```

---

## Updating code on server

```bash
cd /home/vkapp
git pull origin main
cd VKApiServer
dotnet publish -c Release -o /opt/vkapi
systemctl restart vkapi
```

---

## Desktop app login credentials

| Field    | Value                                      |
|----------|--------------------------------------------|
| Mobile   | Any 10-digit number registered in `users` table |
| Password | Value of `DESKTOP_LOGIN_PASSWORD` in `/home/vkapp/db/.env.local` |

> Current password is set in the server's `.env.local` file.

---

## Cloudflare settings

| Setting       | Value                    |
|---------------|--------------------------|
| SSL/TLS mode  | **Full** (not Flexible)  |
| Cache (API)   | Page Rule: `api.*` → Cache Level: Bypass |
| DNS           | A record → server IP, orange cloud ON |
Note: whenever you update the env file in future, remember to copy it again: cp /home/vkapp/db/.env.local /opt/vkapi/db/.env.local && systemctl restart vkapi