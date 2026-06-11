# 03 — The Shell & Navigation 🧭

The "shell" is the window frame that's always there — the menu on the side, the title bar, the agency logo — and the area in the middle where pages appear.

## The code
- [MainWindow.xaml](../../VKdesktopapp/MainWindow.xaml) — the frame layout (menu buttons, logo, the content area).
- [MainWindow.xaml.cs](../../VKdesktopapp/MainWindow.xaml.cs) — wires the menu clicks to pages.

## What is a "Page"?

The app has **one window** (`MainWindow`). Inside it is a content area called `PageContainer`. Each screen — Home, Search, Finances, etc. — is a **`Page`** that gets swapped into that container. Switching menu items just changes *which page is showing*; the window itself never changes.

Analogy: the window is a **picture frame**, and pages are **photos** you slide in and out of it.

## Pages are created once, then reused

When `MainWindow` opens, it builds the main pages a single time and keeps them:

```csharp
_homePage            = new HomePage();
_findVehiclePage     = new FindVehiclePage();
_financesManagerPage = new FinancesManagerPage();
_appUsersManagerPage = new AppUsersManagerPage();
_detailsViewsPage    = new DetailsViewsPage();
_confirmationsPage   = new ConfirmationsManagerPage();
_reportsPage         = new ReportsPage();
_blacklistPage       = new BlacklistPage();
_directDataPage      = new DirectDataPage();
```

Keeping them around means switching back to a page is instant (it doesn't rebuild from scratch).

## How a menu click switches pages

Each menu button has a `Tag` (a little label). When you click one, `btnNav_Click` reads the tag and shows the matching page:

```csharp
case "Home":     LoadPage(_homePage);            break;
case "Search":   LoadPage(_findVehiclePage);     break;
case "Finances": LoadPage(_financesManagerPage); break;
case "Users":    LoadPage(_appUsersManagerPage); break;
...
```

And `LoadPage` is literally one line:

```csharp
private void LoadPage(Page page) => PageContainer.Navigate(page);
```

That's the entire navigation system. Click → read tag → `Navigate(page)`.

## The menu map

| Menu / tile | Opens | Guide |
|---|---|---|
| **Home** | Dashboard | [04](04-home-dashboard.md) |
| **Search** | Find Vehicle | [05](05-find-vehicle-search.md) |
| **Finances** | Finances & Branches | [06](06-finances-and-branches.md) |
| **Users** | App Users (agents) | [07](07-app-users.md) |
| **Upload Records** | Excel editor/uploader (opens its own window) | [08](08-upload-records.md) |
| **Reports** | Reports & Exports | [09](09-reports-and-exports.md) |
| **Details Views** | Search Logs | [10](10-search-logs.md) |
| **Blacklist** | Blacklist agents | [11](11-blacklist.md) |
| ⚙️ **Settings** | Server Settings (own window) | [12](12-server-settings.md) |
| 🎧 **Support** | Support tickets (own window) | [13](13-support.md) |
| **Direct Data** | Provider file ingestion | [14](14-direct-data.md) |

> Some items (Upload Records, Settings, Support) open in their **own pop-up window** instead of swapping into the content area — because they're big tools or modal dialogs. The rest swap into `PageContainer`.

## Other things the shell does

- **Shows your agency branding** — on load it downloads your agency logo and shows your name/contact in the menu header (falls back to the CRMRS default if you're not signed in as an agency).
- **Support badge** — every 60 seconds it checks if CRMRS support replied to your tickets and shows a little red unread count on the 🎧 icon.
- **Window controls** — the custom title bar handles minimize / maximize / close / drag (the app draws its own frame instead of the standard Windows one).

➡️ Now the pages. Start with [04 — Home / Dashboard](04-home-dashboard.md).
