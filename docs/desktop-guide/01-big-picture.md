# 01 — The Big Picture 🗺️

Read this once and every page afterwards will make sense.

## What is this app, really?

`CRMRS.exe` is a **Windows desktop control panel** that an agency's admin uses to manage their vehicle-recovery data: financiers, branches, the millions of vehicle records, the field agents (the mobile-app users), their KYC, subscriptions, search audit logs, and so on.

The desktop app **does not talk to the database directly.** It talks to a **server** over the internet. The server talks to the database. Think of it like a banking app on your phone: the app never touches the bank's vault — it sends requests to the bank's servers, which do the actual work.

```
  YOU (admin)            INTERNET                 SERVER                  DATABASE
  ┌──────────┐   HTTPS   ┌──────────────┐   SQL   ┌──────────────────┐
  │ CRMRS.exe│ ────────▶ │  VKApiServer │ ──────▶ │ crmr_<your-agency>│
  │  (WPF)   │ ◀──────── │  (port 5002) │ ◀────── │   (MySQL/MariaDB) │
  └──────────┘   JSON    └──────────────┘  rows   └──────────────────┘
```

## What is WPF? (30-second version)

WPF is Microsoft's toolkit for building Windows desktop apps. Every screen is made of **two files that work as a pair**:

| File | What it is | Analogy |
|---|---|---|
| `SomethingPage.xaml` | The **layout** — buttons, tables, text boxes, colors. Written in XML-like markup. | The *blueprint / HTML* |
| `SomethingPage.xaml.cs` | The **code-behind** — what happens when you click things, where data comes from. Written in C#. | The *brains / JavaScript* |

When you see a button in `.xaml` like `<Button Click="btnSave_Click">`, there's a matching method `btnSave_Click(...)` in the `.xaml.cs` that runs when it's clicked. That's the whole idea.

## The 4 layers of the app

From what you see, down to the data:

```
┌─────────────────────────────────────────────────────────┐
│ 1. PAGES (the screens)   VKdesktopapp/*.xaml + *.xaml.cs │  ← what the user sees & clicks
├─────────────────────────────────────────────────────────┤
│ 2. THE API CLIENT        VKdesktopapp/Data/               │  ← the ONE place that talks to
│      DesktopApiClient.cs                                  │     the server (every page uses it)
├─────────────────────────────────────────────────────────┤
│ 3. THE SERVER (remote)   VKApiServer  /api/mgr/*          │  ← runs the SQL, returns JSON
├─────────────────────────────────────────────────────────┤
│ 4. THE DATABASE (remote) crmr_<agency>  tables            │  ← where data actually lives
└─────────────────────────────────────────────────────────┘
```

**The golden rule:** a page *never* writes SQL or opens a network socket itself. It calls a method on `DesktopApiClient`, and that one file ([VKdesktopapp/Data/DesktopApiClient.cs](../../VKdesktopapp/Data/DesktopApiClient.cs)) handles all the messy networking. So if you understand `DesktopApiClient`, you understand how *every* page gets its data.

## The universal loop (memorize this)

Every single page follows this loop. **Reading data:**

1. **Page loads** → its code-behind calls something like `await DesktopApiClient.GetFinancesAsync()`.
2. `DesktopApiClient` builds an HTTPS request to `https://api.crmrecoverysoftware.com/api/mgr/finances` and attaches two security headers (explained below).
3. The **server** receives it, runs `SELECT … FROM finances …` on *your agency's* database, and sends back **JSON** (text).
4. `DesktopApiClient` converts that JSON into C# objects (a `List<FinanceDto>`).
5. The page drops those objects into a table (`DataGrid`) or cards on screen.

**Changing data** (a button click) is the same loop, just with a different HTTP verb:

- **Create** → `POST` (e.g. "Add Finance")
- **Update** → `PUT` / `PATCH` (e.g. "Rename branch", "Toggle admin")
- **Delete** → `DELETE` (e.g. "Delete branch")

That's it. Every button on every page is one of: *read, create, update, delete*.

## The two security headers (how the server trusts the app)

Every request `DesktopApiClient` sends carries two things:

1. **`X-Api-Key`** — a fixed app key that says *"I'm an official CRMRS desktop app."* It's the same for all installs. (Set in [App.xaml.cs](../../VKdesktopapp/App.xaml.cs).)
2. **`Authorization: Bearer <token>`** — your **login token**, issued when you signed in. This token secretly contains *which agency you are*, so the server knows to open **your** database (`crmr_<your-slug>`) and nobody else's.

So: the API key proves *it's the real app*; the Bearer token proves *which agency is asking* and routes to the right database. (Full login story in [02 — Startup & Login](02-startup-and-login.md).)

## "Where does the data live?" — the tables you'll keep seeing

Your agency has its own database, `crmr_<slug>`, with these main tables:

| Table | Holds | Pages that use it |
|---|---|---|
| `finances` | Financiers / banks | Finances |
| `branches` | Branches under each financier | Finances, Upload |
| `vehicle_records` | The millions of vehicle/loan records | Search, Upload, Reports |
| `rc_info` / `chassis_info` | Fast-search index (last 4 of plate / last 5 of chassis) | Search |
| `app_users` | The field agents (mobile-app users) | App Users, Blacklist, Home |
| `user_kyc` | Agent KYC document images | App Users |
| `subscriptions` | Agent paid-access periods | App Users |
| `search_logs` | Every vehicle an agent looked up + GPS | Search Logs |
| `device_change_requests` | Pending "I got a new phone" requests | Home, App Users |
| `app_settings` | Agency settings (passwords) | Server Settings |

There's also a shared `crm_master` database that stores the agency's own profile (name, logo, contacts) and login info — Server Settings and Support read/write there.

## How to use the rest of this guide

Each page doc follows the same template so it's easy:

- **What it's for** (plain English)
- **Where the code is** (clickable file links)
- **What you see** (the UI)
- **How it loads** (the universal loop, with the *actual* endpoints + tables)
- **What each button does** (and ⚠️ which ones are destructive / have side-effects)
- **Trace it end-to-end** (follow one action from click to database and back)

➡️ Next: [02 — Startup & Login](02-startup-and-login.md)
