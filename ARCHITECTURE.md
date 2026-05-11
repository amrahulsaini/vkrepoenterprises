# VK Enterprises — System Architecture

## Overview

The system has three components that talk to one MySQL database:

```
┌─────────────────────┐        HTTPS        ┌──────────────────────────────┐
│  Desktop WPF App    │ ──────────────────► │  api.characterverse.tech     │
│  (Manager/Admin)    │                     │                              │
└─────────────────────┘                     │  OpenLiteSpeed (CyberPanel)  │
                                            │                              │
┌─────────────────────┐        HTTPS        │  /api/mobile/* ──► :5001     │
│  Android App        │ ──────────────────► │  /*             ──► :5002    │
│  (Field Users)      │                     └──────────────────────────────┘
└─────────────────────┘                               │              │
                                                      │              │
                                            ┌─────────┘    ┌─────────┘
                                            ▼              ▼
                                     ┌───────────┐  ┌────────────────┐
                                     │VKmobileapi│  │  VKApiServer   │
                                     │ port 5001 │  │  port 5002     │
                                     └─────┬─────┘  └──────┬─────────┘
                                           │               │
                                           └───────┬───────┘
                                                   ▼
                                         ┌──────────────────┐
                                         │  MySQL Database  │
                                         │  vkre_db1        │
                                         │  127.0.0.1:3306  │
                                         └──────────────────┘
```

---

## Database — MySQL (`vkre_db1`)

### Core Tables

| Table | Purpose |
|---|---|
| `vehicle_records` | Main vehicle records (RC, chassis, engine, customer info, branch, finance details) |
| `rc_info` | RC number index — stores `last4` digits, FK → `vehicle_records.id` |
| `chassis_info` | Chassis number index — stores `last5` digits, FK → `vehicle_records.id` |
| `finances` | Finance companies (e.g. HDFC, Bajaj) |
| `branches` | Branches under each finance company |
| `app_users` | Mobile app users — login, device ID, GPS location, subscription |
| `subscriptions` | Active/expired subscriptions per user |
| `device_change_requests` | Pending device-change requests raised when user logs in on new device |
| `user_kyc` | Aadhaar / PAN documents per user |

### Key Relationships

```
finances ──< branches ──< vehicle_records ──< rc_info
                                          └─< chassis_info

app_users ──< subscriptions
          └─< device_change_requests
          └─< user_kyc
```

### GPS Columns on `app_users`

```sql
last_seen  DATETIME   -- updated by mobile heartbeat every 15 min
last_lat   DOUBLE     -- GPS latitude
last_lng   DOUBLE     -- GPS longitude
```

### Search Indexes

`rc_info.last4` and `chassis_info.last5` are indexed so searching by the last
4 digits of an RC number or last 5 of a chassis number is instant even with
100k+ records.

### DB Credentials (production)

```
Host:     127.0.0.1
Port:     3306
User:     vkre_db1
Password: db1
Database: vkre_db1
```

---

## VKApiServer — Desktop/Manager API (port 5002)

ASP.NET Core 6 minimal API. Loaded via `LocalEnv.LoadBestEffort()` which reads
`db/.env.local` for credentials.

### Endpoints

| Method | Path | Description |
|---|---|---|
| POST | `/api/AppUsers/Login` | Desktop manager login |
| GET | `/api/mgr/dashboard-stats` | Total records, finances, branches count |
| GET | `/api/mgr/finances` | List all finance companies |
| POST | `/api/mgr/finances` | Create finance |
| PUT | `/api/mgr/finances/:id` | Rename finance |
| DELETE | `/api/mgr/finances/:id` | Delete finance |
| GET | `/api/mgr/branches` | List branches (optionally filtered by financeId) |
| GET | `/api/mgr/branches/:id` | Get branch detail |
| POST | `/api/mgr/branches` | Create branch |
| PUT | `/api/mgr/branches/:id` | Update branch |
| DELETE | `/api/mgr/branches/:id` | Delete branch + all records |
| POST | `/api/mgr/branches/:id/clear` | Delete all records in a branch |
| GET | `/api/mgr/users` | List all app users with stats |
| GET | `/api/mgr/users/stats` | User count stats (total, active, admins) |
| PATCH | `/api/mgr/users/:id/active` | Enable/disable user |
| PATCH | `/api/mgr/users/:id/admin` | Grant/revoke admin |
| POST | `/api/mgr/users/:id/reset-device` | Clear device ID |
| GET | `/api/mgr/users/:id/subscriptions` | List user subscriptions |
| POST | `/api/mgr/users/:id/subscriptions` | Add subscription |
| DELETE | `/api/mgr/subscriptions/:id` | Delete subscription |
| GET | `/api/mgr/device-requests` | Pending device-change requests |
| POST | `/api/mgr/device-requests/:id/approve` | Approve device change |
| DELETE | `/api/mgr/device-requests/:id` | Deny device change |
| GET | `/api/mgr/live-users?since=HH:mm` | Users with GPS seen since time today |
| GET | `/api/mgr/search?q=...&mode=rc\|chassis` | Vehicle search for desktop |
| POST | `/api/Records/Upload` | Bulk upload vehicle records (Excel) |
| DELETE | `/api/Records/Delete/:id` | Delete single record |
| **GET** | `/api/mobile/{**rest}` | **Proxy → VKmobileapi port 5001** |

### Auth

Desktop API uses `X-Api-Key: 12` header on all `/api/mgr/...` requests.
Login returns a JWT used for record operations.

---

## VKmobileapi — Mobile API (port 5001)

ASP.NET Core 6. Reached via the VKApiServer proxy at `/api/mobile/...`.
Uses same `db/.env.local` for credentials (copied by deploy.sh).

### Endpoints

| Method | Path | Description |
|---|---|---|
| POST | `/api/mobile/register` | New user registration |
| POST | `/api/mobile/login` | Login — returns userId, isAdmin, sub end date |
| GET | `/api/mobile/search/rc/:last4` | Search by last 4 digits of RC |
| GET | `/api/mobile/search/chassis/:last5` | Search by last 5 digits of chassis |
| GET | `/api/mobile/profile/:userId` | User profile + subscriptions |
| PUT | `/api/mobile/profile/:userId/pfp` | Update profile picture |
| GET | `/api/mobile/sync/branches` | Branch list for offline sync |
| GET | `/api/mobile/sync/records/:branchId` | Paginated records for offline sync |
| GET | `/api/mobile/stats` | Record/RC/chassis counts from DB |
| POST | `/api/mobile/heartbeat` | Update last_seen + GPS coordinates |
| GET | `/api/mobile/live-users` | Live user locations (admin only) |
| POST | `/api/mobile/cache/invalidate` | Clear server-side search cache |

### Search Cache

Search results are cached in-memory for 2 hours (key: `rc:XXXX` / `ch:XXXXX`).
Cache is invalidated after each desktop upload via `/api/mobile/cache/invalidate`.

---

## Desktop WPF App

Built with WPF (.NET 6) + WebView2 for the live map.

### Pages

| Page | Function |
|---|---|
| **HomePage** | Dashboard stats, live user map (Leaflet + OpenStreetMap), device-change request approval |
| **FinancePage** | Manage finance companies and their branches |
| **BranchPage** | Upload Excel records, view/delete records per branch |
| **UsersPage** | Manage mobile app users — activate, admin toggle, subscriptions |
| **VehicleSearchPage** | Search vehicles by RC last 4 or chassis last 5 |
| **LoginPage** | Desktop manager login |

### Live Map (HomePage)

- WebView2 renders `public/map_live.html` served via virtual host `http://vkapp.local/`
- Leaflet.js + OpenStreetMap tiles
- Red pins for each live user with name, mobile, location (reverse geocoded via Nominatim), and last-seen time
- Refreshes every 30 seconds; manual time filter (show users seen since HH:mm today)

### API Communication

All calls go to `https://api.characterverse.tech/` with header `X-Api-Key: 12`.

---

## Android App

Jetpack Compose + Hilt DI + Room local database + WorkManager for background GPS.

### Key Components

| Component | Role |
|---|---|
| `AuthViewModel` | Login state, userId, isAdmin stored in DataStore |
| `SearchViewModel` | RC / chassis search — local Room first, falls back to server |
| `SettingsViewModel` | Server stats, sync progress, force-sync trigger |
| `SyncRepository` | Downloads branches + paginated records from server into Room |
| `LocationWorker` | WorkManager periodic task — sends GPS heartbeat every 15 min |

### Local Cache (Room SQLite)

```
vehicle_cache     — id, branchId, vehicleNo, chassisNo, engineNo, model, customerName, last4, last5
branch_sync_state — branchId, uploadedAt
```

Search first hits Room (instant), then falls back to server API if not found.
Sync downloads all records per branch and replaces the local cache.

### Offline Search Flow

```
User types last4/last5
       │
       ▼
Room SQLite query (indexed on last4 / last5)
       │
  found? ──Yes──► show results
       │
      No
       │
       ▼
Server API query (/api/mobile/search/rc/:last4)
       │
       ▼
show results
```

---

## Deployment

### Server

- Ubuntu 22.04 + CyberPanel (OpenLiteSpeed)
- Domain: `api.characterverse.tech` → OpenLiteSpeed → VKApiServer (:5002)
- Path `/api/mobile/*` → VKApiServer proxy → VKmobileapi (:5001)

### Deploy Command (run on server after every push)

```bash
bash /home/vkapp/deploy.sh
```

This: pulls latest code → builds VKApiServer → copies env → restarts vkapi service →
builds VKmobileapi → copies env → restarts vkmobileapi service.

### Environment File (`/home/vkapp/db/.env.local`)

```
MYSQL_HOST=127.0.0.1
MYSQL_USER=vkre_db1
MYSQL_PASSWORD=db1
MYSQL_DATABASE=vkre_db1
MYSQL_PORT=3306
PRIVATEKEY=vk_enterprises_local_jwt_key
DESKTOP_LOGIN_PASSWORD=12
```

---

## Data Flow — Excel Upload

```
Desktop App
  │  select Excel file
  │
  ▼
POST /api/Records/Upload (multipart)
  │
  ▼
VKApiServer parses Excel rows
  │
  ├─► INSERT INTO vehicle_records (bulk pipeline)
  ├─► REPLACE INTO rc_info (last4 index)
  ├─► REPLACE INTO chassis_info (last5 index)
  └─► UPDATE branches SET total_records=..., uploaded_at=NOW()
  │
  ▼
POST /api/mobile/cache/invalidate
  │
  ▼
VKmobileapi clears in-memory search cache
  │
  ▼
Mobile apps pick up new records on next sync
```

---

## Data Flow — Mobile Search

```
Android App types last4
  │
  ▼
Room SQLite (local cache)
  │── hit ──► results shown instantly (offline capable)
  │
  └── miss ──► GET /api/mobile/search/rc/{last4}
                │
                ▼
           VKmobileapi
                │
                ▼
           SELECT ... FROM rc_info
           JOIN vehicle_records
           JOIN branches
           JOIN finances
           WHERE rc_info.last4 = ?
                │
                ▼
           JSON response → shown in app
```
