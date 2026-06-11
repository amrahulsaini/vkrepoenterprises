# CRMRS Desktop App — Learn-It Guide 🎓

A from-scratch, plain-English walkthrough of the **WPF desktop admin app** (`CRMRS.exe`, code in [VKdesktopapp/](../../VKdesktopapp/)). Read it top to bottom and you'll understand how every screen works.

## How to read this guide

**Start with the foundation** (these teach the one pattern everything reuses):

1. [01 — The Big Picture](01-big-picture.md) ← *read this first.* What the app is, the layers, and the **universal data-flow loop** every page follows.
2. [02 — Startup & Login](02-startup-and-login.md) — what happens from double-click to the dashboard.
3. [03 — The Shell & Navigation](03-shell-and-navigation.md) — the window frame, the menu, how pages switch.

**Then each page module** (each one is just the universal loop with different endpoints/tables):

4. [04 — Home / Dashboard](04-home-dashboard.md)
5. [05 — Find Vehicle (Search)](05-find-vehicle-search.md)
6. [06 — Finances & Branches](06-finances-and-branches.md)
7. [07 — App Users (Agents)](07-app-users.md)
8. [08 — Upload Records](08-upload-records.md)
9. [09 — Reports & Exports](09-reports-and-exports.md)
10. [10 — Search Logs](10-search-logs.md)
11. [11 — Blacklist](11-blacklist.md)
12. [12 — Server Settings](12-server-settings.md)
13. [13 — Support](13-support.md)
14. [14 — Direct Data](14-direct-data.md)

## The one sentence that explains the whole app

> Every screen does the same 4 things: **(1)** the page asks `DesktopApiClient` for data → **(2)** that sends an HTTPS request to the server → **(3)** the server runs SQL on your agency's database and returns JSON → **(4)** the page shows it. Buttons do the same loop in reverse to create/update/delete.

Once that clicks, all 11 pages are the same thing wearing different clothes.
