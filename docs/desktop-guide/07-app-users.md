# 07 — App Users (Agents) 👤

The most powerful page. This is where you manage your **field agents** (the people who use the mobile app): approve them, review their KYC, give/revoke access, grant subscriptions, and remove them.

## Where the code is
- [AppUsers/AppUsersManagerPage.xaml.cs](../../VKdesktopapp/AppUsers/AppUsersManagerPage.xaml.cs)
- [AppUsers/SubscriptionEditorWindow.xaml.cs](../../VKdesktopapp/AppUsers/SubscriptionEditorWindow.xaml.cs)

## What you see
- A table of agents with stats (total / active / admins / with subscription).
- For each agent: toggles, KYC documents, subscription editor, and danger actions.

## The agent "switches" (flags) — what each one means

Every agent row in `app_users` has on/off flags. The desktop flips them:

| Flag | Meaning when ON |
|---|---|
| `is_active` | Agent is approved and can use the app |
| `is_admin` | Agent can use the in-app admin Control Panel |
| `is_stopped` | App is paused for this agent ("App Stopped" screen) |
| `is_blacklisted` | Agent is blocked entirely |

## How it loads

| Action | → endpoint | → tables |
|---|---|---|
| Open page | `GetUsersWithStatsAsync()` → `GET /api/mgr/users` | `app_users` (+ `subscriptions` for end-date) |
| View an agent's KYC | `GetUserKycAsync(id)` → `GET /api/mgr/users/{id}/kyc` | `app_users`, `user_kyc` (+ images) |
| View subscriptions | `GetSubscriptionsAsync(id)` → `GET /api/mgr/users/{id}/subscriptions` | `subscriptions` |

## What the buttons do

| Button | Calls | Effect (table) |
|---|---|---|
| Activate / deactivate | `SetUserActiveAsync` → `PATCH …/active` | `app_users.is_active` |
| Make / remove admin | `SetUserAdminAsync` → `PATCH …/admin` | `app_users.is_admin` |
| Stop / start app | `SetUserStoppedAsync` → `PATCH …/stopped` | `app_users.is_stopped` |
| Reset device | `ResetUserDeviceAsync` → `POST …/reset-device` | clears `device_id` + pending device requests |
| Mark KYC verified / reject | `SetUserKycStatusAsync` → `PATCH …/kyc-status` | see ⚠️ below |
| Add / delete subscription | `AddSubscriptionAsync` / `DeleteSubscriptionAsync` | `subscriptions` |
| Restrict to financiers | `SetUserFinanceRestrictionsAsync` → `PUT …/finance-restrictions` | `user_finance_restrictions` |
| ⚠️ **Delete agent** | `DeleteUserAsync` → `DELETE /api/mgr/users/{id}` | see ⚠️ below |

## ⚠️ Two actions with hidden side-effects

1. **Reject KYC** doesn't only set the status to "failed" — it **also deactivates the agent** (`is_active = 0`). One click both rejects *and* locks them out.
2. **Delete agent** removes the `app_users` row (which cascades to their KYC, subscriptions, search logs, device requests) **and** clears their entry in the shared cross-agency registry — so that mobile number is freed up to register again (with you or another agency).

## How approval actually works (the agent's journey)

1. Agent registers on the phone → lands in your list as **inactive, KYC pending**.
2. You open their **KYC** here, eyeball the Aadhaar/PAN/selfie, and click **Mark Verified**.
3. You **activate** them (and usually **grant a subscription**).
4. Now the agent can log in and search. If they misbehave, you **stop** or **blacklist** them — which takes effect on their phone within seconds (the next heartbeat).

## Trace it end-to-end (blacklist an agent)

1. You click **Blacklist** on an agent.
2. Page → `SetUserBlacklistedAsync(id, true)` → `PATCH /api/mgr/users/{id}/blacklisted`.
3. Server sets `app_users.is_blacklisted = 1` in your database.
4. On the agent's phone, the very next search/heartbeat asks the server "am I OK?" → server says "blacklisted" → the app blocks them. No need to message them; it's instant.

➡️ Next: [08 — Upload Records](08-upload-records.md)
