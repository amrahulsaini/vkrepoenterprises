# VK Repo Enterprises — Full Technical Architecture

> **Prepared for:** Client Technical Review  
> **Date:** May 2026  
> **Scope:** Complete system — Android mobile app, Windows desktop app, backend APIs, database

---

## Table of Contents

1. [Technology Stack](#1-technology-stack)
2. [System Architecture Overview](#2-system-architecture-overview)
3. [Database Schema](#3-database-schema)
4. [VKApiServer — Desktop API](#4-vkapiserver--desktop-api-port-5002)
5. [VKmobileapi — Mobile API](#5-vkmobileapi--mobile-api-port-5001)
6. [Android Mobile App](#6-android-mobile-app)
7. [Desktop WPF Application](#7-desktop-wpf-application)
8. [Authentication & Authorization](#8-authentication--authorization)
9. [Deployment Architecture](#9-deployment-architecture)
10. [Key Architecture Decisions](#10-key-architecture-decisions)
11. [System Summary](#11-system-summary)

---

## 1. Technology Stack

| Component | Language | Framework / Library | Runtime |
|---|---|---|---|
| **Backend API — Desktop** | C# | ASP.NET Core 6 | .NET 6 |
| **Backend API — Mobile** | C# | ASP.NET Core 6 | .NET 6 |
| **Desktop Application** | C# | WPF (Windows Presentation Foundation) | .NET 6 |
| **Mobile Application** | Kotlin | Jetpack Compose, Hilt DI, Room, WorkManager | Android SDK 34 |
| **Database** | SQL | MySQL 10.11 | — |
| **Embedded Map (Desktop)** | JavaScript | Leaflet.js + OpenStreetMap | WebView2 |
| **Deployment OS** | Bash | systemd + OpenLiteSpeed (CyberPanel) | Ubuntu 22.04 |

---

## 2. System Architecture Overview

### Architectural Style

The system is a **three-tier client-server application**. Two clients (Android app and Windows desktop app) communicate with two separate ASP.NET Core 6 backends, which share a single MySQL database. All external traffic enters through **VKApiServer** — the desktop API also serves as a transparent reverse proxy for all mobile API traffic.

### Topology Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          PRODUCTION SERVER                              │
│                Ubuntu 22.04 — api.characterverse.tech                   │
│                                                                         │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │             VKApiServer  (ASP.NET Core 6 — Port 5002)              │ │
│  │          All inbound traffic enters here via OpenLiteSpeed         │ │
│  │                                                                    │ │
│  │   ┌──────────────────────────┐   ┌──────────────────────────────┐ │ │
│  │   │     /api/mgr/*           │   │   /api/mobile/** (proxy)     │ │ │
│  │   │  Desktop management API  │   │   Forwards to :5001          │ │ │
│  │   └────────────┬─────────────┘   └──────────────┬───────────────┘ │ │
│  │                │                                 │                 │ │
│  │                ▼                                 ▼                 │ │
│  │        ┌───────────────┐            ┌────────────────────────┐    │ │
│  │        │   MySQL DB    │◄───────────│  VKmobileapi (:5001)   │    │ │
│  │        │   vkre_db1    │            │  ASP.NET Core 6        │    │ │
│  │        └───────────────┘            └────────────────────────┘    │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
                 ▲                                       ▲
                 │  HTTPS                                │  HTTPS
                 │  X-Api-Key: 12                        │  X-User-Id header
                 │                                       │
        ┌─────────────────┐                   ┌──────────────────────┐
        │  WPF Desktop    │                   │   Android App        │
        │  App (C#/.NET)  │                   │   (Kotlin)           │
        │                 │                   │   Jetpack Compose    │
        │  Admin-only     │                   │   Room + WorkManager │
        │  Windows only   │                   │   Field agents       │
        └─────────────────┘                   └──────────────────────┘
```

### Request Routing Summary

| Client | Auth | Routes to |
|---|---|---|
| WPF Desktop App | `X-Api-Key: 12` header | VKApiServer `/api/mgr/*` directly |
| Android App | `X-User-Id` header | VKApiServer `/api/mobile/*` → proxied to VKmobileapi |

---

## 3. Database Schema

### Database: `vkre_db1` (MySQL 10.11)

### Entity-Relationship Diagram

```
finances (1) ──────< branches (1) ──────< vehicle_records (1) ──< rc_info
                                                              └──< chassis_info

app_users (1) ──< subscriptions
             ├──< device_change_requests
             ├──< user_kyc
             └──< search_logs

column_types (1) ──< column_mappings
```

---

### Table Reference

#### `finances`
Stores finance company entities (e.g., HDFC, Bajaj Finance).

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| name | VARCHAR UNIQUE | Company name |
| description | TEXT | Optional description |
| is_active | TINYINT | Soft-delete flag |

---

#### `branches`
Each finance company can have multiple branches.

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| finance_id | INT FK | → `finances.id` |
| name | VARCHAR | Branch name |
| address | TEXT | Physical address |
| contact1, contact2, contact3 | VARCHAR | Up to 3 contacts |
| uploaded_at | DATETIME | Timestamp of last record upload |
| total_records | INT | Denormalized record count |
| is_active | TINYINT | Active flag |

---

#### `vehicle_records`
Core table — all vehicle data uploaded by the desktop admin.

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| branch_id | INT FK | → `branches.id` |
| vehicle_no | VARCHAR | Full RC number |
| chassis_no | VARCHAR | Full chassis number |
| engine_no | VARCHAR | Engine number |
| model | VARCHAR | Vehicle model |
| customer_name | VARCHAR | Owner/customer name |
| agreement_no | VARCHAR | Loan agreement number |
| bucket | VARCHAR | Loan bucket / DPD category |
| gv | VARCHAR | Guaranteed value |
| od | VARCHAR | Outstanding dues |
| seasoning | VARCHAR | Loan seasoning |
| tbr_flag | VARCHAR | TBR flag |
| sec9_available | VARCHAR | Section 9 status |
| sec17_available | VARCHAR | Section 17 status |
| region | VARCHAR | Region |
| area | VARCHAR | Area |
| level1–level4 | VARCHAR | Hierarchy levels |
| branch_name_raw | VARCHAR | Raw branch name from source file |
| created_at | DATETIME | Insert timestamp |

---

#### `rc_info` — Search Index for RC Numbers
Separate table with indexed `last4` column for instant suffix search.

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| vehicle_record_id | INT FK | → `vehicle_records.id` |
| rc_number | VARCHAR | Full RC number |
| model | VARCHAR | Vehicle model |
| **last4** | VARCHAR(4) | **INDEXED** — last 4 digits of RC |

---

#### `chassis_info` — Search Index for Chassis Numbers

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| vehicle_record_id | INT FK | → `vehicle_records.id` |
| chassis_number | VARCHAR | Full chassis number |
| model | VARCHAR | Vehicle model |
| **last5** | VARCHAR(5) | **INDEXED** — last 5 digits of chassis |

> **Design note:** Splitting last4/last5 into dedicated indexed tables enables O(1) suffix lookup even on 1M+ record datasets, without full-table scans or LIKE queries.

---

#### `app_users`
Mobile app users — field agents and admins.

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| mobile | VARCHAR UNIQUE | Phone number (login identifier) |
| name | VARCHAR | Full name |
| address | TEXT | Address |
| pincode | VARCHAR | PIN code |
| pfp | LONGTEXT | Profile picture (base64 encoded) |
| device_id | VARCHAR | Bound Android device ID |
| is_active | TINYINT | 0 = pending/disabled, 1 = active |
| is_admin | TINYINT | Admin flag |
| balance | DECIMAL | Account balance |
| last_seen | DATETIME | Last heartbeat timestamp |
| last_lat | DOUBLE | Last GPS latitude |
| last_lng | DOUBLE | Last GPS longitude |

---

#### `subscriptions`

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| user_id | INT FK | → `app_users.id` |
| start_date | DATE | Subscription start |
| end_date | DATE | Subscription end |
| amount | DECIMAL | Amount paid |
| notes | TEXT | Admin notes |

---

#### `user_kyc`
One KYC record per user (stored as base64 images).

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| user_id | INT FK UNIQUE | → `app_users.id` |
| aadhaar_front | LONGTEXT | Base64 image |
| aadhaar_back | LONGTEXT | Base64 image |
| pan_front | LONGTEXT | Base64 image |

---

#### `device_change_requests`
Queued requests from users who changed their device.

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| user_id | INT | Requesting user |
| user_name | VARCHAR | Snapshot of name |
| user_mobile | VARCHAR | Snapshot of mobile |
| new_device_id | VARCHAR | New device identifier |
| requested_at | DATETIME | Request timestamp |

---

#### `search_logs`
Full audit trail of every vehicle lookup made by field agents.

| Column | Type | Notes |
|---|---|---|
| id | INT PK AUTO | — |
| user_id | INT FK | → `app_users.id` |
| vehicle_no | VARCHAR | RC number searched |
| chassis_no | VARCHAR | Chassis number searched |
| model | VARCHAR | Vehicle model |
| lat | DOUBLE | Agent GPS latitude at search time |
| lng | DOUBLE | Agent GPS longitude at search time |
| address | VARCHAR | Reverse-geocoded address |
| device_time | DATETIME | Timestamp from agent's device |
| server_time | DATETIME | Timestamp recorded by server |

---

#### `column_mappings` / `column_types`
Configuration for Excel import column normalization.

| Table | Key Columns |
|---|---|
| `column_types` | id, name, sort_order |
| `column_mappings` | id, column_type_id (FK), name (normalized alias) |

---

## 4. VKApiServer — Desktop API (Port 5002)

**Source:** `VKApiServer/Program.cs` (1572 lines), `VKApiServer/Models.cs` (361 lines)

**Base URL:** `https://api.characterverse.tech/`  
**Auth:** All `/api/mgr/*` endpoints require HTTP header `X-Api-Key: 12`  
**Config:** Credentials loaded from `db/.env.local` at startup  
**Pool:** 20 MySQL connections, max request body 200 MB  

---

### 4.1 Login

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/AppUsers/Login` | Desktop manager login — validates password, returns Base64 JWT token |

---

### 4.2 Finance Management

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/finances` | List all finance companies with branch count and total record count |
| `POST` | `/api/mgr/finances` | Create a new finance company |
| `PUT` | `/api/mgr/finances/{id}` | Rename a finance company |
| `DELETE` | `/api/mgr/finances/{id}` | Delete finance company — **cascades** to all branches and all their vehicle records |

---

### 4.3 Branch Management

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/branches?financeId={id}` | List branches — optional filter by finance ID |
| `GET` | `/api/mgr/branches/{id}` | Get full branch detail including contacts and record count |
| `POST` | `/api/mgr/branches` | Create a new branch under a finance company |
| `PUT` | `/api/mgr/branches/{id}` | Update branch name, address, and contacts |
| `DELETE` | `/api/mgr/branches/{id}` | Delete branch and all its vehicle records |
| `POST` | `/api/mgr/branches/{id}/clear` | Wipe all vehicle records for a branch while keeping the branch itself |

---

### 4.4 Bulk Vehicle Record Upload

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/mgr/records/upload` | Streaming bulk upload of vehicle records — gzip-compressed pipe-delimited body |

**Detailed Upload Flow:**

```
1. Client sends gzip-compressed request body:
      Line 1    → branchId (integer)
      Line 2+   → 32 pipe-delimited fields per vehicle record

2. Server processing:
   a. Decompress gzip UTF-8 stream
   b. Clear all existing vehicle records for the branch
   c. SqlBulkCopy into vehicle_records (batched writes)
   d. Rebuild rc_info table  (extracts and indexes last4 of RC number)
   e. Rebuild chassis_info table (extracts and indexes last5 of chassis)
   f. Stream real-time ndjson progress back to client:
         {"pct": 0,   "msg": "Clearing old records..."}
         {"pct": 25,  "msg": "Inserting 45000 records..."}
         {"pct": 75,  "msg": "Rebuilding RC index..."}
         {"pct": 100, "msg": "Done. 45000 records loaded."}
   g. POST /api/mobile/cache/invalidate  → flush mobile search cache
```

---

### 4.5 Individual Record Operations

| Method | Endpoint | Description |
|---|---|---|
| `DELETE` | `/api/Records/Delete/{id}` | Delete a single vehicle record by ID |
| `POST` | `/api/Records/MarkReleased/{id}` | Toggle the released/seized status of a record |

---

### 4.6 User Management

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/users` | List all mobile users with subscription status, KYC status, and last activity |
| `GET` | `/api/mgr/users/picker` | Lightweight user list without profile pictures — for dropdowns |
| `GET` | `/api/mgr/users/stats` | Aggregate counts: total users, active users, subscribed users |
| `PATCH` | `/api/mgr/users/{id}/active` | Enable or disable a user account (`is_active` toggle) |
| `PATCH` | `/api/mgr/users/{id}/admin` | Grant or revoke admin role (`is_admin` toggle) |
| `POST` | `/api/mgr/users/{id}/reset-device` | Clear bound `device_id` — forces re-login on next device use |

---

### 4.7 Subscription Management

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/users/{id}/subscriptions` | List all subscription records for a specific user |
| `POST` | `/api/mgr/users/{id}/subscriptions` | Create a subscription (start date, end date, amount, notes) |
| `DELETE` | `/api/mgr/subscriptions/{id}` | Delete a subscription record |

---

### 4.8 Device Change Requests

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/device-requests` | List all pending device-change requests |
| `POST` | `/api/mgr/device-requests/{id}/approve` | Approve request — updates `app_users.device_id` to the new device |
| `DELETE` | `/api/mgr/device-requests/{id}` | Deny and discard the request |

---

### 4.9 Vehicle Search (Desktop)

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/search?q={query}&mode=rc` | Search vehicles by last 4 digits of RC number |
| `GET` | `/api/mgr/search?q={query}&mode=chassis` | Search vehicles by last 5 digits of chassis number |

---

### 4.10 Search Audit Logs

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/search-logs?fromDate=&toDate=&userId=&q=` | Paginated audit log of all mobile vehicle searches with filters |
| `GET` | `/api/mgr/search-logs?...&export=true` | Same as above but returns a downloadable CSV file |

---

### 4.11 Live User Tracking

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/live-users?since=HH:mm` | Returns all users who sent a heartbeat since the given time, with their GPS coordinates |

---

### 4.12 Dashboard

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/dashboard-stats` | Top-level counts: total records, finances, branches |
| `GET` | `/api/Overview` | Overview dashboard data |
| `GET` | `/api/Finances` | Finance dashboard data |
| `GET` | `/api/AppUsers` | Users dashboard data |
| `GET` | `/api/Uploads` | Uploads dashboard data |

---

### 4.13 Column Mappings (Excel Import Config)

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mgr/column-mappings` | List all Excel column type → name mappings |
| `POST` | `/api/mgr/column-mappings` | Create a new mapping entry |
| `DELETE` | `/api/mgr/column-mappings/{id}` | Delete a mapping |
| `POST` | `/api/mgr/column-types` | Create a new column type definition |

---

### 4.14 Mobile Reverse Proxy

| Method | Endpoint | Description |
|---|---|---|
| `ANY` | `/api/mobile/{**rest}` | Transparent reverse proxy — all `/api/mobile/*` traffic forwarded to VKmobileapi on port 5001 |

---

## 5. VKmobileapi — Mobile API (Port 5001)

**Source:** `VKmobileapi/Controllers/MobileController.cs` (291 lines)

**Base path:** All endpoints prefixed with `/api/mobile/`  
**Auth:** `X-User-Id` header (userId from login response)  
**Cache:** In-memory search cache with 2-hour TTL, keyed as `rc:XXXX` / `ch:XXXXX`  

---

### 5.1 Registration & Login

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/mobile/register` | Register a new user. Body: `mobile`, `name`, `address`, `pincode`, `pfpBase64`, `deviceId`, `aadhaarFront`, `aadhaarBack`, `panFront`. Account starts inactive pending admin approval. |
| `POST` | `/api/mobile/login` | Login with `mobile` + `deviceId`. Returns `userId`, `isAdmin`, `subscriptionEndDate`, `pfpBase64`. Returns `403` if inactive, `409` if device mismatch. |

---

### 5.2 Vehicle Search

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mobile/search/rc/{last4}` | Search by last 4 digits of RC number. Requires active subscription. Returns `402 Payment Required` if expired. Results cached 2 hours. |
| `GET` | `/api/mobile/search/chassis/{last5}` | Search by last 5 digits of chassis number. Same subscription enforcement and caching rules. |

**Search Flow:**

```
GET /api/mobile/search/rc/{last4}
        │
        ▼
Check in-memory cache  (key: "rc:{last4}")
        │ cache miss
        ▼
SELECT v.*, r.rc_number
FROM rc_info r
JOIN vehicle_records v ON r.vehicle_record_id = v.id
WHERE r.last4 = '{last4}'
        │
        ▼
Store result in cache  (TTL: 2 hours)
        │
        ▼
Return JSON array of matching vehicle records
```

---

### 5.3 User Profile

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mobile/profile/{userId}` | Full profile: user info + all subscriptions + KYC status |
| `PUT` | `/api/mobile/profile/{userId}/pfp` | Update profile picture (base64 body) |
| `GET` | `/api/mobile/pfp/{userId}` | Fetch profile picture as base64 string |

---

### 5.4 Offline Sync (Incremental / Resumable)

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/mobile/sync/branches` | List all branches with `total_records` and `uploaded_at` timestamp — used to detect stale local data |
| `GET` | `/api/mobile/sync/records/{branchId}?page=0&size=500` | Paginated vehicle records for a branch. Max page size: 5000. |
| `GET` | `/api/mobile/stats` | Global counts: total records, RC entries, chassis entries |

**Sync Algorithm (executed by Android app):**

```
GET /api/mobile/sync/branches
   └─ For each branch:
         Compare server uploaded_at  vs  local BranchSyncState.uploadedAt
         │
         ├── Timestamp changed → Full re-download  (reset offset to 0)
         │
         └── Same timestamp   → Resume from stored page offset
                  │
                  └─ GET /sync/records/{branchId}?page=N&size=2000
                        ├── Upsert into Room (VehicleCache)
                        ├── Update BranchSyncState.offset = N+1
                        └── Repeat until response count < page size
```

---

### 5.5 GPS Heartbeat & Live Tracking

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/mobile/heartbeat` | Update `last_seen`, `last_lat`, `last_lng` for the user. Called every 15 minutes by WorkManager. |
| `GET` | `/api/mobile/live-users` | Admin only (`is_admin = 1`). Returns all users with recent GPS positions. |

---

### 5.6 Search Audit Logging

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/mobile/search-log` | Fire-and-forget. Logs vehicle view with: `userId`, `vehicleNo`, `chassisNo`, `model`, `lat`, `lng`, `address`, `deviceTimeIso`. Both device and server timestamps recorded. |

---

### 5.7 Cache Invalidation

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/mobile/cache/invalidate` | Flush entire in-memory search cache. Called by VKApiServer after every bulk upload. |
| `POST` | `/api/mobile/cache/invalidate-sub/{userId}` | Flush subscription cache for a specific user. Called after subscription changes. |

---

## 6. Android Mobile App

**Location:** `android/`  
**Language:** Kotlin  
**Min SDK:** 26 (Android 8.0) | **Target SDK:** 34 (Android 14)  
**Build system:** Gradle with Kotlin DSL

---

### 6.1 Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        UI Layer                             │
│            Jetpack Compose Screens + Material 3             │
│    SplashScreen · HomeScreen · ProfileScreen · Settings…    │
└────────────────────────┬────────────────────────────────────┘
                         │  StateFlow / LiveData
┌────────────────────────▼────────────────────────────────────┐
│                    ViewModel Layer                          │
│      AuthViewModel · SearchViewModel · SettingsViewModel    │
│      ProfileViewModel                                       │
└────────────────────────┬────────────────────────────────────┘
                         │  suspend functions (Coroutines)
┌────────────────────────▼────────────────────────────────────┐
│                   Repository Layer                          │
│      AuthRepository · SearchRepository · SyncRepository    │
└──────────────┬──────────────────────────────┬──────────────┘
               │                              │
┌──────────────▼──────────┐    ┌─────────────▼──────────────┐
│  Room Database (SQLite) │    │ Retrofit + OkHttp + Gson   │
│  VehicleCache           │    │ API calls to VKApiServer   │
│  BranchSyncState        │    │                            │
└─────────────────────────┘    └────────────────────────────┘
```

**Dependency Injection:** Hilt (Dagger-Hilt) — all repositories, ViewModels, and WorkManager workers are injected at compile time.

**Session storage:** Jetpack DataStore (PreferencesManager) — persists `userId`, `name`, `mobile`, `isAdmin`, `subscriptionEndDate`, `pfpBase64` across app restarts.

---

### 6.2 Local Room Database Entities

| Entity | Indexed Columns | Purpose |
|---|---|---|
| `VehicleCache` | `last4`, `last5`, `branchId` | Local offline copy of all vehicle records for instant search |
| `BranchSyncState` | `branchId` | Tracks sync `uploadedAt` timestamp and current page offset per branch |

---

### 6.3 Screens

| Screen | Purpose |
|---|---|
| `SplashScreen` | Checks stored session → routes to Login or Home |
| `LoginScreen` | Mobile number + device ID entry |
| `RegisterScreen` | Full registration: name, address, pincode, profile picture, Aadhaar front/back, PAN |
| `WaitingApprovalScreen` | Displayed when account is inactive or device mismatch detected |
| `HomeScreen` | Main search UI — user enters last 4 (RC) or last 5 (chassis) digits; local Room query first, server fallback |
| `VehicleDetailScreen` | All vehicle fields displayed; fires search log to server with GPS on view |
| `ConfirmScreen` | Seizure/repossession confirmation flow |
| `SubscriptionExpiredScreen` | Shown on HTTP 402 response from search endpoints |
| `ProfileScreen` | User info, subscription history, KYC status |
| `SettingsScreen` | Sync statistics, force full sync button, global record counts |
| `LiveUsersScreen` | Admin only — map view of all active field agents with GPS pins |

---

### 6.4 Background Workers (WorkManager)

| Worker | Trigger | Action |
|---|---|---|
| `LocationWorker` | Periodic — every 15 minutes | Gets fresh GPS fix via `FusedLocationProviderClient`. Falls back to last known location if unavailable. Posts `POST /api/mobile/heartbeat`. Runs as a foreground service — survives app kill. |
| `SyncWorker` | On-demand or scheduled | Downloads all vehicle branches and records incrementally. Writes to Room. Resumable from last offset. |

---

### 6.5 Android Permissions

| Permission | Purpose |
|---|---|
| `INTERNET` | API communication |
| `READ_MEDIA_IMAGES` | Profile picture and KYC document upload |
| `ACCESS_FINE_LOCATION` | Precise GPS for heartbeat and search logging |
| `ACCESS_COARSE_LOCATION` | Fallback GPS |
| `FOREGROUND_SERVICE` | Background location worker |
| `FOREGROUND_SERVICE_LOCATION` | Foreground service with location type |

---

### 6.6 Offline-First Search Flow

```
User enters 4-digit RC suffix
          │
          ▼
Room.searchByLast4(last4)              ← indexed, sub-millisecond
          │
          ├── Results found → Display immediately
          │
          └── No local results
                    │
                    ▼
          GET /api/mobile/search/rc/{last4}   ← server call
                    │
                    ├── 200 OK           → display results
                    ├── 402 Expired      → SubscriptionExpiredScreen
                    └── Error            → show error state

                    [User taps a vehicle card]
                              │
                              ▼
                    POST /api/mobile/search-log  (fire-and-forget)
                    logs: userId, vehicleNo, GPS coords,
                          device timestamp, server timestamp
```

---

## 7. Desktop WPF Application

**Location:** `VKdesktopapp/`  
**Language:** C#  
**Framework:** WPF (Windows Presentation Foundation) on .NET 6  
**Platform:** Windows only

---

### 7.1 Key Dependencies

| Library | Version | Purpose |
|---|---|---|
| Microsoft.Web.WebView2 | 1.0.2903.40 | Embeds Chromium browser for Leaflet.js live map |
| Syncfusion.WPF | Latest | DataGrid, PropertyGrid, PDF export, Spreadsheet |
| MySqlConnector | 2.5.0 | Legacy direct MySQL (mostly replaced by API calls) |
| FontAwesome5 | — | UI icon set |

---

### 7.2 Pages and Windows

| Page / Window | Purpose |
|---|---|
| `LoginWindow` | Password entry — validated against VKApiServer |
| `HomePage` | Dashboard stats cards + live Leaflet map. Polls `/api/mgr/live-users` every 30 seconds. Shows device-change request approval cards. |
| `FinancesManagerPage` | Full CRUD for finance companies and their branches, with record counts |
| `BranchEditorWindow` | Modal — create or edit a branch: name, address, 3 contacts, branch code |
| `BulkOperationDialog` | File picker → streaming upload → real-time progress bar reading ndjson stream |
| `AppUsersManagerPage` | Full user management: activate, admin toggle, subscriptions, reset device |
| `SubscriptionEditorWindow` | Modal — add or edit subscription: dates, amount, notes |
| `ConfirmationsManagerPage` | View and update vehicle repossession confirmation records |
| `SearchLogPage` | Searchable audit trail of all mobile searches — date range, user filter, CSV export |
| `VehicleSearchPage` | Desktop vehicle search by last4 (RC) or last5 (chassis) |

---

### 7.3 Live Map (HomePage)

- Embedded via **WebView2** loading `public/map_live.html` on virtual host `http://vkapp.local/`
- Renders **Leaflet.js** tiles from **OpenStreetMap**
- Red pin per live user showing: name, mobile, GPS coordinates, last-seen timestamp
- Reverse geocoding via **Nominatim API** (lat/lng → human-readable address)
- Auto-refreshes every 30 seconds; supports manual `since=HH:mm` time filter

---

### 7.4 Desktop Data Layer

| File | Purpose |
|---|---|
| `Data/DesktopApiClient.cs` | All HTTP calls to VKApiServer — case-insensitive JSON deserialization |
| `Data/EnvLoader.cs` | Loads credentials from `.env.local` at startup |
| `Data/FinanceRepository.cs` | Finance CRUD wrappers |
| `Data/BranchRepository.cs` | Branch CRUD wrappers |
| `Data/AppUserRepository.cs` | User management wrappers |
| `Data/VehicleSearchRepository.cs` | Search query wrappers |
| `Data/RecordsRepository.cs` | Record operations |

---

## 8. Authentication & Authorization

### 8.1 Mobile User Lifecycle

```
1. User installs app → fills registration form (name, mobile, KYC docs)
         │
         ▼
2. POST /api/mobile/register
   → account created with is_active = 0 (pending approval)
         │
         ▼
3. Admin sees new user in desktop app
   → reviews KYC documents
   → approves: PATCH /api/mgr/users/{id}/active  (sets is_active = 1)
         │
         ▼
4. User logs in: POST /api/mobile/login  (mobile + deviceId)
         │
         ├── is_active = 0     → HTTP 403 → WaitingApprovalScreen
         │
         ├── device_id mismatch → HTTP 409
         │         → device_change_request inserted in DB
         │         → WaitingApprovalScreen (pending admin device approval)
         │
         └── All checks pass
               → { userId, isAdmin, subscriptionEndDate, pfpBase64 }
               → Stored in DataStore → user enters app
```

---

### 8.2 Subscription Enforcement

```
User calls GET /api/mobile/search/rc/{last4}
         │
         ▼
Server: SELECT MAX(end_date) FROM subscriptions WHERE user_id = ?
         │
         ├── end_date < today  → HTTP 402 → SubscriptionExpiredScreen
         │
         └── Valid subscription → proceed with search and return results
```

---

### 8.3 Device Binding

- Each user is bound to exactly one `device_id` stored in `app_users`
- Logging in from a new device returns HTTP 409 and inserts a `device_change_requests` row
- Admin approves from desktop → `app_users.device_id` updated to new device
- **Prevents credential sharing** across multiple physical devices

---

### 8.4 Desktop Manager Auth

```
Desktop login → POST /api/AppUsers/Login { password }
         │
         ▼
Server validates against DESKTOP_LOGIN_PASSWORD env var
         │
         ▼
Returns Base64 JWT:  base64(mobile + ":" + timestamp + ":" + PRIVATEKEY)
         │
         ▼
All subsequent requests include:  X-Api-Key: 12  header
```

---

### 8.5 Admin Role

- `is_admin = 1` in `app_users` grants access to:
  - `GET /api/mobile/live-users` — all field agent GPS positions
  - `LiveUsersScreen` in the mobile app
- Set and revoked exclusively by desktop admin via `PATCH /api/mgr/users/{id}/admin`

---

## 9. Deployment Architecture

### Server Stack

```
Ubuntu 22.04 LTS
│
├── CyberPanel  (control panel)
│   └── OpenLiteSpeed  (reverse proxy)
│         └── api.characterverse.tech → :5002
│
├── systemd: vkapi
│   └── /opt/vkapi/VKApiServer         (ASP.NET Core 6, port 5002)
│
├── systemd: vkmobileapi
│   └── /opt/vkmobileapi/VKmobileapi   (ASP.NET Core 6, port 5001)
│
└── MySQL 10.11  (local, port 3306)
      └── Database: vkre_db1
```

---

### Environment File

**Location:** `/home/vkapp/db/.env.local`  
Loaded at startup by both API services via `LocalEnv.cs`.

| Variable | Purpose |
|---|---|
| `MYSQL_HOST` | Database host (`127.0.0.1`) |
| `MYSQL_USER` | MySQL username |
| `MYSQL_PASSWORD` | MySQL password |
| `MYSQL_DATABASE` | Database name (`vkre_db1`) |
| `MYSQL_PORT` | Port (3306) |
| `PRIVATEKEY` | JWT signing key |
| `DESKTOP_LOGIN_PASSWORD` | Desktop manager password |

---

### Deployment Script (`deploy.sh`)

```bash
git pull origin main

# VKApiServer
dotnet publish VKApiServer -c Release -o /opt/vkapi
cp /home/vkapp/db/.env.local /opt/vkapi/db/.env.local
systemctl restart vkapi

# VKmobileapi
dotnet publish VKmobileapi -c Release -o /opt/vkmobileapi
cp /home/vkapp/db/.env.local /opt/vkmobileapi/db/.env.local
systemctl restart vkmobileapi
```

---

## 10. Key Architecture Decisions

| Decision | Detail | Rationale |
|---|---|---|
| **Server-side SQL aggregation** | Desktop app never queries MySQL directly — all SQL runs inside VKApiServer | Eliminates high-latency WAN round-trips; one HTTP call replaces N DB queries |
| **Streaming bulk upload (ndjson)** | Progress events streamed via ndjson during `SqlBulkCopy` inserts | Desktop shows live progress bar for multi-million-row uploads without HTTP timeout |
| **Dedicated indexed search tables** | `rc_info.last4` and `chassis_info.last5` as separate indexed columns | Sub-millisecond suffix lookup on 1M+ records — no LIKE queries, no full-table scans |
| **In-memory search cache (2h TTL)** | Search results cached in VKmobileapi process memory | Absorbs repeated identical queries; flushed atomically after every desktop upload |
| **Resumable offline sync** | Per-branch `uploaded_at` comparison + page offset stored in Room | Handles network interruptions without re-downloading already-synced pages |
| **Offline-first mobile search** | Room local query fires before any network call | Instant results for field agents even without connectivity |
| **WorkManager GPS heartbeat** | `PeriodicWorkRequest` every 15 min as foreground service | Survives app kill; falls back to cached GPS if fresh fix unavailable |
| **Device binding + approval queue** | One `device_id` per user; changes need explicit admin approval | Prevents credential sharing and unauthorized device handoffs |
| **WebView2 Leaflet map in WPF** | Chromium via WebView2 loading local HTML on virtual host `http://vkapp.local/` | Full Leaflet.js capability in a native desktop window; avoids WPF mapping limitations |
| **Dual timestamps in search logs** | Both `device_time` and `server_time` stored on every search log | Enables forensic detection of device clock manipulation by field agents |
| **Fire-and-forget audit logging** | `POST /api/mobile/search-log` not awaited | Does not block the UI; logging is secondary to the search experience |

---

## 11. System Summary

### What the System Does

VK Repo Enterprises is a **vehicle repossession field operations platform** consisting of:

- A **Windows desktop application** used by administrators to manage vehicle data, users, subscriptions, and approvals
- **Two cloud-hosted ASP.NET Core 6 REST APIs** handling all business logic and database access
- An **Android mobile application** used by field agents for real-time vehicle lookup and GPS tracking
- A **MySQL database** storing all vehicle records, user accounts, subscriptions, audit logs, and KYC documents

---

### Capabilities at a Glance

| Capability | Detail |
|---|---|
| Vehicle records | Millions of records across multiple finance companies and branches |
| Offline search | Full dataset synced to each field device via resumable incremental download |
| Search performance | Sub-millisecond indexed suffix lookup (last4 RC / last5 chassis) |
| GPS tracking | Field agent positions updated every 15 minutes; visible on real-time admin map |
| Audit trail | Every vehicle lookup logged with GPS coordinates, device time, and server time |
| Bulk data upload | Multi-million-row gzip uploads with streaming progress bar |
| KYC onboarding | Aadhaar (front/back) and PAN capture at registration |
| Device security | One-device-per-user binding with admin-controlled change approval |
| Subscription control | Per-user date-based subscription enforcement with HTTP 402 on expiry |
| Admin tools | Live user map, search log export (CSV), user activation, device reset |

---

### Component File Reference

| Component | Key Source File | Lines |
|---|---|---|
| Desktop API — all endpoints | `VKApiServer/Program.cs` | 1572 |
| Desktop API — data models | `VKApiServer/Models.cs` | 361 |
| Mobile API — all endpoints | `VKmobileapi/Controllers/MobileController.cs` | 291 |
| Android — Retrofit API interface | `android/.../network/ApiService.kt` | 60 |
| Android — auth state & session | `android/.../viewmodel/AuthViewModel.kt` | 133 |
| Android — search (local + server) | `android/.../repository/SearchRepository.kt` | 46 |
| Android — incremental sync | `android/.../repository/SyncRepository.kt` | 137 |
| Desktop client — API calls | `VKdesktopapp/Data/DesktopApiClient.cs` | 400+ |
| Database schema | `latest.sql` | 475 |
| Deploy script | `deploy.sh` | — |

---

*End of document.*
