# VK Enterprises — Complete Software Technical Overview

> Prepared from the actual source code (not summary docs).
> Repository: `vkrepoenterprises` · Branch: `main`

---

## 1. What the Software Is

VK Enterprises is a **vehicle repossession field-operations platform** for a finance/recovery
business. It has three parts that work together:

1. **Windows Desktop App** — used by office administrators to upload vehicle data, manage
   finance companies, manage mobile users, run reports, and watch field agents on a live map.
2. **Android Mobile App** — used by field agents to look up a vehicle by its RC/chassis
   number (works offline), send repossession confirmations, and report their GPS location.
3. **Two Cloud APIs + MySQL Database** — hosted on a single Linux server; hold all business
   logic and data.

The desktop app is admin-only. The mobile app is for agents (and admin agents). All data
lives in one MySQL database on the server.

---

## 2. Languages & Technology Used

| Part of the system | Language | Framework / Key Libraries | Runtime / Tooling |
|---|---|---|---|
| Desktop application | **C#** | WPF (Windows Presentation Foundation) | .NET 6 (`net6.0-windows`) |
| Desktop API server | **C#** | ASP.NET Core Minimal APIs | .NET 6 (`net6.0`) |
| Mobile API server | **C#** | ASP.NET Core MVC Controllers | .NET 6 (`net6.0`) |
| Mobile application | **Kotlin** | Jetpack Compose, Hilt, Room, Retrofit, WorkManager | Android SDK 34 |
| Database | **SQL** | MySQL | MySQL 8 / MariaDB-compatible |
| Desktop embedded map | **JavaScript / HTML** | Leaflet.js + OpenStreetMap tiles | Microsoft WebView2 |
| Build / deploy | **Bash** | `deploy.sh`, Inno Setup, systemd | Ubuntu + CyberPanel / OpenLiteSpeed |

### Desktop app NuGet packages (actual versions)
- `Microsoft.Web.WebView2` 1.0.2903.40 — embedded Chromium for the live map
- `Syncfusion.SfSpreadsheet.WPF`, `Syncfusion.Pdf.Net.Core`, `Syncfusion.PropertyGrid.WPF`,
  `Syncfusion.Licensing` 28.1.33 — Excel grid, Excel/PDF export
- `FontAwesome5` 2.1.11 — icons
- `System.Net.Http.Json` 6.0.1 — JSON HTTP calls

### Mobile API packages
- `MySqlConnector` 2.5.0 — MySQL driver
- `Microsoft.AspNetCore.Authentication.JwtBearer` 6.0.36, `System.IdentityModel.Tokens.Jwt` 6.36.0

### Desktop API packages
- `MySqlConnector` 2.3.7 — MySQL driver

### Android dependencies (actual versions)
- Compose BOM 2024.06.00, Material 3, `material-icons-extended`
- Hilt (Dagger) 2.51.1 — dependency injection
- Room 2.6.1 — on-device SQLite cache
- Retrofit 2.11.0 + Gson converter + OkHttp logging interceptor — networking
- Navigation Compose 2.7.7
- WorkManager 2.9.1 + Hilt-Work 1.2.0 — background GPS jobs
- DataStore Preferences 1.1.1 — session storage
- Coil 2.6.0 — image loading (PFP / KYC)
- Play Services Location 21.3.0 — GPS
- `minSdk 26` (Android 8.0), `targetSdk 34` / `compileSdk 34`

---

## 3. System Architecture

```
                         PRODUCTION SERVER (Ubuntu, CyberPanel / OpenLiteSpeed)
                         domain: api.characterverse.tech
   ┌────────────────────────────────────────────────────────────────────────┐
   │                                                                        │
   │   VKApiServer  (ASP.NET Core 6, port 5002)  ── all inbound traffic     │
   │     • /api/mgr/*      desktop management endpoints                      │
   │     • /api/*          dashboard endpoints                               │
   │     • /api/mobile/**  reverse-proxied to VKmobileapi                    │
   │                          │                                             │
   │   VKmobileapi  (ASP.NET Core 6, port 5001)  ── mobile endpoints         │
   │                          │                                             │
   │                     MySQL  database: vkre_db1                           │
   └────────────────────────────────────────────────────────────────────────┘
              ▲                                          ▲
        X-Api-Key header                          X-User-Id header
              │                                          │
     ┌─────────────────┐                       ┌────────────────────┐
     │ WPF Desktop App │                       │  Android Mobile App│
     │  (C# / .NET 6)  │                       │   (Kotlin)         │
     └─────────────────┘                       └────────────────────┘
```

- The **desktop app** talks only to `/api/mgr/*` and `/api/*` on VKApiServer.
- The **mobile app** calls `/api/mobile/*`; VKApiServer transparently forwards those to
  VKmobileapi on port 5001.
- Both API servers share the single MySQL database.
- The server also serves static assets: the installer download page (`/downloads/`) and
  the live-map HTML (`/public/map_live.html`).

---

## 4. Database (MySQL — `vkre_db1`)

### Core relationships
```
finances ──< branches ──< vehicle_records ──< rc_info        (RC fast-lookup index)
                                          └──< chassis_info   (chassis fast-lookup index)

app_users ──< subscriptions
          ──< user_kyc
          ──< device_change_requests
          ──< search_logs
          ──< user_finance_restrictions
```

### Main tables
- **`finances`** — finance/head-office companies (id, name, …).
- **`branches`** — branches under each finance (id, finance_id, name, address, contact1-3,
  uploaded_at, total_records).
- **`vehicle_records`** — the core data: one row per vehicle. Holds `vehicle_no`,
  `chassis_no`, `engine_no`, `model`, `agreement_no`, customer fields, `bucket`, `gv`, `od`,
  `seasoning`, `tbr_flag`, `sec9/17`, `region`, `area`, `level1-4` + contacts, sender mails,
  `executive_name`, `pos`, `toss`, `remark`, `is_released`, `created_at`.
- **`rc_info`** — search index: `vehicle_record_id`, `rc_number`, `model`, **`last4`**
  (indexed — the right-most 4-digit cluster of the RC number).
- **`chassis_info`** — search index: `vehicle_record_id`, `chassis_number`, `model`,
  **`last5`** (indexed — last 5 chars of the chassis).
- **`app_users`** — mobile users: `mobile` (login id), `name`, `address`, `pincode`, `pfp`,
  `device_id`, `is_active`, `is_admin`, `is_stopped`, `is_blacklisted`, `balance`,
  `account_number`, `ifsc_code`, `admin_pass` (per-admin Control Panel password),
  `last_seen`, `last_lat`, `last_lng`.
- **`subscriptions`** — per-user plans: `user_id`, `start_date`, `end_date`, `amount`, `notes`.
- **`user_kyc`** — Aadhaar front/back + PAN front (stored as uploaded files / URLs).
- **`device_change_requests`** — queued requests when a user logs in from a new phone.
- **`search_logs`** — every vehicle lookup by an agent, with GPS + device + server timestamps.
- **`user_finance_restrictions`** — finances a specific user is blocked from seeing.
- **`column_types` / `column_mappings`** — config for normalising Excel import column names.
- **`repoconformations`** — repossession confirmation records.
- **`app_settings`** — key/value settings (e.g. `subs_password`).

### Why two index tables (`rc_info`, `chassis_info`)
They give O(1) suffix lookup. A field agent types the last 4 digits of an RC; the server
hits the indexed `rc_info.last4` column instead of scanning millions of `vehicle_records`
rows with a `LIKE` query.

---

## 5. Desktop Application (WPF / C#)

The desktop app is a single-window shell (`MainWindow`) with a left icon sidebar and a
slide-out tile menu. Each menu item swaps a `Page` into the content frame.

### 5.1 Pages (modules)

| Module / Page | Purpose |
|---|---|
| **LoginWindow** | Password login — validated against the server's desktop password. |
| **HomePage** | Dashboard. Stat cards (records / finances / branches / users). Live agent **map** (WebView2 + Leaflet). Device-change request approve/deny cards. Time filter — defaults to "1 hour ago" in IST. Auto-refreshes every 30 s. |
| **FindVehiclePage** (Search) | Search a vehicle by last 4 digits of RC or last 5 of chassis. Shows matching vehicles, their branches/finances, full detail panel, copy, confirm, release toggle, delete. Results sorted alphabetically. |
| **FinancesManagerPage** | Two-pane CRUD: Head Offices (left) and their Finances/branches (right). Add / edit / delete / download records. Per-row ⋮ menu and right-click menu. Refresh buttons. Branch pane is blank until a head office is selected. |
| **UploadRecordsPage** + **RecordsEditorWindow** + **RecordValidatorAndUploaderWindow** | Excel-based bulk upload. The editor loads an Excel file, maps columns, validates every RC number, de-duplicates, then streams the records to the server with a live progress bar. |
| **AppUsersManagerPage** | Manage mobile users: activate/deactivate, admin toggle, stop app, subscriptions, KYC document view/download/delete, finance restrictions, per-admin Control Panel password, profile picture, device reset. |
| **ConfirmationsManagerPage** + **ManageConfirmationWindow** | View and manage vehicle repossession confirmation records. |
| **DetailsViewsPage** | Audit view of vehicle-lookup activity. |
| **ReportsPage** | Export data to Excel or PDF: Users, Subscriptions, Vehicle Records, RC Records, Chassis Records. Paginated fetch with a progress bar. |
| **BlacklistPage** | Restricted-users view. |
| **ServerSettingsWindow** | Configure the API server address and firm details. |

### 5.2 Supporting windows
`BranchEditorWindow`, `NewFinanceDialog`, `BranchDialogWindow`, `BulkOperationDialog`,
`SubscriptionEditorWindow`, `MapPointWindow`.

### 5.3 Desktop data layer
`Data/DesktopApiClient.cs` is the single HTTP gateway to the server. Repository wrappers:
`FinanceRepository`, `BranchRepository`, `AppUserRepository`, `VehicleSearchRepository`,
`RecordsRepository`. `EnvLoader.cs` loads config at startup.

### 5.4 Live map
`HomePage` embeds **WebView2** loading `public/map_live.html` (served from the API server so
fixes ship without an installer update). The HTML uses **Leaflet.js** with OpenStreetMap
tiles; each live agent is a red pin with name, mobile, coordinates, last-seen time.

---

## 6. Android Mobile Application (Kotlin)

### 6.1 Architecture (MVVM + offline-first)
```
 Compose Screens  ──>  ViewModels (StateFlow)  ──>  Repositories
                                                     │        │
                                          Room (SQLite cache)  Retrofit (server API)
```
- **Hilt** injects repositories, ViewModels and Workers.
- **DataStore** persists the session (userId, name, mobile, isAdmin, subscription end, pfp).
- **Room** holds an on-device copy of all vehicle records so search works with no network.

### 6.2 Screens (modules)

| Screen | Purpose |
|---|---|
| **SplashScreen** | Reads stored session → routes to Login or Home. |
| **RegisterScreen** | New-user signup: name, mobile, address, pincode, profile photo, Aadhaar front/back, PAN, **account number + IFSC**. Account starts inactive. |
| **LoginScreen** | Mobile number + device ID login. |
| **WaitingApprovalScreen** | Shown when account is pending admin approval. |
| **InactiveScreen / AppStoppedScreen / BlacklistedScreen / SubscriptionExpiredScreen** | Gate screens shown when the account is disabled, stopped, blacklisted, or the subscription has expired. |
| **HomeScreen** | Main search. RC/Chassis mode toggle, digit input. Searches the local Room cache first, server as fallback. Online/Offline cloud toggle (with a confirmation dialog). Sync button. Quick-access tiles — "Offline Records" shows the live local record count; "Control Panel" for admins. |
| **VehicleDetailScreen** | All fields of the selected vehicle, "Found in Finances" sheet, WhatsApp/copy actions. Has a **search bar pinned at the top** (numeric keyboard auto-opens) so the agent can search again without going back. |
| **ConfirmScreen** | Build and send a repossession confirmation message via WhatsApp / SMS. Message preview includes BKT and OD. |
| **ProfileScreen** | The agent's own profile, subscription, KYC. |
| **SettingsScreen** | Sync stats, force full sync, online/offline records counts. |
| **LiveUsersScreen** | Admin only — map of all active agents. |
| **ControlPanelScreen** | Admin only, password-gated. Manage any user: status toggles (active / stopped / blacklisted), subscriptions (calendar date pickers), and view full user details — Personal Info, **Bank Details** (account/IFSC), **KYC documents**. |
| **ManageSubscriptionsScreen** | Subscription management entry. |

### 6.3 ViewModels
`AuthViewModel` (session/login), `SearchViewModel` (search + sync + offline count),
`SettingsViewModel` (sync stats), `ControlPanelViewModel` (admin user management).

### 6.4 Local Room database
- **`VehicleCache`** — offline copy of vehicle records; indexed on `last4`, `last5`, `branchId`.
- **`BranchSyncState`** — per-branch `uploadedAt` + sync page offset, for resumable sync.

### 6.5 Background work (WorkManager)
- **GPS heartbeat worker** — periodically gets a GPS fix and posts it to the server so the
  admin map stays live; runs as a foreground service so it survives the app being closed.
- **Sync worker** — incrementally downloads vehicle records into Room, resumable per branch.

### 6.6 Offline-first search
The agent types 4 digits → Room indexed query returns instantly → if nothing local, the app
calls the server. A digit-only result is validated against the accepted RC formats before
being shown.

---

## 7. API Endpoints

### 7.1 VKApiServer — Desktop API (port 5002)
Auth: `X-Api-Key` header. Source: `VKApiServer/Program.cs`.

**Login & dashboards**
- `POST /api/AppUsers/Login` — desktop manager login.
- `GET /api/Overview`, `/api/Finances`, `/api/AppUsers`, `/api/Uploads`,
  `/api/DetailsViews`, `/api/OTPs`, `/api/Reports`, `/api/Payments`,
  `/api/PaymentMethods`, `/api/Modules/{moduleKey}` — dashboard data.
- `GET /api/mgr/dashboard-stats` — record / finance / branch counts.

**Finances**
- `GET /api/mgr/finances` — list finances with branch + record counts.
- `POST /api/mgr/finances` — create.
- `PUT /api/mgr/finances/{id}` — rename.
- `DELETE /api/mgr/finances/{id}` — delete (cascades to branches + records).

**Branches**
- `GET /api/mgr/branches?financeId=` — list (optional filter).
- `GET /api/mgr/branches/{id}` — branch detail.
- `POST /api/mgr/branches` — create.
- `PUT /api/mgr/branches/{id}` — update.
- `POST /api/mgr/branches/{id}/clear` — wipe a branch's records, keep the branch.
- `DELETE /api/mgr/branches/{id}` — delete branch + records.

**Vehicle records**
- `POST /api/mgr/records/upload` — streaming bulk upload (gzip, ndjson progress back).
- `GET /api/mgr/search?q=&mode=rc|chassis` — desktop vehicle search.
- `POST /api/Records/MarkReleased/{id}` — toggle released/seized.
- `DELETE /api/Records/Delete/{id}` — delete one record.
- `POST /api/Records/PostRecordsFile` — file-based record post.

**Mobile users (management)**
- `GET /api/mgr/users` — full list with stats.
- `GET /api/mgr/users/picker`, `/api/mgr/users/all-simple` — lightweight lists.
- `GET /api/mgr/users/stats` — aggregate counts.
- `PATCH /api/mgr/users/{id}/active` — enable/disable.
- `PATCH /api/mgr/users/{id}/admin` — grant/revoke admin.
- `PATCH /api/mgr/users/{id}/stopped` — stop/resume the app for a user.
- `PATCH /api/mgr/users/{id}/blacklisted` — blacklist/unblacklist.
- `POST /api/mgr/users/{id}/reset-device` — clear bound device.
- `GET / PUT /api/mgr/users/{id}/finance-restrictions` — read/set blocked finances.
- `GET / PATCH /api/mgr/users/{id}/admin-pass` — per-admin Control Panel password.
- `GET /api/mgr/users/{id}/kyc` — KYC documents.
- `DELETE /api/mgr/users/{id}/kyc/{docType}` — delete a KYC doc.
- `GET /api/mgr/blacklist` — blacklisted users.

**Subscriptions**
- `GET /api/mgr/users/{id}/subscriptions` — list.
- `POST /api/mgr/users/{id}/subscriptions` — add.
- `DELETE /api/mgr/subscriptions/{id}` — delete.
- `GET / PUT /api/mgr/settings/subs-password` — shared subscription password.

**Device-change requests**
- `GET /api/mgr/device-requests` — pending requests.
- `POST /api/mgr/device-requests/{id}/approve` — approve (rebinds device).
- `DELETE /api/mgr/device-requests/{id}` — deny.

**Live tracking & audit**
- `GET /api/mgr/live-users?since=HH:mm` — agents seen since a time, with GPS.
- `GET /api/mgr/search-logs?fromDate=&toDate=&userId=&q=&export=` — audit log (+CSV).

**Exports** (used by ReportsPage and Finances downloads)
- `GET /api/mgr/export/users`
- `GET /api/mgr/export/subscriptions`
- `GET /api/mgr/export/vehicle-records` — sourced from `vehicle_records`.
- `GET /api/mgr/export/rc-records` — sourced from `rc_info` (RC numbers).
- `GET /api/mgr/export/chassis-records` — sourced from `chassis_info`.
- `GET /api/mgr/export/branch-records?branchId=` — one branch's vehicle records.
- `GET /api/mgr/export/finance-records?financeId=` — one finance's vehicle records.

**Column mapping config**
- `GET / POST /api/mgr/column-mappings`, `DELETE /api/mgr/column-mappings/{id}`,
  `POST /api/mgr/column-types`.

**Proxy**
- `ANY /api/mobile/{**rest}` — transparently forwarded to VKmobileapi (port 5001).

### 7.2 VKmobileapi — Mobile API (port 5001)
Auth: `X-User-Id` header. Source: `VKmobileapi/Controllers/MobileController.cs`.
All routes are under `/api/mobile/`.

**Registration & login**
- `POST register` — new user (starts inactive).
- `POST login` — mobile + deviceId; 403 if inactive, 409 on device mismatch.

**Search**
- `GET search/rc/{last4}` — search by last 4 RC digits (subscription enforced; cached).
- `GET search/chassis/{last5}` — search by last 5 chassis digits.

**Profile**
- `GET profile/{userId}` — full profile (info + bank + KYC + subscriptions).
- `PUT profile/{userId}/pfp` — update profile picture.
- `GET pfp/{userId}` — fetch profile picture.
- `GET profile/{userId}/subscriptions` — a user's plans (admin).

**Admin / Control Panel** (caller must be `is_admin`)
- `POST admin/verify-subs-pass` — verify the shared subscription password.
- `POST admin/verify-admin-pass` — verify the caller's Control Panel password.
- `GET admin/users` — list users with status flags.
- `POST admin/users/{targetUserId}/subscriptions` — add a plan.
- `DELETE admin/subscriptions/{subId}` — delete a plan.
- `PATCH admin/users/{targetUserId}/active|stopped|blacklisted` — status toggles.

**Sync & stats**
- `GET sync/branches` — branches with `uploaded_at` for staleness detection.
- `GET sync/records/{branchId}?page=&size=` — paginated records for offline cache.
- `GET stats` — global record / RC / chassis counts.

**Live tracking & audit**
- `POST heartbeat` — update last-seen + GPS.
- `GET live-users` — admin-only live agent list.
- `GET me/status` — the caller's stopped/blacklisted status.
- `POST search-log` — fire-and-forget vehicle-lookup log.

**Cache**
- `POST cache/invalidate` — flush the search cache (called after a desktop upload).
- `POST cache/invalidate-sub/{userId}` — flush one user's subscription cache.

---

## 8. Authentication & Security

- **Desktop app** — password login; afterwards every request carries the `X-Api-Key`
  header. The password is held in server config (`DESKTOP_LOGIN_PASSWORD`).
- **Mobile app** — login by mobile number + device ID. Afterwards requests carry
  `X-User-Id`. Accounts begin inactive and must be approved by an admin.
- **Device binding** — each user is locked to one device. Logging in from a new phone
  raises a device-change request that an admin must approve. Prevents account sharing.
- **Subscription enforcement** — vehicle search checks the user's latest subscription end
  date; an expired plan returns "payment required" and the app shows the expired screen.
- **Account gates** — `is_active`, `is_stopped`, `is_blacklisted` each route the mobile app
  to a dedicated lock screen.
- **Admin role** — `is_admin` unlocks the live-users map and the Control Panel. The Control
  Panel is additionally protected by a per-admin password (`admin_pass`).
- **RC number formats accepted** (validation + search) — standard `MH12AB1234`,
  legacy long-digit `HR736546`, and Bharat-series `22BH2271E`.

---

## 9. Deployment

```
Ubuntu server (CyberPanel + OpenLiteSpeed reverse proxy → api.characterverse.tech)
 ├── systemd service "vkapi"        → /opt/vkapi        (VKApiServer, port 5002)
 ├── systemd service "vkmobileapi"  → /opt/vkmobileapi  (VKmobileapi, port 5001)
 └── MySQL  (database vkre_db1, port 3306)
```

- Config (`/home/vkapp/db/.env.local`) holds MySQL credentials, JWT key, ports, and the
  desktop login password.
- `deploy.sh` pulls the latest code, runs `dotnet publish` for both API servers, copies the
  env file, restarts both systemd services, and syncs the installer + map assets to the
  web root.
- The Windows desktop app is packaged with **Inno Setup** into a single
  `VKEnterprises_Setup.exe`, published as a self-contained .NET 6 build (no separate .NET
  install needed), and hosted at `api.characterverse.tech/downloads/`.

---

## 10. One-line Summary

A three-tier vehicle-recovery platform: a **C#/WPF Windows admin app** and a
**Kotlin/Jetpack-Compose Android field app**, both backed by **two C#/ASP.NET Core 6 APIs**
over a shared **MySQL** database, featuring offline-first search, GPS field tracking,
subscription billing, KYC onboarding, device binding, and bulk Excel import/export.

---

*Generated from the live codebase.*
