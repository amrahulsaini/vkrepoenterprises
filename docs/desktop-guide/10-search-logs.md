# 10 — Search Logs (Details Views) 🧾

An audit trail: **which agent looked up which vehicle, when, and where** (with GPS + address). Great for accountability and for tracing a recovery.

## Where the code is
- [Records/DetailsViewsPage.xaml.cs](../../VKdesktopapp/Records/DetailsViewsPage.xaml.cs)
- Helpers: `MapPointWindow` (shows a log's location on a map), `UserPickerWindow` (filter by agent).

## Where the data comes from

Every time a field agent **opens a vehicle's full detail** on the phone, the app records it: a row in `search_logs` with the agent, the vehicle, the time, and the phone's GPS. The server even reverse-geocodes the GPS into a human address. This page just reads that table.

## How it loads

| Action | → endpoint | → tables |
|---|---|---|
| Open page / apply date filter | `GetSearchLogsAsync(from, to)` → `GET /api/mgr/search-logs` | `search_logs` joined to `app_users` |
| Pick an agent to filter | `GetPickerUsersAsync()` → `GET /api/mgr/users/picker` | `app_users` |
| Export | `DownloadSearchLogsXlsxAsync(...)` → `GET /api/mgr/search-logs.xlsx` | `search_logs` |

## What you can do

| Action | Effect |
|---|---|
| Filter by date range | re-queries `search_logs` between two dates |
| Filter by agent | only that agent's lookups |
| Search text (`q`) | filter by vehicle/agent text |
| 🗺️ View on map | opens `MapPointWindow` with that lookup's GPS pin |
| Export to Excel | server streams the filtered logs as .xlsx |

## Trace it end-to-end (see where an agent searched yesterday)

1. You set the date to yesterday, pick agent "Suresh", click apply.
2. Page → `GetSearchLogsAsync(from, to, userId=Suresh)` → `GET /api/mgr/search-logs?fromDate=…&toDate=…&userId=…`.
3. Server: `SELECT … FROM search_logs WHERE user_id=… AND server_time BETWEEN …` joined to `app_users` for the name.
4. The grid fills with each lookup (vehicle, time, address).
5. You click 🗺️ on a row → a map window opens showing exactly where Suresh was standing when he pulled up that vehicle.

## Read-only

This page only reads logs — it never changes anything. The logs themselves are written *by the mobile app*, not the desktop.

➡️ Next: [11 — Blacklist](11-blacklist.md)
