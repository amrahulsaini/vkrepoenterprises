# Multi-Tenancy Research — One Database Per Agency

> **Question.** Each agency that uses the software should have its own
> database. When a new agency registers, can the software automatically
> create a fresh database for them?
>
> **Short answer.** **Yes, fully feasible.** It is a standard SaaS pattern
> ("database-per-tenant"). I verified end-to-end on the live server that a
> brand-new database + dedicated user can be created from a single SQL
> session, the new user can connect and create tables, and the user is
> isolated from every other database. The application changes are moderate
> (mostly routing logic + a registry), the infrastructure changes are
> small but specific.

---

## 1. What I verified on the live server (not theory)

| Check | Result |
|---|---|
| MariaDB version | **10.11.16** on the production VM (Ubuntu 22.04) |
| Root access | `sudo mysql -u root` works via local socket (no password prompt on the box). CyberPanel also stores the root password at `/etc/cyberpanel/mysqlPassword` |
| Current databases | `vkre_db1` (772 MB — the only app DB), plus CyberPanel/system DBs |
| Existing app user privileges | `vkre_db1@localhost` has `ALL` on `vkre_db1.*` only — **it cannot CREATE DATABASE or GRANT**. Tenant provisioning needs a separate admin account. |
| Feasibility test | Created `crmre_test_tenant` DB + dedicated MySQL user in one SQL block. The new user connected, created a table, inserted, queried, **and could not see any other DB**. Cleaned up. ✓ |
| Schema size | The whole `vkre_db1` schema (15 tables + 1 view, with PKs, FKs, indexes) = **440 lines / ~23 KB of DDL**. Trivial to apply to each new tenant. |
| Current DB size | 772 MB total; dominated by `vehicle_records` (393 MB), `rc_info` (181 MB), `chassis_info` (195 MB) — all per-agency vehicle data. Other tables are tiny. |
| `max_connections` | **151** — this is going to be the binding constraint as tenants multiply. |
| `innodb_buffer_pool_size` | **128 MB** — far smaller than even the current single tenant's working set; will need raising significantly. |
| `wait_timeout` | 28 800 s (8 h). Fine. |

### Concrete SQL that worked end-to-end

```sql
-- One transaction, runs as MySQL root:
CREATE DATABASE crmre_test_tenant
    CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
CREATE USER 'crmre_test_tenant'@'localhost'
    IDENTIFIED BY 'tempPwd_1234!';
GRANT ALL PRIVILEGES ON crmre_test_tenant.*
    TO 'crmre_test_tenant'@'localhost';
FLUSH PRIVILEGES;
```

Then as the new user:
```
$ mysql -u crmre_test_tenant -ptempPwd_1234! crmre_test_tenant
mysql> CREATE TABLE _check (id INT PRIMARY KEY, note VARCHAR(50));
mysql> INSERT INTO _check VALUES (1, 'tenant-provisioning-works');
mysql> SELECT * FROM _check;
id | note
 1 | tenant-provisioning-works
```

`SHOW DATABASES;` for that user returned only `crmre_test_tenant` +
`information_schema` — **`vkre_db1` was invisible**. Isolation works.

---

## 2. Three architecture patterns — pick one

| Pattern | What it is | Pros | Cons |
|---|---|---|---|
| **A. Database-per-tenant** *(what you asked about)* | Each agency = its own MySQL DB, same schema, dedicated MySQL user. | Strongest isolation; easy delete/export/backup per agency; per-tenant performance independence; legal/regulatory clarity (one bank's data never even sits in another's tables); easy to move one big agency to its own server later. | Schema migrations must run against EVERY tenant DB; more MySQL connections needed (pool per tenant); admin-level MySQL user required at provisioning time; slightly more operational complexity. |
| **B. Schema-per-tenant** *(MySQL: same as A)* | In MySQL "schema" == "database", so this is identical to A. Don't think of it as a separate option. | — | — |
| **C. Shared DB + `tenant_id` column** | One DB. Every business table gets a `tenant_id`. Every query is filtered by it. | One DB to back up; one schema migration; lowest infra cost. | Cross-tenant data leak is one missing `WHERE tenant_id =` away — riskiest model; large tables get even larger; one noisy tenant degrades everyone; deleting an agency is a careful per-table cascade. |

**For a vehicle-recovery service holding KYC and bank details for multiple
agencies, option A is the right call.** The isolation matters legally
(your bank vendor questionnaire — Annexure F Data Protection — is far easier
to defend when one agency literally cannot see another agency's tables).
And practically: agencies have very different data sizes; isolating them
keeps a big one from hurting a small one.

The rest of this document assumes pattern A.

---

## 3. Recommended architecture for VK / CRM Recovery

```
                                      ┌──────────────────────────┐
   Desktop/Mobile/Web client          │  PRIMARY  ("master") DB  │
            │                         │       crm_master         │
            │  HTTPS                  │                          │
            ▼                         │  ┌────────────────────┐  │
     api.crmrecoverysoftware.com      │  │ agencies           │  │
       (OpenLiteSpeed proxy)          │  │  id  PK            │  │
            │                         │  │  name              │  │
            ▼                         │  │  slug / subdomain  │  │
   VKApiServer / VKmobileapi          │  │  db_name           │  │
                  │                   │  │  db_user           │  │
                  │ (tenant lookup    │  │  db_password_enc   │  │
                  │  by agency_id /   │  │  status            │  │
                  │  user → agency)   │  │  created_at        │  │
                  │                   │  └────────────────────┘  │
                  │                   │  global_admins, billing, │
                  │                   │  feature_flags, …        │
                  ▼                   └─────────────┬────────────┘
       ┌──────────────────┐                         │ writes
       │ connection cache │                         │
       │  per-tenant      │                         ▼
       └────┬─────────────┘     ┌──────────────────────────────────────┐
            │                   │  PER-TENANT DATABASES (one each)     │
            ├──── agency 1 ───► │  agency_1   ←  app_users, vehicle_   │
            │                   │  agency_2      records, finances,    │
            ├──── agency 2 ───► │  agency_3      branches, kyc, etc.   │
            │                   │  …            (the schema we already │
            └──── …               │              have, applied to each)│
                                  └──────────────────────────────────────┘
```

Two layers of database:

### 3.1 The master DB (`crm_master`) — single, central
Owns the cross-tenant truth. Tables (rough):
- `agencies` — `id`, `name`, `slug`, `db_name`, `db_user`, `db_password_enc`,
  `status`, `created_at`, `plan_id`, contact, etc.
- `agency_admins` *(optional)* — global super-admin accounts that can log
  into multiple agencies; this is YOUR control panel.
- `plans` / `billing` *(later)*.
- `provisioning_logs` — audit trail of "agency X provisioned at time T".

This DB is small forever — a few rows per agency. The application connects
to it with a **read-only-ish app user** (`crm_master@localhost` with `SELECT`
+ targeted `UPDATE` rights on `agencies`).

### 3.2 Per-tenant DBs (`agency_<id>`) — one per agency
Identical schema to the current `vkre_db1` (the 440 lines of DDL). Each has:
- Its own dedicated MySQL user (`agency_<id>@localhost`) with `ALL PRIVILEGES`
  on its DB only.
- A random per-tenant password generated at provisioning, **stored encrypted
  in the master DB** (encrypted with a server-side key — never in plaintext,
  never in app code).

### 3.3 The privileged "provisioner" account
A separate MySQL user — `crm_provisioner@localhost` — used **only** by the
agency-registration endpoint. Privileges:
```sql
GRANT CREATE, DROP, GRANT OPTION,
      ALTER, INDEX, CREATE VIEW,
      CREATE ROUTINE, ALTER ROUTINE, EVENT,
      RELOAD                         -- for FLUSH PRIVILEGES
  ON *.* TO 'crm_provisioner'@'localhost';
```
Or simpler but blunter:
`GRANT ALL ON *.* TO 'crm_provisioner'@'localhost' WITH GRANT OPTION;`

Used ONLY at provisioning time. Normal requests still use the per-tenant
limited user.

---

## 4. Auto-provisioning flow (what runs when a new agency registers)

A typical `POST /api/agencies/register` handler does:

```text
1.  Validate input (agency name, contact, contact mobile/email).
2.  Generate a tenant slug + tenant id (e.g. agency_42).
3.  Generate a random 24-char password for the new MySQL user.

4.  Open a MySQL connection as crm_provisioner.

5.  Execute (with proper identifier quoting):
        CREATE DATABASE `agency_42`
            CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
        CREATE USER `agency_42`@`localhost` IDENTIFIED BY '<random>';
        GRANT ALL PRIVILEGES ON `agency_42`.* TO `agency_42`@`localhost`;
        FLUSH PRIVILEGES;

6.  Apply the schema template:
        mysql -u agency_42 -p<random> agency_42 < /opt/vkapi/tenant_template.sql
    (the 23 KB DDL file generated from mysqldump --no-data)

7.  Insert into the master registry:
        INSERT INTO crm_master.agencies
            (name, slug, db_name, db_user, db_password_enc, status, …)
            VALUES (…, 'agency_42', 'agency_42', encrypt('<random>'), 'active', …);

8.  (Optional) Seed: insert the first super-admin user into agency_42.app_users.

9.  Return { agencyId, message } to the client.
```

Total time at scale: <1 second. The 23 KB DDL applies in well under 200 ms.

### Failure handling
Every step must be reversible:
- If schema apply fails → drop the database + drop the user → roll back.
- If master insert fails → drop the DB + user → roll back.
- Wrap the provisioner work in a try/catch with cleanup on any error.

---

## 5. Connection routing — how a normal API request finds its DB

```text
client → API request (with auth header)
                │
                ▼
        Resolve user → agency_id
        (from JWT/session OR a master-DB lookup
         on a user-mobile-to-agency mapping)
                │
                ▼
        Look up tenant connection-info from master.agencies
        (cached in memory for ~10 minutes per agency)
                │
                ▼
        Get-or-open a connection from a per-tenant pool
        (cap: 5–10 connections per active tenant)
                │
                ▼
        Run the user's query against agency_<n>
```

Caching the master lookup is essential — you don't want to read
`crm_master.agencies` on every request. Refresh on a TTL or on agency
status change.

---

## 6. What changes in the codebase (high level — no code edited here)

The current `VKApiServer` / `VKmobileapi` both:
- Read the MySQL connection string from env (`MYSQL_HOST`, `MYSQL_USER`, etc.).
- Open new connections per query (via `MySqlConnection(connStr)`).

To support tenants the minimum changes are:

1. **A `TenantResolver`** — given an incoming request (its `X-User-Id`,
   `X-Api-Key`, or a new `X-Agency-Id` header / JWT claim), return the
   tenant's connection string.
2. **A `MasterDbContext`** — a fixed connection to `crm_master`. Used by
   `TenantResolver`, registration endpoint, master-admin endpoints.
3. **A `TenantConnectionFactory`** — every place that today does
   `new MySqlConnection(connStr)` instead calls
   `tenantFactory.OpenAsync(agencyId)`. The factory caches builders per agency.
4. **A new public endpoint** — `POST /api/master/agencies` (auth = a
   super-admin token, not a tenant user). It runs the provisioning flow.
5. **A schema template file** shipped with the deployment (e.g.
   `/opt/vkapi/tenant_template.sql`) and a small **migration table** in each
   tenant DB (see §7).

A staged rollout is possible: keep the existing `vkre_db1` flow as the
"default" tenant during transition; once code is wired, migrate existing
data into `agency_1` and decommission the env-var-based connection.

---

## 7. Schema migrations across many tenants

The thing that bites teams that adopt DB-per-tenant: **how do you evolve
the schema once you have 50 tenants?**

Standard solution: a tiny **migration table inside every tenant DB**:

```sql
CREATE TABLE schema_migrations (
    version INT PRIMARY KEY,
    applied_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);
```

The application ships a folder of numbered SQL files:
```
db/migrations/
    001_initial.sql         -- the 440-line dump (the template)
    002_add_admin_pass.sql
    003_add_bank_details.sql
    …
```

On startup (and again as a `POST /api/master/migrate` endpoint):
- Read `crm_master.agencies` → list every tenant DB.
- For each tenant, connect and check `schema_migrations.MAX(version)`.
- Apply any newer migration files in order, in a transaction per file.
- Record the new version in `schema_migrations`.

This is well-trodden territory — there are libraries that do it
(Flyway, DbUp, EvolveDb, etc.) but a 50-line custom runner is fine too.
The same migration runner is what onboards a brand-new tenant: start from
0, apply 001, 002, 003, … done.

---

## 8. Server-side changes required first

Before turning multi-tenant ON, these MUST be raised on the production VM:

| Setting | Current | Recommended starting point | Why |
|---|---:|---:|---|
| `max_connections` | 151 | **500–1000** | Each tenant pool holds ~5–10 idle connections. 50 tenants × 5 = 250 just for pools. |
| `innodb_buffer_pool_size` | 128 MB | **At least 40–60 % of VM RAM** | Index/hot-data caching; today's single tenant is already 772 MB and growing. |
| `max_user_connections` | 0 (unlimited) | Optionally **20–50 per user** | Caps any one tenant from exhausting the pool. |
| `table_open_cache` | 2000 | **4000+** | Per-tenant DBs mean N× tables open at once. |

These go in `/etc/mysql/mariadb.conf.d/50-server.cnf` followed by
`systemctl restart mariadb`. The CyberPanel UI also has a Database Tuner.

Disk growth: today vkre_db1 is 772 MB for one tenant. Plan for ~500 MB–2 GB
per active tenant on average; check `df -h /` before signing each new
client, and enable GCP VM disk auto-resize.

---

## 9. Backup / DR per tenant (now actually clean)

This is the biggest unsung win of DB-per-tenant:

- **Per-tenant backup** = `mysqldump <db>` — one file per agency.
- **Per-tenant restore** = `mysql <db> < backup.sql` — restoring one agency
  does not touch any other.
- **Per-tenant delete** = `DROP DATABASE` + `DROP USER` — done, all of
  their PII is gone, easy to prove to a regulator.
- **Per-tenant export** *(GDPR/DPDP "data portability")* = `mysqldump <db>`
  → ship the file.

Add a daily cron job that loops `crm_master.agencies` and dumps each one
into `/home/vkapp/db/backups/agency_<id>.<date>.sql.gz`. Snapshot the whole
disk weekly via GCP for redundancy.

---

## 10. Capacity / scaling — back-of-envelope

Assuming each agency averages ~1 GB of data and a moderate query load:

| Tenants | Disk | Connections | Notes |
|---:|---:|---:|---|
| 10 | ~10 GB | ~100 | Easy on the current VM (with `max_connections` raised). |
| 50 | ~50 GB | ~500 | Need larger VM disk, possibly more RAM (raise buffer pool). |
| 200+ | ~200 GB+ | 2000+ | Worth moving to a managed MySQL (Cloud SQL) or sharding across multiple VMs. |

Database-per-tenant *makes the move easier*, not harder: at 200 tenants you
can shard "tenants 1–100 on server A, 101–200 on server B" by changing
`crm_master.agencies.db_host` — no application rewrite needed.

---

## 11. Risks & open questions

- **The current "admin" auth model is global** (`X-Api-Key: 12` for the
  desktop manager). In a multi-tenant world there are two layers:
  *agency admin* (manages users inside their agency) and *super admin*
  (you — manages agencies themselves). The auth scheme needs to grow a
  `agency_id` claim. Worth designing before coding starts.
- **Cross-tenant search**, if you ever need it, becomes a federation
  problem instead of a `WHERE`. For VK Enterprises it's probably not
  needed — agencies don't share vehicle data.
- **Per-tenant subdomains** (e.g. `acme.crmrecoverysoftware.com`) are a
  nice UX but each subdomain needs an OpenLiteSpeed vhost. With a wildcard
  cert and one wildcard vhost that proxies to the same API service this
  becomes trivial. Recommend for v2.
- **Schema drift across tenants** is the standard footgun — only a
  disciplined migration runner (§7) keeps every tenant on the same schema.
- **Tenant deletion** must actually cascade. A `DROP DATABASE` is final;
  belt-and-braces: snapshot the DB to backup *first*, then drop.

---

## 12. Recommended next steps (in order)

1. **Decide:** confirm pattern A (DB-per-tenant). One sentence — yes/no.
2. **One-time server prep** (no app code yet):
   - Raise `max_connections` to 500 and `innodb_buffer_pool_size` to ~2 GB
     (or whatever fits in 50 % of VM RAM); restart MariaDB.
   - Create the `crm_master` database + the `crm_provisioner` MySQL user.
3. **In the codebase, in this order:**
   1. Add a `MasterDb` connection + `agencies` table.
   2. Add a `TenantConnectionFactory` (just a wrapper around
      `MySqlConnection` that uses the tenant's connection string).
   3. Pipe every existing repository method through the factory.
      `vkre_db1` becomes "tenant 1" during transition.
   4. Add the migration runner with the existing schema as `001_initial.sql`.
   5. Add the `POST /api/master/agencies` registration endpoint.
   6. Add tenant-aware auth (`agency_id` in the session/JWT).
4. **Operational:**
   - Daily per-tenant `mysqldump` cron.
   - Update the docs (`SERVER_INFRASTRUCTURE.md` already covers the
     plumbing; add a tenants section once this lands).

---

## 13. Bottom line

Database-per-tenant is the correct fit for this product. The MySQL side is
trivial and **verified working** on the existing server. The application
side is moderate, well-scoped work — the existing repository pattern means
the change is "swap one connection string for many", not a rewrite.

No changes have been made to application code or to any production state
in writing this report — only the throwaway feasibility test (which is
already cleaned up).
