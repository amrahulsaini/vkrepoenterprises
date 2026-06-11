# 05 — Find Vehicle (Search) 🔍

Look up a vehicle the same way a field agent would: by the **last 4 of the number plate** or the **last 5 of the chassis**, then open the full record.

## Where the code is
- [Records/FindVehiclePage.xaml.cs](../../VKdesktopapp/Records/FindVehiclePage.xaml.cs)

## What you see
- A search box + a toggle for **RC (plate)** vs **Chassis**.
- A results list (slim: plate, model, financer, branch).
- Clicking a result opens the **full detail** (customer, address, dues, all contact levels).

## Why "last 4 / last 5"? (the clever bit)

The `vehicle_records` table can have **millions** of rows. Searching the whole plate every time would be slow. So when records are uploaded, the server also fills two tiny helper tables:

- `rc_info` → stores the **last 4** of each plate + a link to the full record.
- `chassis_info` → stores the **last 5** of each chassis + a link.

These are **indexed**, so "find everything ending in `4521`" is instant. The search hits the helper table first, then pulls the full row only when you open one.

## How it loads (two-stage: list, then detail)

**Stage 1 — search (slim list):**

| Action | → endpoint | → tables |
|---|---|---|
| Type 4/5 chars + search | `GET /api/mgr/search` or `/api/mgr/search/list` | `rc_info`/`chassis_info` → joined to `vehicle_records`, `branches`, `finances` |

**Stage 2 — open one result (full detail):**

| Action | → endpoint | → tables |
|---|---|---|
| Click a result | `GET /api/mgr/record/{id}` | `vehicle_records` + `branches` + `finances` |

Two stages = the list stays fast and small, and the heavy detail is fetched only when you actually need it.

## Trace it end-to-end (search "4521" by plate)

1. You type `4521`, mode = RC, press search.
2. Page → `DesktopApiClient` → `GET /api/mgr/search/list?q=4521&mode=rc`.
3. Server: `SELECT … FROM rc_info WHERE last4='4521'` → joins to `vehicle_records` for plate/model + `branches`/`finances` for names.
4. Returns a small JSON list. The page shows it in the results grid.
5. You click a row → `GET /api/mgr/record/12345` → server returns *that one* full record → the page shows the detail panel.

## Notes
- **Read-only for your real data.** This page only *reads* `vehicle_records`; it doesn't change them.
- The **"Mark Released"** button uses an old/legacy endpoint (`/api/Records/MarkReleased/...`) that points at a database that no longer exists for agencies, so treat it as inactive.
- This is the *same* data your field agents search from the mobile app — the desktop just gives admins a window into it.

➡️ Next: [06 — Finances & Branches](06-finances-and-branches.md)
