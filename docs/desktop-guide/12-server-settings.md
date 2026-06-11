# 12 — Server Settings ⚙️

Edit your **agency's own profile** (name, address, contact numbers shown to agents) and set two agency-wide passwords. Opens as its own pop-up window from the gear icon.

## Where the code is
- [ServerSettingsWindow.xaml.cs](../../VKdesktopapp/ServerSettingsWindow.xaml.cs)

## Two different storage places (notice this)

This screen touches **two** databases:

| Setting | → endpoint | → where it's stored |
|---|---|---|
| Agency profile (name, address, mobiles) | `GET`/`POST /api/agency/desktop/profile` | the shared `crm_master.agencies` row |
| Control-panel password | `GET`/`PUT /api/mgr/settings/control-password` | your tenant `app_settings` |
| Subscriptions password | `GET`/`PUT /api/mgr/settings/subs-password` | your tenant `app_settings` |

Notice the profile uses `/api/agency/...` (authenticated by your **login token only**, no API key) while the passwords use `/api/mgr/...`. Different doors, same app.

## ⚠️ The profile edit has a ripple effect

Your agency profile (especially the contact numbers) is the **same row the mobile app reads** for its in-app "Agency" panel. So when you change your contact number here, **every field agent's app shows the new number too**, automatically. One edit, two places updated.

## What the two passwords are for

- **Control-panel password** — agents who are admins must type this to open the in-app Control Panel on the phone.
- **Subscriptions password** — a gate before an admin can grant/revoke subscriptions on the phone.

These let you delegate some admin power to trusted agents without handing over the desktop.

## How it loads

1. Open Settings → the code calls `GetAgencyProfileAsync()`, `GetControlPasswordAsync()`, `GetSubsPasswordAsync()` and fills the fields.
2. You edit, click **Save** → `SaveAgencyProfileAsync(...)` + (if changed) `SetControlPasswordAsync(...)` / `SetSubsPasswordAsync(...)`.

## Trace it end-to-end (change the agency phone number)

1. You update "Primary mobile" and click Save.
2. Window → `SaveAgencyProfileAsync(name, address, mobile1, …)` → `POST /api/agency/desktop/profile`.
3. Server (using your login token to know which agency) updates your row in `crm_master.agencies`.
4. Next time any agent opens the "Agency" panel in the mobile app, it reads that same row → shows your new number.

➡️ Next: [13 — Support](13-support.md)
