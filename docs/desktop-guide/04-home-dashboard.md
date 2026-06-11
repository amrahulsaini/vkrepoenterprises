# 04 — Home / Dashboard 🏠

The landing screen. A quick health check of the agency: totals, agent stats, who's online right now (on a live map), and any pending "new phone" requests.

## Where the code is
- [HomePage.xaml](../../VKdesktopapp/HomePage.xaml) / [HomePage.xaml.cs](../../VKdesktopapp/HomePage.xaml.cs)

## What you see
- **Stat cards** — total vehicle records, total finances, total branches, total/active/admin agents.
- **Device-change requests** — a list of agents asking to switch phones, each with **Approve** / **Deny**.
- **Live map** — pins of agents active in the last ~15 minutes (embedded web map).

## How it loads (the universal loop, 4 calls at once)

When the page opens, it fires **four requests in parallel** (so the dashboard fills fast even if one is slow). Each is wrapped so one failure can't blank the whole page:

| The page calls… | → endpoint | → reads tables |
|---|---|---|
| `GetDashboardStatsAsync()` | `GET /api/mgr/dashboard-stats` | `vehicle_records`, `finances`, `branches` |
| `GetUserStatsAsync()` | `GET /api/mgr/users/stats` | `app_users` |
| `GetDeviceRequestsAsync()` | `GET /api/mgr/device-requests` | `device_change_requests` |
| `GetLiveUsersAsync(since)` | `GET /api/mgr/live-users` | `app_users` (last seen + GPS) |

The live map is a small web page (`public/map_live.html`) shown inside the app; the code pushes the agent pins into it.

## What the buttons do

| Button | Calls | Effect |
|---|---|---|
| **Approve** (device request) | `ApproveDeviceRequestAsync(id)` → `POST /api/mgr/device-requests/{id}/approve` | ⚠️ **Two effects:** updates that agent's `device_id` in `app_users` to the new phone **and** deletes the request. The agent can now log in on the new device. |
| **Deny** (device request) | `DenyDeviceRequestAsync(id)` → `DELETE /api/mgr/device-requests/{id}` | Deletes the request; the agent stays locked to the old phone. |

## Trace it end-to-end (Approve a device request)

1. You click **Approve** on Ramesh's request.
2. Code-behind calls `DesktopApiClient.ApproveDeviceRequestAsync(42)`.
3. That sends `POST /api/mgr/device-requests/42/approve` with your API key + token.
4. Server opens **your** database, sets `app_users.device_id` = Ramesh's new phone ID, and deletes row 42 from `device_change_requests`.
5. Server replies `OK`.
6. The page refreshes the request list → Ramesh's row disappears. He can now sign in on his new phone.

## Why "live users" matters

Each agent's phone quietly sends a **heartbeat** (its GPS) every few seconds. The server stores it on the agent's `app_users` row (`last_seen`, `last_lat`, `last_lng`). The dashboard's "live users" call just asks "who has a `last_seen` in the last 15 minutes?" and maps them. So the map is powered by the mobile app, not the desktop.

➡️ Next: [05 — Find Vehicle (Search)](05-find-vehicle-search.md)
