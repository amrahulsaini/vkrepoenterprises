# 08 — Upload Records 📤

How millions of vehicle/loan records get *into* the system. This is the most technically involved screen — it takes a financier's messy Excel file and loads it into a branch.

## Where the code is
- [Records/RecordsEditorWindow.xaml.cs](../../VKdesktopapp/Records/RecordsEditorWindow.xaml.cs) — the Excel-like spreadsheet editor.
- [Records/RecordValidatorAndUploaderWindow.xaml.cs](../../VKdesktopapp/Records/RecordValidatorAndUploaderWindow.xaml.cs) — checks the data and uploads it.
- [Records/AddMappingWindow.xaml.cs](../../VKdesktopapp/Records/AddMappingWindow.xaml.cs) — teaches the app what each column means.
- The actual upload logic: `UploadRecordsAsync` in [Data/DesktopApiClient.cs](../../VKdesktopapp/Data/DesktopApiClient.cs).

## The problem this solves

Every financier sends Excel files with **different column headers** ("Veh No" vs "Vehicle Number" vs "RegnNo"). The app needs to map those to its own fields. And the files are **huge** (tens of thousands to millions of rows) on **weak agency internet**. So the upload has to be smart.

## The flow (4 stages)

1. **Open / paste the Excel** into the spreadsheet editor.
2. **Map the columns** — tell the app "this column is the plate number, that one is the customer name." Mappings are remembered (`column_types` / `column_mappings` tables) so next time the same header auto-maps.
3. **Validate** — the app checks the rows look sane.
4. **Upload** to a chosen branch → `POST /api/mgr/records/upload`.

## How the upload is made reliable (the clever engineering)

A single giant upload would die on any network hiccup and you'd have to start over. Instead:

- The rows are **compressed** (gzip) and **split into chunks of 25,000**.
- Each chunk is uploaded as its own request, and **retried** if the network blips — so a hiccup only re-sends ~1 MB, not the whole file.
- The server streams back **live progress** so you see a moving bar.

The chunks coordinate with a `mode` flag so "replace this branch's data" works correctly across many requests:

| mode | meaning |
|---|---|
| `begin` | clear the branch's old records, insert this first chunk |
| `append` | insert this chunk |
| `finish` | insert the last chunk, then finalize |

## ⚠️ Multi-effect action — what "finish" really does

When the last chunk lands, the server does several things in one go:
1. Inserts the records into `vehicle_records`.
2. **Rebuilds the search indexes** (`rc_info` last-4 and `chassis_info` last-5) so the new records are instantly searchable.
3. **Updates the branch's stats** (`branches.total_records`, upload time).

And because the data changed, the mobile app's search cache should be refreshed (`POST /api/mobile/cache/invalidate`) so agents see the new records right away.

## Trace it end-to-end (upload 60,000 records)

1. You map columns, pick "HDFC Jaipur", click **Upload**.
2. `UploadRecordsAsync` splits 60,000 rows into 3 chunks of ~20–25k, gzips each.
3. Chunk 1 → `POST /api/mgr/records/upload?mode=begin` → server clears old Jaipur records + inserts chunk 1.
4. Chunk 2 → `…?mode=append` → inserts chunk 2.
5. Chunk 3 → `…?mode=finish` → inserts chunk 3, rebuilds `rc_info`/`chassis_info`, updates branch stats.
6. The progress bar reaches 100%, and those 60,000 vehicles are now searchable on every agent's phone.

➡️ Next: [09 — Reports & Exports](09-reports-and-exports.md)
