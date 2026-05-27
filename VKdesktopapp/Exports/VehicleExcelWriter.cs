using System.Collections.Generic;
using Syncfusion.XlsIO;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Exports;

// Shared Excel writer for vehicle rows. Identical column order whether the
// caller is FinancesManagerPage (Finance/Branch) or ReportsPage (vehicle/RC/
// chassis records) so downstream consumers get one stable schema. Used by
// ChunkedExportDialog to write each chunk to its own .xlsx file.
//
// Perf note: the previous implementation wrote one cell at a time
// (ws[r,c].Text = ...) which is ~3.4M Syncfusion calls for a 100k-row, 34-col
// chunk. That alone took 20+ seconds per file. We now build a 2D object[,]
// in pure managed memory and hand it to Range.SetValue, which Syncfusion
// streams into the underlying SST + sheet in one shot — typically 8-15x
// faster. AutoFitColumns is also dropped for big chunks because it forces a
// second full-sheet scan; we set a sane fixed width instead.
internal static class VehicleExcelWriter
{
    private static readonly string[] Headers =
    {
        "Vehicle No", "Chassis No", "Engine No", "Model", "Agreement No",
        "Customer Name", "Customer Contact", "Customer Address",
        "Finance Name", "Branch Name", "Bucket", "GV", "OD", "Seasoning",
        "TBR Flag", "Sec9 Available", "Sec17 Available",
        "Level1", "Level1 Contact", "Level2", "Level2 Contact",
        "Level3", "Level3 Contact", "Level4", "Level4 Contact",
        "Sender Mail 1", "Sender Mail 2", "Executive Name",
        "POS", "TOSS", "Remark", "Region", "Area", "Created On"
    };

    public static void Write(
        List<DesktopApiClient.ExportVehicleRow> rows,
        string sheetName,
        string filePath)
    {
        using var engine = new ExcelEngine();
        var xlApp = engine.Excel;
        xlApp.DefaultVersion = ExcelVersion.Xlsx;
        var wb = xlApp.Workbooks.Create(1);
        var ws = wb.Worksheets[0];
        ws.Name = sheetName.Length > 31 ? sheetName[..31] : sheetName;

        int cols = Headers.Length;

        // ── Header row ────────────────────────────────────────────────────
        // Only 34 cells — cell-by-cell is fine here, the perf cost was in
        // the data rows.
        for (int c = 0; c < cols; c++)
        {
            var cell = ws[1, c + 1];
            cell.Text = Headers[c];
            cell.CellStyle.Font.Bold  = true;
            cell.CellStyle.Color      = System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1A);
            cell.CellStyle.Font.Color = ExcelKnownColors.White;
        }

        // ── Data rows via jagged-array import ─────────────────────────────
        // Syncfusion's ImportArray takes a 1D object[] (one row per call) +
        // a row index. Looping 1 call per row is still ~34x cheaper than
        // 34 cell-by-cell .Text assignments per row — ImportArray writes
        // the whole row's worth of cells in a single internal pass, skips
        // per-cell format detection, and reuses the same row range.
        if (rows.Count > 0)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                var v = rows[r];
                var row = new object[cols];
                row[ 0] = v.VehicleNo       ?? "";
                row[ 1] = v.ChassisNo       ?? "";
                row[ 2] = v.EngineNo        ?? "";
                row[ 3] = v.Model           ?? "";
                row[ 4] = v.AgreementNo     ?? "";
                row[ 5] = v.CustomerName    ?? "";
                row[ 6] = v.CustomerContact ?? "";
                row[ 7] = v.CustomerAddress ?? "";
                row[ 8] = v.Financer        ?? "";
                row[ 9] = v.BranchName      ?? "";
                row[10] = v.Bucket          ?? "";
                row[11] = v.Gv              ?? "";
                row[12] = v.Od              ?? "";
                row[13] = v.Seasoning       ?? "";
                row[14] = v.TbrFlag         ?? "";
                row[15] = v.Sec9            ?? "";
                row[16] = v.Sec17           ?? "";
                row[17] = v.Level1          ?? "";
                row[18] = v.Level1Contact   ?? "";
                row[19] = v.Level2          ?? "";
                row[20] = v.Level2Contact   ?? "";
                row[21] = v.Level3          ?? "";
                row[22] = v.Level3Contact   ?? "";
                row[23] = v.Level4          ?? "";
                row[24] = v.Level4Contact   ?? "";
                row[25] = v.SenderMail1     ?? "";
                row[26] = v.SenderMail2     ?? "";
                row[27] = v.ExecutiveName   ?? "";
                row[28] = v.Pos             ?? "";
                row[29] = v.Toss            ?? "";
                row[30] = v.Remark          ?? "";
                row[31] = v.Region          ?? "";
                row[32] = v.Area            ?? "";
                row[33] = v.CreatedOn       ?? "";
                // ImportArray(arr, firstRow, firstColumn, isVertical=false)
                // — horizontal import into a single row.
                ws.ImportArray(row, r + 2, 1, false);
            }
        }

        // Fixed column widths instead of AutoFitColumns — autofit would force
        // Syncfusion to scan every cell again, doubling the write time on a
        // 100k-row chunk. 18 chars is wide enough for vehicle numbers and
        // typical names; the user can widen further in Excel if they need.
        for (int c = 1; c <= cols; c++) ws.SetColumnWidth(c, 18);

        wb.SaveAs(filePath);
    }
}
