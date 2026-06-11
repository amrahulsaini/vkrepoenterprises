# 06 — Finances & Branches 🏦

Manage the structure your vehicle records hang off: **financiers** (banks/NBFCs) and the **branches** under each one. This is also where you export a branch's or financier's records to Excel.

## Where the code is
- [Finances/FinancesManagerPage.xaml.cs](../../VKdesktopapp/Finances/FinancesManagerPage.xaml.cs)
- Dialogs: `NewFinanceDialog`, `BranchEditorWindow`, `BranchDialogWindow`, `BulkOperationDialog` (all in [Finances/](../../VKdesktopapp/Finances/)).

## The shape of the data (important mental model)

```
finance (HDFC)
  ├── branch (HDFC Jaipur)   ── 50,000 vehicle_records
  ├── branch (HDFC Kota)     ── 30,000 vehicle_records
  └── branch (HDFC Ajmer)    ── 12,000 vehicle_records
```

A **finance** has many **branches**; a **branch** has many **vehicle_records**. They're linked by IDs, and the database **cascades deletes** — delete a parent and all its children go too (this is the dangerous part, see below).

## How it loads

| Action | → endpoint | → tables |
|---|---|---|
| Open page | `GetFinancesAsync()` → `GET /api/mgr/finances` | `finances` (+ branch/record counts) |
| Click a finance | `GetBranchesByFinanceAsync(id)` → `GET /api/mgr/branches?financeId=…` | `branches` |

## What the buttons do

| Button | Calls | Effect |
|---|---|---|
| Add finance | `CreateFinanceAsync` → `POST /api/mgr/finances` | new `finances` row |
| Rename finance | `UpdateFinanceAsync` → `PUT /api/mgr/finances/{id}` | updates name |
| Add / edit branch | `CreateBranchAsync` / `UpdateBranchAsync` → `POST`/`PUT /api/mgr/branches` | `branches` row |
| Export to Excel | `DownloadBranchXlsx…` / `DownloadFinanceXlsx…` → `GET /api/mgr/export/branch-records.xlsx` etc. | server builds the .xlsx and streams it to your disk |
| ⚠️ **Clear branch records** | `ClearBranchRecordsAsync` → `POST /api/mgr/branches/{id}/clear` | **deletes ALL `vehicle_records` for that branch** (and their search-index rows), then resets the branch's count to 0 |
| ⚠️ **Delete branch** | `DeleteBranchAsync` → `DELETE /api/mgr/branches/{id}` | deletes the branch **and all its records** (cascade) |
| ⚠️ **Delete finance** | `DeleteFinanceAsync` → `DELETE /api/mgr/finances/{id}` | deletes the finance **→ all its branches → all their records** (cascade) |

## ⚠️ The destructive cascade — read this twice

Because of the parent→child links, **one click can erase a lot**:

- Delete a **branch** → its ~tens of thousands of records vanish.
- Delete a **finance** → *every* branch under it, and *every* record under those, vanish.

There's **no undo** — the rows are gone from the database. The server does this safely and quickly (it deletes in chunks at database speed), but the *decision* is irreversible. Always double-check which row is selected before deleting a parent.

## Trace it end-to-end (export a branch to Excel)

1. You pick "HDFC Jaipur" and click **Export to Excel**.
2. Page calls `DesktopApiClient.DownloadBranchXlsxChunkAsync(branchId, …)`.
3. That sends `GET /api/mgr/export/branch-records.xlsx?branchId=…`.
4. The server runs the query, **builds the .xlsx file on the server**, and streams the bytes back.
5. The app writes those bytes straight to the file you chose — no waiting to assemble the spreadsheet on your PC, so even huge branches export smoothly.

➡️ Next: [07 — App Users (Agents)](07-app-users.md)
