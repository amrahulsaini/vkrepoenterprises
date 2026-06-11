# 11 — Blacklist 🚫

A focused screen to block (or unblock) an agent quickly. It's the same "blacklist" switch from the App Users page, given its own simple screen.

## Where the code is
- [Blacklist/BlacklistPage.xaml.cs](../../VKdesktopapp/Blacklist/BlacklistPage.xaml.cs)

## How it loads

| Action | → endpoint | → table |
|---|---|---|
| Open page | `GetAllSimpleUsersAsync()` → `GET /api/mgr/users/all-simple` | `app_users` (slim list with the flags) |

## What it does

| Button | Calls | Effect |
|---|---|---|
| Blacklist / un-blacklist | `SetUserBlacklistedAsync(id, true/false)` → `PATCH /api/mgr/users/{id}/blacklisted` | sets `app_users.is_blacklisted` |

## What "blacklisted" actually does to the agent

It's not just a label. The server checks this flag on **every** request the agent's phone makes. So the moment you blacklist someone:

- their next search returns "blacklisted" → the app shows a blocked screen,
- their heartbeat returns "blacklisted" → the app blocks itself even if they're mid-action.

Effect is near-instant (within one heartbeat, a few seconds). Un-blacklisting reverses it just as fast.

## Trace it end-to-end

1. You find the agent in the list, click **Blacklist**.
2. Page → `SetUserBlacklistedAsync(id, true)` → `PATCH /api/mgr/users/{id}/blacklisted` with `{ blacklisted: true }`.
3. Server sets `is_blacklisted = 1` on that `app_users` row.
4. The agent's phone is cut off on its next call to the server.

## Blacklist vs Stop vs Deactivate (don't mix them up)

| Action | Agent sees | Use it when… |
|---|---|---|
| **Blacklist** | "blocked by agency" | you want to *ban* them |
| **Stop** (`is_stopped`) | "app stopped" | temporary pause (non-payment, etc.) |
| **Deactivate** (`is_active=0`) | "pending approval / inactive" | not yet approved or revoked access |

➡️ Next: [12 — Server Settings](12-server-settings.md)
