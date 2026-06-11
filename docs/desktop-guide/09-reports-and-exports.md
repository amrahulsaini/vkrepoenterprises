# 09 — Reports & Exports 📊

Download your data as **Excel** or **PDF**: the full vehicle records, the agents list, or the subscriptions list.

## Where the code is
- [Reports/ReportsPage.xaml.cs](../../VKdesktopapp/Reports/ReportsPage.xaml.cs)
- Helpers: [Exports/ChunkedExportDialog.xaml.cs](../../VKdesktopapp/Exports/ChunkedExportDialog.xaml.cs), [Exports/VehicleExcelWriter.cs](../../VKdesktopapp/Exports/VehicleExcelWriter.cs)

## What you can export

| Report | Calls | → endpoint | → table |
|---|---|---|---|
| Vehicle records | `ExportVehicleRecordsPageAsync` / `DownloadRecordsXlsxChunkAsync` | `GET /api/mgr/export/vehicle-records[.xlsx]` | `vehicle_records` |
| RC records | `ExportRcRecordsPageAsync` | `GET /api/mgr/export/rc-records` | `vehicle_records` via `rc_info` |
| Chassis records | `ExportChassisRecordsPageAsync` | `GET /api/mgr/export/chassis-records` | `vehicle_records` via `chassis_info` |
| Agents | `ExportUsersAsync` | `GET /api/mgr/export/users` | `app_users` |
| Subscriptions | `ExportSubscriptionsAsync` | `GET /api/mgr/export/subscriptions` | `subscriptions` |

## Two ways data comes back (and why)

- **Excel (.xlsx)** → the **server builds the spreadsheet** and streams it straight to your disk. Best for huge data — your PC just saves bytes, it doesn't assemble the file.
- **PDF** → the app pulls the rows **page by page** (in batches) as JSON and lays them out into a PDF locally.

## Why "paged" / "chunked"?

You can't load 4 million rows into memory at once — it'd freeze the app. So exports fetch in **pages** (e.g. 5,000 rows at a time) and either stream them to a file or append to the document. The `ChunkedExportDialog` shows the progress while it loops through the pages.

## Trace it end-to-end (export all vehicles to Excel)

1. You pick "Vehicle Records → Excel" and choose a save location.
2. The app calls `DownloadRecordsXlsxChunkAsync("vehicle-records", …)`.
3. Request → `GET /api/mgr/export/vehicle-records.xlsx?…`.
4. The server queries `vehicle_records`, writes the .xlsx **on the server side**, and streams it down.
5. The app writes the bytes to your file as they arrive, showing a progress bar by bytes received.

## Read-only and safe

Everything on this page only **reads** data — nothing is created, changed, or deleted. You can't break anything here; worst case an export is slow on a big dataset.

➡️ Next: [10 — Search Logs](10-search-logs.md)
