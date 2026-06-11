# CRMRS — Whole-Codebase Deep Analysis

> Single document covering the **WPF desktop app**, the **Android agent app**, the **two .NET APIs**, and the **multi-tenant MySQL/MariaDB** behind them. For every page / screen / CRUD action it lists: where the code lives, which API route it calls, how data is fetched, which table(s) it touches, how it is secured, and which actions have **multiple side-effects**. It ends with a live-server DB inspection and a security assessment.
>
> _Compiled from source + a live read-only SSH inspection of `103.67.239.102` (per [RUNBOOK.md](RUNBOOK.md)). Date: 2026-06-08._

---

## 0. TL;DR

- **Architecture:** Multi-tenant SaaS. One Linux box (CyberPanel + OpenLiteSpeed) runs **two .NET services** behind an HTTPS reverse proxy and **one MariaDB** with **one database per agency** (`crmr_<slug>`) plus a master registry (`crm_master`).
- **Clients:** A **WPF/.NET 6 desktop** ("CRMRS.exe") for agency admins, and a **Kotlin/Jetpack-Compose Android app** for field recovery agents (white-labelled per agency).
- **Two APIs:**
  - `VKApiServer` (:5002) — desktop admin (`/api/mgr/*`), agency portal + manage portal (`/api/agency/*`), legacy showcase (`/api/*`).
  - `VKmobileapi` (:5001) — the phone app (`/api/mobile/*`).
- **Tenancy:** A signed token (HMAC) on each request selects the tenant DB for that request's lifetime. The desktop additionally sends a shared static `X-Api-Key`.
- **Live data (V K Enterprises tenant):** 6 app users, 1,724 branches, 135 finances, **3.9 M vehicle records**, 355 search logs.
- **Security headline:** DB-layer tenant isolation is correctly scoped and all service/DB ports are bound to localhost only (good). The master signing/derivation secret (`TENANT_DB_SECRET`) **was** running on its hardcoded default — **this was rotated to a strong random value on 2026-06-09** (see [§9](#9-security-assessment) finding #1, now ✅ fixed). Remaining quick wins: the manage password and `X-Api-Key` are still weak/static.

---

## 1. Where the code lives (repository map)

| Path | Component | Language / Tech |
|---|---|---|
| [VKdesktopapp/](VKdesktopapp/) | **WPF desktop admin app** (CRMRS.exe) | C# / .NET 6 / WPF / Syncfusion |
| [VKApiServer/](VKApiServer/) | **Desktop + portal API** (port 5002, svc `vkapi`) | C# / ASP.NET Minimal API / MySqlConnector |
| [VKmobileapi/](VKmobileapi/) | **Mobile API** (port 5001, svc `vkmobileapi`) | C# / ASP.NET Controllers |
| [android/](android/) | **Android agent app** (per-agency APK) | Kotlin / Compose / Hilt / Retrofit / Room |
| [dbschema/](dbschema/) | SQL schema: tenant template + master | SQL (MariaDB) |
| [db/](db/) | Local env + legacy Mongo seed (dormant) | — |
| [agency-portal/](agency-portal/), [manage-portal/](manage-portal/), [main-site/](main-site/) | Static web frontends (agency / admin / marketing) | HTML/CSS/JS |
| [deploy.sh](deploy.sh) | One-shot build+deploy of both APIs + static sites | bash |
| [RUNBOOK.md](RUNBOOK.md) | Ops runbook (SSH, deploy, secrets index) — git-ignored | — |

### Desktop sub-folders (each maps to a UI module)
`AppUsers/`, `Blacklist/`, `Confirmations/`, `DirectData/`, `Exports/`, `Feedbacks/`, `Finances/`, `Records/`, `Reports/`, `Utilities/`, `Material/` (styles), `Models/`, `Data/` (the API client + repositories). The single networking choke-point is [VKdesktopapp/Data/DesktopApiClient.cs](VKdesktopapp/Data/DesktopApiClient.cs).

### Android packages
`ui/screens/` (Compose screens), `viewmodel/`, `data/api/` (Retrofit `ApiService` + `ApiClient`), `data/repository/`, `data/local/` (Room offline cache), `workers/` (background sync + GPS), `utils/`.

---

## 2. Runtime architecture & request routing

```
 Android agent app  ─┐                          ┌── :5001  VKmobileapi   /api/mobile/*
 (per-agency APK)    ├─▶ api.crmrecoverysoftware.com (HTTPS, OpenLiteSpeed reverse proxy)
 WPF admin desktop  ─┘        │                  └── :5002  VKApiServer   /api/mgr|agency/*
 (CRMRS.exe)                  ▼
                       MariaDB (127.0.0.1:3306)
                         ├── crm_master                (registry: agencies, OTPs, registry)
                         ├── crmr_v_k_enterprises      (tenant data)
                         └── crmr_rk_enterprises        (tenant data)
```

**Verified live:** MariaDB (3306) and both dotnet services (5001/5002) listen **only on 127.0.0.1** — they are unreachable from the internet directly; only the OLS proxy fronts them over TLS.

### How a request is routed to the right tenant DB

Both APIs use the same pattern — an `AsyncLocal` "current connection string" that defaults to the legacy DB and is swapped per-request when a valid signed token is present.

- **Desktop / `VKApiServer`** — [VKApiServer/Program.cs:67-92](VKApiServer/Program.cs#L67-L92), [VKApiServer/TenantContext.cs](VKApiServer/TenantContext.cs):
  - The desktop sends a **Bearer token** (format `agt1.<payload>.<hmac>`, payload = `agencyId|slug|expiry`).
  - Middleware verifies the HMAC offline (no DB hit), reads the slug, and sets `TenantContext.Conn` to `crmr_<slug>` using DB user `tu_<slug>` with a password **derived** from the slug — so routing needs no master lookup.
  - An invalid `agt1.` token → `401` (never silently falls back to the legacy DB).
- **Mobile / `VKmobileapi`** — [VKmobileapi/Program.cs:75-96](VKmobileapi/Program.cs#L75-L96), [VKmobileapi/TenantContext.cs](VKmobileapi/TenantContext.cs):
  - The app sends `X-Tenant-Token` (format `mt1.<slug|expiry>.<hmac>`), issued at login.
  - A handful of endpoints are "tenant-bound-by-body" and need no token (`register`, `login`, `agencies`, `otp/*`, `check-mobile`, `kyc/aadhaar/*`, `kyc/resubmit`) — they set the tenant from the request body's slug.
  - Any other `/api/mobile/*` without a valid token → `401`.

### Auth models at a glance

| Surface | Auth mechanism | Where checked |
|---|---|---|
| Desktop `/api/mgr/*` | **Static shared `X-Api-Key`** (`MgrAuth`) **+** agency Bearer token (selects tenant) | [Program.cs:608-609](VKApiServer/Program.cs#L608-L609) |
| Desktop self-profile / tickets `/api/agency/desktop/*` | Agency Bearer token only (`VerifyAgencyBearer`) | [AgencyPortal.cs:180-187](VKApiServer/AgencyPortal.cs#L180-L187) |
| Manage portal `/api/agency/manage/*` | Hardcoded password → opaque 64-char `manage_token` (12 h) | [AgencyPortal.cs:374-450](VKApiServer/AgencyPortal.cs#L374-L450) |
| Agency web portal `/api/agency/web/*` | Email+password **then email OTP (2FA)** → Bearer | [AgencyPortal.cs:1204-1283](VKApiServer/AgencyPortal.cs#L1204-L1283) |
| Mobile `/api/mobile/*` | `X-Tenant-Token` (tenant) + `X-User-Id` (identity) | [VKmobileapi/Program.cs:75-96](VKmobileapi/Program.cs#L75-L96) |
| Mobile admin actions | Above **+** server-side `is_admin` check (`IsAdminAsync`) | [MobileController.cs](VKmobileapi/Controllers/MobileController.cs) |

> **Important nuance on `X-Api-Key`:** `MgrAuth` compares the header against `DESKTOP_LOGIN_PASSWORD` (env). The same value is used both as the desktop admin **API key** and as the legacy `/api/AppUsers/Login` password. It is a single **global** secret shared by every agency's desktop install (the RUNBOOK documents it as `12`). It does **not** by itself grant tenant access — the Bearer token selects the tenant — but it is a weak, shared, non-rotating factor (see [§9](#9-security-assessment)).

---

## 3. Databases

Multi-tenant: **one DB per agency** + a master registry. App code talks to MariaDB via the MySqlConnector library with **parameterised queries throughout** (no string-concatenated user input in the live `/api/mgr` and `/api/mobile` paths — search term is the one place a `LIKE %term%` is parameter-bound).

### 3.1 `crm_master` — cross-agency registry ([dbschema/crm_master.sql](dbschema/crm_master.sql))

| Table | Purpose | Key columns |
|---|---|---|
| `agencies` | One row per agency (tenant) | `id, name, slug, mobile1/2, mobiles_extra, address, logo_path, email1/2, password_hash, db_name, db_user, status(pending/approved/rejected/suspended)` |
| `agency_otps` | Short-lived email OTP codes | `email, code, purpose(register/login/manage), expires_at, consumed` |
| `manage_sessions` | Opaque tokens for the `/manage` gate | `token(char64), expires_at` |
| `app_user_registry` | **Cross-agency uniqueness** of mobile-app users | `mobile UNIQUE, device_id, agency_slug` |
| `support_tickets`, `support_ticket_messages` | Agency↔admin support threads | (created lazily / by portal) |
| `client_error_log` | Central capture of desktop client errors | created via `CREATE IF NOT EXISTS` ([AgencyPortal.cs:137-159](VKApiServer/AgencyPortal.cs#L137-L159)) |

The `app_user_registry.mobile UNIQUE` constraint is the hard guarantee that **one mobile number belongs to exactly one agency** across the whole platform.

### 3.2 Tenant DB `crmr_<slug>` — per agency ([dbschema/tenant_template.sql](dbschema/tenant_template.sql))

| Table | Purpose |
|---|---|
| `app_users` | Mobile agents: identity, device binding, flags (`is_active/is_admin/is_stopped/is_blacklisted`), `balance`, bank, **KYC columns** (`kyc_aadhaar_*`, `kyc_pan_*`, `kyc_bank_*`, `kyc_status`, `kyc_reject_note`), `admin_pass`, `last_seen/last_lat/last_lng` |
| `user_kyc` | KYC document image paths (aadhaar front/back, pan, selfie, aadhaar photo) |
| `subscriptions` | Per-agent paid access windows (`start_date, end_date, amount, notes`) |
| `user_finance_restrictions` | Restrict an agent to specific financiers |
| `device_change_requests` | Pending device re-bind requests (UNIQUE per user) |
| `finances` | Financiers/banks (parent of branches) |
| `branches` | Per-financier branches + contacts + `total_records` cache |
| `vehicle_records` | The big one — full repossession record (32+ fields). **3.9 M rows** in V K. |
| `rc_info` / `chassis_info` | Search-index side tables: `last4(rc)` / `last5(chassis)` → `vehicle_record_id` |
| `search_logs` | Every vehicle detail an agent opens, with GPS + reverse-geocoded address |
| `column_types` / `column_mappings` | Excel-header → canonical-field mapping for uploads |
| `app_settings` | Key/value (e.g. subscription password, control-panel password) |
| `webhook_banks` / `webhook_files` / `webhook_users` | "Direct Data" provider-push ingestion |
| `v_finance_summary` (view), `sp_create_*` / `sp_seed_vehicles` (procs) | Convenience aggregates / seeding |

**Search design (important):** `vehicle_records` holds the data; `rc_info.last4` and `chassis_info.last5` are denormalised index tables so the phone can search by the **last 4 of a number plate** or **last 5 of a chassis** with an indexed equality lookup, then fetch the full record by id on tap.

### 3.3 Legacy `vkre_db1` — **does not exist on the server** (dead default)

`vkre_db1` is only a **hardcoded default connection-string fallback** in code ([Program.cs:36-49](VKApiServer/Program.cs#L36-L49), [DbFactory.cs:13-16](VKmobileapi/Data/DbFactory.cs#L13-L16)). **Verified live: there is no `vkre_db1` database on the server** — the only databases are `crm_master`, `crmr_v_k_enterprises`, `crmr_rk_enterprises` (+ system DBs). Because every real desktop/mobile request carries a tenant token, the connection is always swapped to `crmr_<slug>` and the fallback is never used. The **legacy `/api/*` endpoints** (`/api/Overview`, `/api/Confirmations`, `/api/Feedbacks`, `/api/Payments`, `/api/OTPs`, `/api/Uploads`, `/api/Modules/*`, `/api/Records/*`) that read this schema via [VKApiServer/DashboardRepository.cs](VKApiServer/DashboardRepository.cs) are therefore **dead code** — they'd hit tables that don't exist in the tenant DBs (and a connection that doesn't exist for untenanted calls). **Net effect: the live functional surface is `/api/mgr/*` and `/api/agency/*` only.** Safe to delete the legacy endpoints + their desktop pages. Called out per-page in [§5](#5-wpf-desktop-app--page-by-page).

---

## 4. Complete API route inventory

### 4.1 `VKApiServer` — `/api/mgr/*` (desktop admin; `X-Api-Key` + tenant Bearer)
Source: [VKApiServer/Program.cs](VKApiServer/Program.cs)

| Method & route | Action | Table(s) |
|---|---|---|
| `GET /api/mgr/finances` | List financiers + counts | `finances`, `branches` |
| `POST/PUT/DELETE /api/mgr/finances[/{id}]` | CRUD financier | `finances` |
| `GET /api/mgr/branches[?financeId=]`, `/branches/{id}` | List/detail branches | `branches`, `finances` |
| `POST/PUT /api/mgr/branches[/{id}]` | Create/update branch | `branches` |
| `POST /api/mgr/branches/{id}/clear` | **Purge all records of a branch** | `vehicle_records`(+`rc_info`/`chassis_info` cascade) |
| `DELETE /api/mgr/branches/{id}` | **Delete branch + its records** | `branches`, `vehicle_records`… |
| `GET /api/mgr/users` (+`/picker`,`/stats`,`/all-simple`) | App-user lists + stats | `app_users`, `subscriptions` |
| `PATCH /api/mgr/users/{id}/active\|admin\|stopped\|blacklisted` | Toggle agent flags | `app_users` |
| `POST /api/mgr/users/{id}/reset-device` | Clear device binding | `app_users`, `device_change_requests` |
| `DELETE /api/mgr/users/{id}` | **Delete agent (+registry cleanup)** | `app_users`, `crm_master.app_user_registry` |
| `GET/PUT /api/mgr/users/{id}/finance-restrictions` | Per-agent financier scoping | `user_finance_restrictions` |
| `GET /api/mgr/users/{id}/kyc`, `DELETE …/kyc/{docType}`, `…/kyc-uidai` | KYC review docs | `app_users`, `user_kyc` + files |
| `PATCH /api/mgr/users/{id}/kyc-status` | **Approve/reject KYC (reject also deactivates)** | `app_users` |
| `GET/PATCH /api/mgr/users/{id}/admin-pass` | Per-agent control-panel pass | `app_users.admin_pass` |
| `GET/PUT /api/mgr/settings/subs-password`, `…/control-password` | Agency-wide passwords | `app_settings` |
| `GET/POST/DELETE /api/mgr/users/{id}/subscriptions`, `/subscriptions/{id}` | Subscription CRUD | `subscriptions` |
| `GET /api/mgr/search`, `/search/list`, `/record/{id}` | Admin vehicle search | `vehicle_records`, `rc_info`, `chassis_info`, `branches`, `finances` |
| `GET /api/mgr/dashboard-stats` | Counts for home tiles | `vehicle_records`, `finances`, `branches` |
| `GET /api/mgr/device-requests`, `POST …/approve`, `DELETE …` | Device-rebind queue | `device_change_requests`, `app_users` |
| `GET /api/mgr/live-users[?since=]` | Agents seen in last ~15 min + GPS | `app_users` |
| `GET /api/mgr/search-logs[...]`, `/search-logs.xlsx` | Agent search audit + export | `search_logs`, `app_users` |
| `GET/POST/DELETE /api/mgr/column-mappings`, `POST /column-types` | Upload header mapping | `column_mappings`, `column_types` |
| `POST /api/mgr/records/upload?mode=` | **Bulk record upload (chunked, gzip, streaming)** | `vehicle_records`, `rc_info`, `chassis_info`, `branches` |
| `GET /api/mgr/export/{users\|subscriptions\|vehicle-records\|rc-records\|chassis-records\|branch-records\|finance-records}[.xlsx]` | Paged / streamed exports | respective tables |
| `POST /api/webhooks/provider/HDB`, `GET /api/webhooks/files[...]`, `/users` … | Direct-Data ingestion | `webhook_*` |

### 4.2 `VKApiServer` — `/api/agency/*` (portals)
Source: [VKApiServer/AgencyPortal.cs](VKApiServer/AgencyPortal.cs)

`otp/send`, `otp/verify`, `register` (multipart + logo) · `manage/login`, `manage/otp/request`, `manage/otp/verify` · `manage/list`, `manage/approve/{id}`, `manage/reject/{id}`, `manage/agency/{id}` (GET/POST) · `desktop/login`, `desktop/profile` (GET/POST), `desktop/tickets` (+messages), `desktop/client-error` · `manage/client-errors`, `manage/tickets` (+messages/status), `manage/apps[/{flavor}/download/{type}]` · `web/login`, `web/verify`, `web/search`, `web/record/{id}`.

### 4.3 `VKmobileapi` — `/api/mobile/*`
Source: [VKmobileapi/Controllers/MobileController.cs](VKmobileapi/Controllers/MobileController.cs) — see full CRUD breakdown in [§6](#6-android-agent-app--screen--crud-breakdown).

---

## 5. WPF desktop app — page by page

> Shell: [LoginWindow](VKdesktopapp/LoginWindow.xaml.cs) → [MainWindow](VKdesktopapp/MainWindow.xaml.cs). Login posts to `/api/agency/desktop/login`; the returned `agt1` Bearer token is attached to **every** request via `App.SetAuthToken` ([App.xaml.cs:270-273](VKdesktopapp/App.xaml.cs#L270-L273)). All `/api/mgr/*` calls additionally carry the static `X-Api-Key` from [DesktopApiClient.Send](VKdesktopapp/Data/DesktopApiClient.cs#L898-L948). Network resilience (retry on transient 5xx/timeouts) is built into that client.

### 5.1 Home / Dashboard — `Home`
- **Code:** [VKdesktopapp/HomePage.xaml.cs](VKdesktopapp/HomePage.xaml.cs)
- **Endpoints:** `GET /api/mgr/dashboard-stats`, `/api/mgr/users/stats`, `/api/mgr/device-requests`, `/api/mgr/live-users?since=` (polled). Embeds a live map (`public/map_live.html`).
- **Tables:** `vehicle_records`, `finances`, `branches`, `app_users`, `device_change_requests`.
- **Data flow:** four calls fired in parallel (`Task.WhenAll` with per-call `Safe()` so one failure doesn't blank the page); live-users markers pushed into a WebView2 map.
- **Security:** `X-Api-Key` + tenant Bearer. Read-mostly. **Multi-effect:** "Approve device request" here = `POST /api/mgr/device-requests/{id}/approve` which **rebinds the agent's `app_users.device_id` and deletes the queued request**.

### 5.2 Find Vehicle (Search) — `Search`
- **Code:** [VKdesktopapp/Records/FindVehiclePage.xaml.cs](VKdesktopapp/Records/FindVehiclePage.xaml.cs)
- **Endpoints:** `GET /api/mgr/search` / `/api/mgr/search/list` (skinny list), `GET /api/mgr/record/{id}` (full detail on open). "Mark Released" calls the **legacy** `POST /api/Records/MarkReleased/{id}` (vkre_db1 — dormant for tenants).
- **Tables:** `vehicle_records` + `rc_info`/`chassis_info` (indexed last4/last5 lookup) joined to `branches`/`finances`.
- **Security:** `X-Api-Key` + tenant Bearer. Read-only for tenant data.

### 5.3 Finances & Branches — `Finances`
- **Code:** [VKdesktopapp/Finances/FinancesManagerPage.xaml.cs](VKdesktopapp/Finances/FinancesManagerPage.xaml.cs) (+ `BranchDialogWindow`, `BranchEditorWindow`, `NewFinanceDialog`, `BulkOperationDialog`).
- **Endpoints:** `finances` CRUD, `branches` CRUD, `POST /branches/{id}/clear`, `DELETE /branches/{id}`, and Excel export via `export/branch-records.xlsx` / `export/finance-records.xlsx` (server-streamed, chunked download with progress).
- **Tables:** `finances`, `branches`, `vehicle_records` (export/clear).
- **Multi-effect (destructive):**
  - **Clear branch** → server-side chunked `DELETE` of all `vehicle_records` for the branch (cascades to `rc_info`/`chassis_info`), then resets `branches.total_records`.
  - **Delete branch** → purges its records **and** drops the branch row (FK `ON DELETE CASCADE`).
  - **Delete finance** → cascades to all its branches → all their records. _A single click can erase millions of rows._

### 5.4 App Users (Agents) — `Users`
- **Code:** [VKdesktopapp/AppUsers/AppUsersManagerPage.xaml.cs](VKdesktopapp/AppUsers/AppUsersManagerPage.xaml.cs) (+ `SubscriptionEditorWindow`).
- **Endpoints:** `GET /api/mgr/users`; `PATCH …/active|admin|stopped|blacklisted`; `POST …/reset-device`; `DELETE …/{id}`; `GET …/{id}/kyc`, `DELETE …/kyc/{docType}`, `…/kyc-uidai`; `PATCH …/kyc-status`; `GET/PATCH …/admin-pass`; subscriptions CRUD; finance-restrictions GET/PUT.
- **Tables:** `app_users`, `user_kyc`, `subscriptions`, `user_finance_restrictions`, `device_change_requests`; **also `crm_master.app_user_registry`** on delete.
- **Security:** `X-Api-Key` + tenant Bearer. This is the most privileged page (it can blacklist, delete, grant admin, set passwords).
- **Multi-effect:**
  - **Reject KYC** (`kyc-status=failed`) → sets `kyc_status` **and deactivates** the agent (`is_active=0`).
  - **Delete user** → removes the `app_users` row (cascades to KYC/subs/search_logs/device requests) **and** clears the master `app_user_registry` row so the number can re-register elsewhere.
  - **Reset device** → clears `device_id` and any pending `device_change_requests`.

### 5.5 Search Logs / Details Views — tile `DetailsViews`
- **Code:** [VKdesktopapp/Records/DetailsViewsPage.xaml.cs](VKdesktopapp/Records/DetailsViewsPage.xaml.cs) (+ `MapPointWindow`, `UserPickerWindow`).
- **Endpoints:** `GET /api/mgr/search-logs[?from&to&userId&q]`, `GET /api/mgr/search-logs.xlsx`, `GET /api/mgr/users/picker`.
- **Tables:** `search_logs` ⋈ `app_users`. Shows who looked up which vehicle, when, and where (GPS + reverse-geocoded address; map view per row).
- **Security:** `X-Api-Key` + tenant Bearer. Read-only audit surface.

### 5.6 Upload Records — tile `UploadRecords`
- **Code:** [VKdesktopapp/Records/RecordsEditorWindow.xaml.cs](VKdesktopapp/Records/RecordsEditorWindow.xaml.cs) (Syncfusion spreadsheet) + [RecordValidatorAndUploaderWindow.xaml.cs](VKdesktopapp/Records/RecordValidatorAndUploaderWindow.xaml.cs) + [AddMappingWindow](VKdesktopapp/Records/AddMappingWindow.xaml.cs).
- **Endpoint:** `POST /api/mgr/records/upload?mode=begin|append|finish|replace` — the **most complex flow in the app**. The client gzip-compresses pipe-delimited rows and uploads in **25 000-row chunks** with per-chunk retry; the server streams back ndjson progress ([DesktopApiClient.cs:484-642](VKdesktopapp/Data/DesktopApiClient.cs#L484-L642)).
- **Tables:** `vehicle_records` (insert), `rc_info`/`chassis_info` (index rebuild), `branches` (stats), `column_mappings`/`column_types` (header resolution).
- **Multi-effect:** `mode=begin` clears the branch's existing records; `finish` finalises stats + rebuilds search indexes. Replace-semantics span multiple HTTP requests. After upload the mobile search cache should be invalidated (`POST /api/mobile/cache/invalidate`).

### 5.7 Reports & Exports — `Reports`
- **Code:** [VKdesktopapp/Reports/ReportsPage.xaml.cs](VKdesktopapp/Reports/ReportsPage.xaml.cs) + [Exports/ChunkedExportDialog.xaml.cs](VKdesktopapp/Exports/ChunkedExportDialog.xaml.cs), [Exports/VehicleExcelWriter.cs](VKdesktopapp/Exports/VehicleExcelWriter.cs).
- **Endpoints:** `export/users`, `export/subscriptions`, `export/{vehicle|rc|chassis}-records[.xlsx]` (paged JSON for PDF, server-streamed `.xlsx` for Excel).
- **Tables:** `app_users`, `subscriptions`, `vehicle_records`. Read-only.

### 5.8 Blacklist — tile `Blacklist`
- **Code:** [VKdesktopapp/Blacklist/BlacklistPage.xaml.cs](VKdesktopapp/Blacklist/BlacklistPage.xaml.cs)
- **Endpoints:** `GET /api/mgr/users/all-simple`, `PATCH /api/mgr/users/{id}/blacklisted`.
- **Table:** `app_users.is_blacklisted`. **Multi-effect:** a blacklisted agent is blocked from every mobile search/record call server-side (checked on each request).

### 5.9 Server Settings — gear icon
- **Code:** [VKdesktopapp/ServerSettingsWindow.xaml.cs](VKdesktopapp/ServerSettingsWindow.xaml.cs)
- **Endpoints:** `GET/POST /api/agency/desktop/profile` (agency contact details — **Bearer-only**, no `X-Api-Key`), `GET/PUT /api/mgr/settings/control-password`, `…/subs-password`.
- **Tables:** `crm_master.agencies` (profile), tenant `app_settings` (passwords).
- **Multi-effect:** editing the agency profile here **also changes what the mobile app shows** in its in-app "Agency" panel (both read the same `crm_master.agencies` row).

### 5.10 Support — headset icon
- **Code:** [VKdesktopapp/SupportWindow.xaml.cs](VKdesktopapp/SupportWindow.xaml.cs) + [TicketThreadWindow.xaml.cs](VKdesktopapp/TicketThreadWindow.xaml.cs).
- **Endpoints:** `GET/POST /api/agency/desktop/tickets`, `POST …/{id}/messages` (Bearer-only). Unread-badge polls every 60 s ([MainWindow.xaml.cs:145-156](VKdesktopapp/MainWindow.xaml.cs#L145-L156)).
- **Tables:** `crm_master.support_tickets`, `support_ticket_messages`.

### 5.11 Direct Data — `DirectData`
- **Code:** [VKdesktopapp/DirectData/DirectDataPage.xaml.cs](VKdesktopapp/DirectData/DirectDataPage.xaml.cs) (+ `AddCredentialDialog`).
- **Endpoints:** `GET /api/webhooks/files`, `GET /api/webhooks/files/{id}/download`, `GET/POST/DELETE /api/webhooks/users`.
- **Tables:** `webhook_banks`, `webhook_files`, `webhook_users`. Provider-pushed files (e.g. HDB) land via `POST /api/webhooks/provider/HDB`.

### 5.12 Legacy / showcase pages (dormant for tenants)
`Confirmations` ([ConfirmationsManagerPage](VKdesktopapp/Confirmations/ConfirmationsManagerPage.xaml.cs)), `Feedbacks` ([FeedbacksManagerPage](VKdesktopapp/Feedbacks/FeedbacksManagerPage.xaml.cs)), `AcceptPayments` ([AcceptPaymentsPage](VKdesktopapp/AcceptPaymentsPage.xaml.cs)), `OtpManager` ([Utilities/OtpManagerPage](VKdesktopapp/Utilities/OtpManagerPage.xaml.cs)), `ModuleStatus` ([ModuleStatusPage](VKdesktopapp/ModuleStatusPage.xaml.cs)), legacy `UploadRecordsPage`. These call `/api/Confirmations`, `/api/Feedbacks`, `/api/Payments`, `/api/OTPs`, `/api/Modules/*`, `/api/Uploads` → **legacy `vkre_db1` schema** (tables `repoconformations/details/otp/billings/users`) that doesn't exist in tenant DBs, so they return empty for agencies. Treat as vestigial.

---

## 6. Android agent app — screen & CRUD breakdown

> Client wiring: [ApiService.kt](android/app/src/main/java/com/vkenterprises/vras/data/api/ApiService.kt) (Retrofit interface) + [ApiClient.kt](android/app/src/main/java/com/vkenterprises/vras/data/api/ApiClient.kt) (OkHttp). An interceptor injects `X-Tenant-Token` on every call; per-user calls also send `X-User-Id`. Session + tenant token persist in DataStore ([PreferencesManager.kt](android/app/src/main/java/com/vkenterprises/vras/utils/PreferencesManager.kt)); offline records cache in a per-tenant Room DB `vk_cache_<slug>.db` ([data/local/](android/app/src/main/java/com/vkenterprises/vras/data/local/)). Screens are Compose; navigation in [AppNavigation.kt](android/app/src/main/java/com/vkenterprises/vras/navigation/AppNavigation.kt).

### 6.1 Onboarding & auth (no tenant token yet)

| Screen (dir) | CRUD action | Endpoint(s) | Table(s) | Notes / multi-effect |
|---|---|---|---|---|
| **Splash** ([SplashScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/SplashScreen.kt)) | Read session, route | `GET /me/status` | `app_users` | Decides which gate screen to show |
| **Agency picker** ([AgencyPicker.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/AgencyPicker.kt)) | Read agencies | `GET /agencies` | `crm_master.agencies` | White-label build pins slug |
| **Register** ([RegisterScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/RegisterScreen.kt)) | **Create** account (+KYC) | `check-mobile` → `otp/send` → `otp/verify` → `kyc/aadhaar/otp`+`verify` → `POST /register` | **many** (see below) | **Highest multi-effect action in the app** |
| **Login** ([LoginScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/LoginScreen.kt)) | Authenticate | `otp/send`+`verify` (UI), `POST /login` | `app_users`, `crm_master` | Returns + persists `X-Tenant-Token`; device-binding enforced |
| **KYC resubmit** ([KycResubmitScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/KycResubmitScreen.kt)) | Update KYC | `POST /kyc/resubmit` | `app_users`, `user_kyc` + files | Tenant-bound by slug+mobile; resets `kyc_status=pending` |
| Gate screens: WaitingApproval / AppStopped / Blacklisted / Inactive / SubscriptionExpired / KycPending / KycRejected | Re-check status | `POST /login` or `GET /me/status` | `app_users` | "Check again" re-polls without fresh OTP |

**Register multi-effect** ([MobileController.cs:85-191](VKmobileapi/Controllers/MobileController.cs#L85-L191), [MobileRepository.cs:195-319](VKmobileapi/Data/MobileRepository.cs#L195-L319)) — one call does **all** of:
1. Validate agency is approved; verify SMS-OTP freshness.
2. Cross-agency uniqueness check against `crm_master.app_user_registry` (auto-heals orphaned rows).
3. `INSERT` into tenant `app_users` (inactive, pending).
4. Save PFP + up to 5 KYC images to disk under `/opt/vkmobileapi/uploads/kyc/<id>/`.
5. `INSERT` into `user_kyc` (image paths).
6. `UPDATE app_users` with verified Aadhaar demographics + capture GPS, force `kyc_status='pending'`.
7. `INSERT` into `crm_master.app_user_registry` (atomic UNIQUE(mobile) gate).
8. Consume the OTP verification.

### 6.2 Core agent screens (tenant token + user id)

| Screen (dir) | CRUD action | Endpoint(s) | Table(s) | Server-side guards |
|---|---|---|---|---|
| **Home / Search** ([HomeScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/HomeScreen.kt), [SearchViewModel](android/app/src/main/java/com/vkenterprises/vras/viewmodel/SearchViewModel.kt), [SearchRepository](android/app/src/main/java/com/vkenterprises/vras/data/repository/SearchRepository.kt)) | **Read** vehicles (online + offline) | `GET /search/rc/{last4}?lite`, `/search/chassis/{last5}?lite` | `rc_info`/`chassis_info`→`vehicle_records` | blacklisted/inactive/stopped → 403; **no active subscription → 402** |
| **Vehicle detail** ([VehicleDetailScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/VehicleDetailScreen.kt)) | Read one + **log view** | `GET /record/{id}`, `GET /agency`, `POST /search-log` | `vehicle_records`, `crm_master.agencies`, `search_logs` | Opening a record **writes a `search_logs` row with GPS** (reverse-geocoded server-side via Nominatim) |
| **Profile** ([ProfileScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/ProfileScreen.kt), [ProfileViewModel](android/app/src/main/java/com/vkenterprises/vras/viewmodel/ProfileViewModel.kt)) | Read profile, **Update** PFP | `GET /profile/{id}`, `PUT /profile/{id}/pfp` | `app_users`, `user_kyc` | Self only |
| **Settings** ([SettingsScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/SettingsScreen.kt), [SettingsViewModel](android/app/src/main/java/com/vkenterprises/vras/viewmodel/SettingsViewModel.kt)) | Force full sync, logout | `GET /sync/branches`, `/sync/records/{branchId}` | `branches`, `vehicle_records` | Downloads tenant records into Room |
| **Confirm** ([ConfirmScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/ConfirmScreen.kt)) | Repossession confirmation | (local / search-log) | `search_logs` | — |

**Search resilience:** [SearchRepository](android/app/src/main/java/com/vkenterprises/vras/data/repository/SearchRepository.kt) serves from the **offline Room cache** when online fails, so agents in low-signal areas still search. Background sync keeps the cache fresh.

### 6.3 Admin-only screens (server re-checks `is_admin`)

| Screen (dir) | CRUD action | Endpoint(s) | Table(s) | Multi-effect |
|---|---|---|---|---|
| **Control Panel** ([ControlPanelScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/ControlPanelScreen.kt), [ControlPanelViewModel](android/app/src/main/java/com/vkenterprises/vras/viewmodel/ControlPanelViewModel.kt)) | Gate by pass; toggle agents | `POST /admin/verify-admin-pass`; `GET /admin/users`; `PATCH /admin/users/{id}/active\|stopped\|blacklisted\|admin\|kyc-status` | `app_users` | **kyc-status=failed also deactivates**; blacklist/stop instantly cut off the target agent's searches |
| **Live Users** ([LiveUsersScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/LiveUsersScreen.kt)) | Read live agents | `GET /live-users` | `app_users` (last_seen/lat/lng) | Admin sees field positions |
| **Manage Subscriptions** ([ManageSubscriptionsScreen.kt](android/app/src/main/java/com/vkenterprises/vras/ui/screens/ManageSubscriptionsScreen.kt), [ManageSubscriptionsViewModel](android/app/src/main/java/com/vkenterprises/vras/viewmodel/ManageSubscriptionsViewModel.kt)) | Read/Create/Delete subs | `POST /admin/verify-subs-pass`; `GET /profile/{id}/subscriptions`; `POST /admin/users/{id}/subscriptions`; `DELETE /admin/subscriptions/{id}` | `subscriptions` | Granting a sub immediately re-enables the agent's search (subscription cache invalidated) |

### 6.4 KYC verification (Sandbox-backed, server-side)
[MobileController.cs:758-977](VKmobileapi/Controllers/MobileController.cs#L758-L977) — Aadhaar OKYC OTP/verify, PAN verify, bank verify. The app sends Aadhaar/PAN/IFSC; the **server** calls Sandbox (`api.sandbox.co.in`) with credentials held only in the systemd env, then stores verified results on `app_users`. The app never sees Sandbox keys.

### 6.5 Background workers
- **SyncWorker** ([workers/SyncWorker.kt](android/app/src/main/java/com/vkenterprises/vras/workers/SyncWorker.kt)) — periodically pulls `sync/branches` + `sync/records/{branchId}` (paged, bounded 5-way parallelism, resumable — [SyncRepository.kt](android/app/src/main/java/com/vkenterprises/vras/data/repository/SyncRepository.kt)) into Room. Uses `uploadedAt` as the authoritative change signal.
- **LocationWorker** ([workers/LocationWorker.kt](android/app/src/main/java/com/vkenterprises/vras/workers/LocationWorker.kt)) — heartbeats `POST /heartbeat` with GPS, updating `app_users.last_seen/last_lat/last_lng`; the response carries `isStopped`/`isBlacklisted` so a remote stop/blacklist takes effect within one heartbeat. (Heartbeat has tight 8 s timeouts so it never starves a user's search — see the per-host slot note in [ApiClient.kt:99-112](android/app/src/main/java/com/vkenterprises/vras/data/api/ApiClient.kt#L99-L112).)

---

## 7. Catalog of multi-effect (fan-out) actions

| Action | Surface | Side-effects |
|---|---|---|
| **Approve agency** | manage portal | Creates MySQL DB `crmr_<slug>` + user `tu_<slug>` (privileges scoped to that DB) + applies schema template + marks `approved` + emails the agency ([AgencyPortal.cs:501-559](VKApiServer/AgencyPortal.cs#L501-L559)) |
| **Mobile register** | Android | 3 tables across 2 DBs + ≤6 files on disk + OTP consume (see [§6.1](#61-onboarding--auth-no-tenant-token-yet)) |
| **Reject KYC** | desktop & mobile admin | `kyc_status='failed'` **and** `is_active=0` |
| **Delete agent** | desktop | `app_users` (cascades subs/KYC/search_logs/device req) **and** `crm_master.app_user_registry` cleanup |
| **Reset device** | desktop | clears `device_id` + deletes pending `device_change_requests` |
| **Approve device request** | desktop/home | rebinds `app_users.device_id` + deletes the request row |
| **Clear / delete branch / delete finance** | desktop | cascading delete of up to millions of `vehicle_records` (+`rc_info`/`chassis_info`) + branch stat reset |
| **Records upload (`begin/finish`)** | desktop | clears old records → inserts → rebuilds rc/chassis indexes → updates branch stats → (should) invalidate mobile cache |
| **Edit agency profile** | desktop settings | updates `crm_master.agencies` → instantly changes the **mobile app's** Agency panel too |
| **Blacklist / Stop agent** | desktop & mobile admin | next mobile search/record/heartbeat for that agent is refused server-side |
| **Grant subscription** | mobile admin | re-enables search immediately (sub cache cleared) |

---

## 8. Live database inspection (read-only, this session)

Connected via SSH per [RUNBOOK.md](RUNBOOK.md) and ran read-only checks:

- **Databases present:** `crm_master`, `crmr_v_k_enterprises`, `crmr_rk_enterprises`.
- **Agencies:** `V K ENTERPRISES` (`v_k_enterprises`, approved) and `RK ENTERPRISES` (`rk_enterprises`, approved). `app_user_registry` = 6 rows.
- **`crmr_v_k_enterprises` row counts:** `app_users`=6, `branches`=1,724, `finances`=135, **`vehicle_records`=3,900,503**, `subscriptions`=6, `search_logs`=355, `user_kyc`=6.
- **Tenant tables present:** match the template exactly (incl. `_branch_ids`/`_num10` seeding helpers + `v_finance_summary` view).
- **Tenant isolation (verified):** `tu_v_k_enterprises@localhost` holds `GRANT ALL PRIVILEGES ON crmr_v_k_enterprises.*` + only `USAGE ON *.*` — i.e. **it cannot read any other tenant's database**. Same for `tu_rk_enterprises`. Good.
- **Port exposure (verified):** `3306`, `5001`, `5002` all bound to `127.0.0.1` only — not internet-reachable; only the OLS proxy fronts them. Good.
- **Secret hygiene (verified):** `/home/vkapp/db/.env.local` sets `MYSQL_*`, `MASTER_DB_*`, `DESKTOP_LOGIN_PASSWORD`, `PRIVATEKEY`, `SMTP_*`. `SANDBOX_*` + `MSG91_*` are systemd drop-ins. `TENANT_DB_SECRET` was originally unset (defaulted in code) — **rotated 2026-06-09** into a new chmod-600 drop-in (`tenant.conf`) on both services, with the `tu_<slug>` DB passwords re-aliased to match. (Note: `.env.local` is `chmod 644` / world-readable — a separate hardening item; the new secret was deliberately put in a 600 drop-in instead.)

---

## 9. Security assessment

### What's done well ✅
- **Per-request tenant isolation** with offline-verifiable HMAC tokens; invalid tokens fail closed (no silent fallback to another DB).
- **DB-level least privilege**: each tenant MySQL user is scoped to its own database only (verified live).
- **Network surface minimal**: MariaDB + both APIs bound to localhost; TLS terminated at the proxy; secrets (Sandbox/DB) kept out of the repo and the client apps.
- **Password storage**: agency login passwords are stored as **salted PBKDF2-SHA256 one-way hashes** in `crm_master.agencies.password_hash` (format `pbkdf2$<iter>$<salt>$<hash>`) — never plaintext, never reversible. The current code uses 100k iterations ([AgencyPortal.cs:1430-1454](VKApiServer/AgencyPortal.cs#L1430-L1454)); the two existing live rows were created at 10k iterations (still salted PBKDF2 — they'd upgrade to 100k on the next password change).
- **Parameterised SQL** everywhere in the live paths (no SQL-injection vectors found in `/api/mgr` or `/api/mobile`).
- **Defence in depth on mobile**: every search/record call re-checks active/stopped/blacklisted/subscription server-side; remote stop/blacklist propagates within one heartbeat.
- **2FA** (email OTP) on the agency **web** portal; OTPs are cryptographically random and single-use with expiry.
- **Cross-agency user uniqueness** enforced by a DB UNIQUE constraint, with orphan auto-healing.

### Findings (highest first) 🔴🟠🟡

| # | Sev | Finding | Impact | Fix |
|---|---|---|---|---|
| 1 | ✅ **FIXED 2026-06-09** (was 🔴 Critical) | **`TENANT_DB_SECRET` was the in-code default in production.** This one secret signs **all** desktop `agt1` Bearer tokens **and** mobile `mt1` tokens **and** derives **every** tenant DB password. | (Pre-fix) anyone with repo/source access could forge a session token for any agency and compute every tenant DB password. | **Done:** a strong random `TENANT_DB_SECRET` was placed in chmod-600 systemd drop-ins (`/etc/systemd/system/{vkapi,vkmobileapi}.service.d/tenant.conf`), the `tu_<slug>` MySQL passwords were re-aliased to the new derivation (`ALTER USER`, both `@localhost`/`@127.0.0.1`), and both services restarted. Verified: new-secret token → 200, old default-secret token → 401. One-time consequence: all desktop/mobile users re-login once. |
| 2 | 🟠 High | **Manage-portal password hardcoded** `crmrs@kc.12` in [AgencyPortal.cs:28](VKApiServer/AgencyPortal.cs#L28) (committed). The super-admin gate that can approve/reject agencies and read every agency's tickets/errors. | Source disclosure = manage-portal access (OTP step exists but the password is step 1 and is in the repo). | Move to env; rotate; rely on the email-OTP as a true second factor. |
| 3 | 🟠 High | **`X-Api-Key` is a single global static secret** (`12`) shared by every desktop install; `MgrAuth` is a plain string compare with no per-agency scoping or rotation. | Combined with a forged/stolen Bearer token it unlocks the full `/api/mgr/*` admin surface for a tenant. Low entropy ("12"). | Make it a long random per-deployment key at minimum; ideally drop it and rely solely on the Bearer token (which already selects the tenant). |
| 4 | 🟡 Medium | **Insecure default fallbacks throughout** (`MASTER_DB_PASSWORD="SET_VIA_ENV"`, `PROVISIONER_DB_PASSWORD="SET_VIA_ENV"`, `desktopLoginPassword="vk@kunal.admin"`, `MASTER_DB_USER` etc.). If an env var is ever unset, the app boots with a guessable credential instead of failing. | Latent foot-gun; one missing env var = open door. | Fail-fast on startup if any required secret is unset (no silent defaults). |
| 5 | 🟡 Medium | **CORS `AllowAnyOrigin`** on both APIs ([Program.cs:15-19](VKApiServer/Program.cs#L15-L19), [VKmobileapi/Program.cs:26-27](VKmobileapi/Program.cs#L26-L27)). | Any website can call the APIs from a browser (token-based auth limits damage, but it widens CSRF/abuse surface for the cookie-less flows). | Restrict to the known portal origins. |
| 6 | 🟡 Medium | **Mobile identity via `X-User-Id` header**, not bound into the tenant token. The token proves the agency; the user id is a separate, client-supplied header re-checked only for `is_admin`. | A valid token-holder could pass another user's id to admin reads (`profile/{id}/subscriptions` etc.). Blast radius is within the same agency, and admin actions still re-check `is_admin`, but it's weaker than binding the user into the token. | Bind userId into the signed token, or verify the header matches the token subject. |
| 7 | 🟡 Low/Med | **PII at rest is largely unencrypted**: KYC images on disk under `/opt/vkmobileapi/uploads`, Aadhaar last-4/name/DOB/address, bank details, and **continuous GPS** in `search_logs`/`app_users`. | Disk/backup compromise exposes sensitive PII; this is regulated data (Aadhaar/PAN). | Encrypt at rest, tighten file perms, define retention/erasure for location + KYC. |
| 8 | 🟢 Low | **Manage session = bearer-in-URL** for app downloads (`?token=`) ([AgencyPortal.cs:1090-1104](VKApiServer/AgencyPortal.cs#L1090-L1104)). | Tokens can leak via logs/referrers. | Prefer header-only; short-TTL download tokens. |
| 9 | 🟢 Low | **SSH password (with `$`) lives in [RUNBOOK.md](RUNBOOK.md)** (git-ignored) and was used this session. | If the file ever leaks, full server access. | Move to key-based SSH; rotate the password. |

### Overall posture
**Architecturally solid, operationally under-hardened.** The hard parts (tenant isolation, least-privilege DB users, localhost-only services, PBKDF2, server-side authorization re-checks, parameterised SQL) are done right. The exposure is concentrated in **secret management**: the master signing secret and two admin passwords are at their in-repo defaults/hardcoded values. **Fixing finding #1 (rotate `TENANT_DB_SECRET` off the default) is the single highest-leverage action** — until then, the cryptographic backbone of multi-tenancy is effectively public. Findings #2–#4 are quick env-config wins. With those addressed, this would be a genuinely well-secured multi-tenant SaaS.

---

## 10. Quick reference — "I need to change X, where is it?"

| Want to change… | File |
|---|---|
| A desktop API call | [VKdesktopapp/Data/DesktopApiClient.cs](VKdesktopapp/Data/DesktopApiClient.cs) |
| Desktop nav / pages | [VKdesktopapp/MainWindow.xaml.cs](VKdesktopapp/MainWindow.xaml.cs) |
| A `/api/mgr/*` behaviour | [VKApiServer/Program.cs](VKApiServer/Program.cs) |
| Agency/manage portal logic | [VKApiServer/AgencyPortal.cs](VKApiServer/AgencyPortal.cs) |
| Tenant routing / token signing (desktop) | [VKApiServer/TenantContext.cs](VKApiServer/TenantContext.cs) |
| A mobile endpoint | [VKmobileapi/Controllers/MobileController.cs](VKmobileapi/Controllers/MobileController.cs) |
| Mobile SQL / CRUD | [VKmobileapi/Data/MobileRepository.cs](VKmobileapi/Data/MobileRepository.cs) |
| Mobile tenant routing / token | [VKmobileapi/TenantContext.cs](VKmobileapi/TenantContext.cs) |
| Android API surface | [android/.../data/api/ApiService.kt](android/app/src/main/java/com/vkenterprises/vras/data/api/ApiService.kt) |
| Android screens | [android/.../ui/screens/](android/app/src/main/java/com/vkenterprises/vras/ui/screens/) |
| Schema (tenant / master) | [dbschema/tenant_template.sql](dbschema/tenant_template.sql) / [dbschema/crm_master.sql](dbschema/crm_master.sql) |
| Deploy | [deploy.sh](deploy.sh) + [RUNBOOK.md](RUNBOOK.md) |
