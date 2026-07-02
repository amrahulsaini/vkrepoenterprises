# Careers module — deploy & operations

A self-contained PHP + MariaDB careers module that lives inside `main-site` and stores
its data in the existing `crm_master` database using prefixed tables (`careers_jobs`,
`careers_applications`, `careers_admins`). It is served from the root domain
(`https://crmrecoverysoftware.com`) and does not touch the .NET APIs.

## URLs

| Page | Path |
|------|------|
| Public listing | `/careers.php` (old `/careers.html` redirects here) |
| Public job + apply | `/careers/job.php?id=<id>` |
| Admin panel | `/careers/admin/` (login required) |

## Requirements on the server

- The `crmrecoverysoftware.com` vHost must serve **PHP** (CyberPanel/OpenLiteSpeed ship LSPHP;
  the root site is otherwise static, so confirm the PHP handler is enabled for this vHost).
- MariaDB reachable locally with access to `crm_master`.
- `careers/uploads/resumes/` must be writable by the web user and is excluded from git.

## One-time database migration (on the server)

Apply the schema to the existing master DB and seed a super admin:

```bash
mysql crm_master < /home/crmrecoverysoftware.com/public_html/careers/schema.sql

# seed a super admin (replace the password)
HASH=$(php -r "echo password_hash('CHANGE_ME_STRONG', PASSWORD_DEFAULT);")
mysql crm_master -e "INSERT INTO careers_admins (user_name, password, admin_type)
  VALUES ('admin', '$HASH', 'super')
  ON DUPLICATE KEY UPDATE password=VALUES(password);"
```

The tables are prefixed, so they coexist with the existing `crm_master` registry tables.

## Database credentials

`careers/config.php` reads, in order:
1. `CAREERS_DB_HOST/USER/PASSWORD/NAME/PORT` environment variables, else
2. `careers/config.local.php` (git-ignored) if present, else
3. local defaults (`127.0.0.1`, `root`, no password, `crm_master`) for dev.

For production, copy `config.local.example.php` to `config.local.php` on the server and fill in
the `crm_master` DB user/password (e.g. `crm_master_app` from `/home/vkapp/db/.env.local`).
`config.local.php` is **not** committed — create it on the box once; it survives redeploys because
the runbook copies files in without deleting it.

## Deploy (fits RUNBOOK §5.2)

From your machine:

```bash
git add -A && git commit -m "careers module" && git push origin main
```

On the server, the existing main-site step already publishes this module:

```bash
sudo git -C /home/vkapp pull origin main
sudo cp -r /home/vkapp/main-site/. /home/crmrecoverysoftware.com/public_html/
# make uploads writable and re-fix ownership to the site user
sudo mkdir -p /home/crmrecoverysoftware.com/public_html/careers/uploads/resumes
o=$(stat -c '%U:%G' /home/crmrecoverysoftware.com/public_html)
sudo chown -R "$o" /home/crmrecoverysoftware.com/public_html
```

## Verify

```bash
curl -sI https://crmrecoverysoftware.com/careers.php        # 200
curl -sI https://crmrecoverysoftware.com/careers/admin/     # 302 -> login.php
```

Then sign in at `/careers/admin/`, add a job, and submit a test application from the public page.

## Local development

Served at web root via PHP's built-in server so root-absolute links resolve:

```bash
php -S 127.0.0.1:8080 -t main-site
```

Local DB is XAMPP MariaDB (`crm_master`, user `root`, no password). Local admin seeded as
`admin` / `Crmrs@2026` — change or remove before anything public.
