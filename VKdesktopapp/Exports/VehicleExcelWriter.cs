using System.Collections.Generic;
using Syncfusion.XlsIO;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Exports;

// Shared Excel writer for vehicle rows. Identical column order whether the
// caller is FinancesManagerPage (Finance/Branch) or ReportsPage (vehicle/RC/
// chassis records) so downstream consumers get one stable schema. Used by
// ChunkedExportDialog to write each chunk to its own .xlsx file.
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

        for (int c = 0; c < Headers.Length; c++)
        {
            var cell = ws[1, c + 1];
            cell.Text = Headers[c];
            cell.CellStyle.Font.Bold = true;
            cell.CellStyle.Color     = System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1A);
            cell.CellStyle.Font.Color = ExcelKnownColors.White;
        }

        for (int r = 0; r < rows.Count; r++)
        {
            var v = rows[r];
            var vals = new[]
            {
                v.VehicleNo, v.ChassisNo, v.EngineNo, v.Model, v.AgreementNo,
                v.CustomerName, v.CustomerContact, v.CustomerAddress,
                v.Financer, v.BranchName, v.Bucket, v.Gv, v.Od, v.Seasoning,
                v.TbrFlag, v.Sec9, v.Sec17,
                v.Level1, v.Level1Contact, v.Level2, v.Level2Contact,
                v.Level3, v.Level3Contact, v.Level4, v.Level4Contact,
                v.SenderMail1, v.SenderMail2, v.ExecutiveName,
                v.Pos, v.Toss, v.Remark, v.Region, v.Area, v.CreatedOn
            };
            for (int c = 0; c < vals.Length; c++)
                ws[r + 2, c + 1].Text = vals[c] ?? "";
        }

        ws.UsedRange.AutofitColumns();
        wb.SaveAs(filePath);
    }
}
