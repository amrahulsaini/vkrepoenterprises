using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Grid;
using Syncfusion.XlsIO;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Reports;

public partial class ReportsPage : Page
{
    private const int PdfRowCap = 50_000;

    // Path of the most-recently exported file — used by the Open File button.
    private string? _lastExportPath;

    // All export buttons, collected after InitializeComponent so we can bulk-disable/enable.
    private Button[]? _allExportButtons;

    public ReportsPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _allExportButtons = new[]
        {
            btnUsersExcel, btnUsersPdf,
            btnSubsExcel,  btnSubsPdf,
            btnVehicleExcel, btnVehiclePdf,
            btnRcExcel,    btnRcPdf,
            btnChassisExcel, btnChassisPdf
        };
    }

    // ── Export button dispatcher ─────────────────────────────────────────────

    private async void btnExport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = (btn.Tag as string) ?? "";
        var parts = tag.Split('_');
        if (parts.Length != 2) return;

        var report = parts[0];  // e.g. "Users", "VehicleRecords"
        var format = parts[1];  // "Excel" or "Pdf"

        bool isExcel = format == "Excel";

        // SaveFileDialog
        var dlg = new SaveFileDialog
        {
            Title = $"Save {report} Export",
            Filter = isExcel
                ? "Excel Workbook (*.xlsx)|*.xlsx"
                : "PDF Document (*.pdf)|*.pdf",
            FileName = $"{report}_{DateTime.Today:yyyy-MM-dd}" + (isExcel ? ".xlsx" : ".pdf")
        };
        if (dlg.ShowDialog() != true) return;

        var filePath = dlg.FileName;

        BeginExport(report, isExcel);

        try
        {
            if (isExcel)
                await RunExcelExportAsync(report, filePath);
            else
                await RunPdfExportAsync(report, filePath);

            _lastExportPath = filePath;
            FinishExport(success: true);
        }
        catch (Exception ex)
        {
            FinishExport(success: false);
            Log($"ERROR: {ex.Message}");
            MessageBox.Show($"Export failed:\n{ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── UI state helpers ────────────────────────────────────────────────────

    private void BeginExport(string reportName, bool isExcel)
    {
        panelIdle.Visibility    = Visibility.Collapsed;
        panelActive.Visibility  = Visibility.Visible;
        panelComplete.Visibility = Visibility.Collapsed;

        lblExportName.Text = $"{reportName}  •  {(isExcel ? "Excel" : "PDF")}";
        lblProgress.Text   = "Starting…";
        pbExport.Value     = 0;
        txtLog.Clear();
        _lastExportPath    = null;

        if (_allExportButtons != null)
            foreach (var b in _allExportButtons)
                b.IsEnabled = false;
    }

    private void FinishExport(bool success)
    {
        pbExport.Value    = 100;
        lblProgress.Text  = success ? "Done" : "Failed";
        panelComplete.Visibility = success ? Visibility.Visible : Visibility.Collapsed;

        if (_allExportButtons != null)
            foreach (var b in _allExportButtons)
                b.IsEnabled = true;
    }

    private void SetProgress(double pct, string message)
    {
        pbExport.Value   = pct;
        lblProgress.Text = message;
    }

    private void Log(string line)
    {
        txtLog.AppendText(line + Environment.NewLine);
        txtLog.ScrollToEnd();
    }

    // ── Open File handler ────────────────────────────────────────────────────

    private void btnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_lastExportPath == null || !File.Exists(_lastExportPath)) return;
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_lastExportPath}\"");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EXCEL EXPORT
    // ═══════════════════════════════════════════════════════════════════════

    private async Task RunExcelExportAsync(string report, string filePath)
    {
        switch (report)
        {
            case "Users":            await ExcelUsersAsync(filePath);    break;
            case "Subscriptions":    await ExcelSubsAsync(filePath);     break;
            case "VehicleRecords":   await ExcelVehicleAsync(filePath, "VehicleRecords", DesktopApiClient.ExportVehicleRecordsPageAsync); break;
            case "RcRecords":        await ExcelVehicleAsync(filePath, "RC Records",     DesktopApiClient.ExportRcRecordsPageAsync);      break;
            case "ChassisRecords":   await ExcelVehicleAsync(filePath, "Chassis Records",DesktopApiClient.ExportChassisRecordsPageAsync); break;
        }
    }

    private async Task ExcelUsersAsync(string filePath)
    {
        Log("Fetching users from server…");
        SetProgress(5, "Fetching…");
        var rows = await DesktopApiClient.ExportUsersAsync();
        Log($"Fetched {rows.Count:N0} users.");

        SetProgress(80, "Writing Excel file…");
        await Task.Run(() =>
        {
            string[] headers = { "ID","Name","Mobile","Address","Pincode","Active","Admin","Stopped","Blacklisted","Balance","Created At","Sub End" };
            using var engine = new ExcelEngine();
            var xlApp = engine.Excel;
            xlApp.DefaultVersion = ExcelVersion.Xlsx;
            var wb = xlApp.Workbooks.Create(1);
            var ws = wb.Worksheets[0];
            ws.Name = "Users";

            WriteExcelHeader(ws, headers);

            int r = 2;
            foreach (var u in rows)
            {
                ws[r, 1].Value2  = u.Id;
                ws[r, 2].Text    = u.Name;
                ws[r, 3].Text    = u.Mobile;
                ws[r, 4].Text    = u.Address ?? "";
                ws[r, 5].Text    = u.Pincode ?? "";
                ws[r, 6].Text    = u.IsActive      ? "Yes" : "No";
                ws[r, 7].Text    = u.IsAdmin       ? "Yes" : "No";
                ws[r, 8].Text    = u.IsStopped     ? "Yes" : "No";
                ws[r, 9].Text    = u.IsBlacklisted ? "Yes" : "No";
                ws[r, 10].Value2 = (double)u.Balance;
                ws[r, 11].Text   = u.CreatedAt;
                ws[r, 12].Text   = u.SubEnd ?? "";
                r++;
            }

            ws.UsedRange.AutofitColumns();
            wb.SaveAs(filePath);
        });
        Log("Excel file written.");
    }

    private async Task ExcelSubsAsync(string filePath)
    {
        Log("Fetching subscriptions from server…");
        SetProgress(5, "Fetching…");
        var rows = await DesktopApiClient.ExportSubscriptionsAsync();
        Log($"Fetched {rows.Count:N0} subscriptions.");

        SetProgress(80, "Writing Excel file…");
        await Task.Run(() =>
        {
            string[] headers = { "ID","User ID","User Name","Mobile","Start Date","End Date","Amount","Notes","Created At" };
            using var engine = new ExcelEngine();
            var xlApp = engine.Excel;
            xlApp.DefaultVersion = ExcelVersion.Xlsx;
            var wb = xlApp.Workbooks.Create(1);
            var ws = wb.Worksheets[0];
            ws.Name = "Subscriptions";

            WriteExcelHeader(ws, headers);

            int r = 2;
            foreach (var s in rows)
            {
                ws[r, 1].Value2  = s.Id;
                ws[r, 2].Value2  = s.UserId;
                ws[r, 3].Text    = s.UserName;
                ws[r, 4].Text    = s.UserMobile;
                ws[r, 5].Text    = s.StartDate;
                ws[r, 6].Text    = s.EndDate;
                ws[r, 7].Value2  = (double)s.Amount;
                ws[r, 8].Text    = s.Notes ?? "";
                ws[r, 9].Text    = s.CreatedAt;
                r++;
            }

            ws.UsedRange.AutofitColumns();
            wb.SaveAs(filePath);
        });
        Log("Excel file written.");
    }

    private async Task ExcelVehicleAsync(
        string filePath,
        string sheetName,
        Func<int, int, Task<DesktopApiClient.ExportPage<DesktopApiClient.ExportVehicleRow>>> fetchPage)
    {
        string[] headers =
        {
            "Vehicle No","Chassis No","Engine No","Model","Agreement No",
            "Customer Name","Customer Contact","Customer Address",
            "Financer","Branch Name",
            "Bucket","GV","OD","Seasoning","TBR Flag","Sec9","Sec17",
            "Level1","Level1 Contact","Level2","Level2 Contact",
            "Level3","Level3 Contact","Level4","Level4 Contact",
            "Sender Mail1","Sender Mail2","Executive Name",
            "POS","TOSS","Remark","Region","Area","Created On"
        };

        // Phase 1 — fetch all pages
        Log("Fetching page 1…");
        var firstPage = await fetchPage(0, 5000);
        long total     = firstPage.Total;
        int  pageSize  = firstPage.Size > 0 ? firstPage.Size : 5000;
        int  totalPages = (int)Math.Ceiling((double)total / pageSize);

        Log($"Total rows: {total:N0}  •  {totalPages} page(s) of {pageSize:N0}");
        SetProgress(5, $"Fetching page 1 of {totalPages}…");

        var allRows = new List<DesktopApiClient.ExportVehicleRow>(firstPage.Rows);

        for (int pg = 1; pg < totalPages; pg++)
        {
            double pct = (double)pg / totalPages * 80.0;
            SetProgress(pct, $"Fetching page {pg + 1} of {totalPages}…");
            Log($"Fetching page {pg + 1} of {totalPages}…");
            var page = await fetchPage(pg, 5000);
            allRows.AddRange(page.Rows);
        }

        Log($"All {allRows.Count:N0} rows fetched. Writing Excel…");
        SetProgress(80, "Writing Excel file…");

        await Task.Run(() =>
        {
            using var engine = new ExcelEngine();
            var xlApp = engine.Excel;
            xlApp.DefaultVersion = ExcelVersion.Xlsx;
            var wb = xlApp.Workbooks.Create(1);
            var ws = wb.Worksheets[0];
            ws.Name = sheetName.Length > 31 ? sheetName[..31] : sheetName;

            WriteExcelHeader(ws, headers);

            int ri = 2;
            foreach (var v in allRows)
            {
                ws[ri,  1].Text = v.VehicleNo;
                ws[ri,  2].Text = v.ChassisNo;
                ws[ri,  3].Text = v.EngineNo;
                ws[ri,  4].Text = v.Model;
                ws[ri,  5].Text = v.AgreementNo;
                ws[ri,  6].Text = v.CustomerName;
                ws[ri,  7].Text = v.CustomerContact;
                ws[ri,  8].Text = v.CustomerAddress;
                ws[ri,  9].Text = v.Financer;
                ws[ri, 10].Text = v.BranchName;
                ws[ri, 11].Text = v.Bucket;
                ws[ri, 12].Text = v.Gv;
                ws[ri, 13].Text = v.Od;
                ws[ri, 14].Text = v.Seasoning;
                ws[ri, 15].Text = v.TbrFlag;
                ws[ri, 16].Text = v.Sec9;
                ws[ri, 17].Text = v.Sec17;
                ws[ri, 18].Text = v.Level1;
                ws[ri, 19].Text = v.Level1Contact;
                ws[ri, 20].Text = v.Level2;
                ws[ri, 21].Text = v.Level2Contact;
                ws[ri, 22].Text = v.Level3;
                ws[ri, 23].Text = v.Level3Contact;
                ws[ri, 24].Text = v.Level4;
                ws[ri, 25].Text = v.Level4Contact;
                ws[ri, 26].Text = v.SenderMail1;
                ws[ri, 27].Text = v.SenderMail2;
                ws[ri, 28].Text = v.ExecutiveName;
                ws[ri, 29].Text = v.Pos;
                ws[ri, 30].Text = v.Toss;
                ws[ri, 31].Text = v.Remark;
                ws[ri, 32].Text = v.Region;
                ws[ri, 33].Text = v.Area;
                ws[ri, 34].Text = v.CreatedOn;
                ri++;
            }

            ws.UsedRange.AutofitColumns();
            wb.SaveAs(filePath);
        });

        SetProgress(100, "Done");
        Log("Excel file written successfully.");
    }

    // Shared helper: write a bold dark-background header row
    private static void WriteExcelHeader(IWorksheet ws, string[] headers)
    {
        int colCount = headers.Length;
        for (int c = 0; c < colCount; c++)
        {
            ws[1, c + 1].Text                    = headers[c];
            ws[1, c + 1].CellStyle.Font.Bold     = true;
            ws[1, c + 1].CellStyle.Color         = System.Drawing.Color.FromArgb(26, 26, 26);
            ws[1, c + 1].CellStyle.Font.RGBColor = System.Drawing.Color.White;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PDF EXPORT
    // ═══════════════════════════════════════════════════════════════════════

    private async Task RunPdfExportAsync(string report, string filePath)
    {
        switch (report)
        {
            case "Users":          await PdfUsersAsync(filePath);    break;
            case "Subscriptions":  await PdfSubsAsync(filePath);     break;
            case "VehicleRecords": await PdfVehicleAsync(filePath, "Vehicle Records", DesktopApiClient.ExportVehicleRecordsPageAsync); break;
            case "RcRecords":      await PdfVehicleAsync(filePath, "RC Records",      DesktopApiClient.ExportRcRecordsPageAsync);      break;
            case "ChassisRecords": await PdfVehicleAsync(filePath, "Chassis Records", DesktopApiClient.ExportChassisRecordsPageAsync); break;
        }
    }

    private async Task PdfUsersAsync(string filePath)
    {
        Log("Fetching users from server…");
        SetProgress(5, "Fetching…");
        var rows = await DesktopApiClient.ExportUsersAsync();
        Log($"Fetched {rows.Count:N0} users.");

        bool capped = rows.Count > PdfRowCap;
        if (capped)
        {
            MessageBox.Show(
                $"There are {rows.Count:N0} user rows. PDF export is capped at {PdfRowCap:N0} rows. The file will contain only the first {PdfRowCap:N0}.",
                "PDF Row Cap", MessageBoxButton.OK, MessageBoxImage.Warning);
            rows = rows.GetRange(0, PdfRowCap);
        }

        SetProgress(80, "Writing PDF…");
        await Task.Run(() =>
        {
            string[] pdfHeaders = { "ID","Name","Mobile","Address","Active","Admin","Stopped","Blacklisted","Balance","Sub End" };
            using var doc = new PdfDocument();
            doc.PageSettings.Orientation = PdfPageOrientation.Landscape;
            doc.PageSettings.Margins.All = 18;

            var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
            var dataFont  = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);
            var hdrFont   = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f, PdfFontStyle.Bold);

            var firstPage = doc.Pages.Add();
            string title = $"VK Enterprises — Users  ({DateTime.Today:dd MMM yyyy})  •  {rows.Count:N0} records" +
                           (capped ? $"  [capped at {PdfRowCap:N0}]" : "");
            firstPage.Graphics.DrawString(title, titleFont, PdfBrushes.Black,
                new Syncfusion.Drawing.PointF(0, 0));

            var grid = new PdfGrid();
            grid.Style.Font = dataFont;
            grid.Columns.Add(pdfHeaders.Length);

            var headerRow = grid.Headers.Add(1)[0];
            for (int c = 0; c < pdfHeaders.Length; c++)
            {
                headerRow.Cells[c].Value                 = pdfHeaders[c];
                headerRow.Cells[c].Style.Font            = hdrFont;
                headerRow.Cells[c].Style.BackgroundBrush = new PdfSolidBrush(
                    Syncfusion.Drawing.Color.FromArgb(255, 26, 26, 26));
                headerRow.Cells[c].Style.TextBrush       = PdfBrushes.White;
            }

            foreach (var u in rows)
            {
                var row = grid.Rows.Add();
                row.Cells[0].Value = u.Id.ToString();
                row.Cells[1].Value = u.Name;
                row.Cells[2].Value = u.Mobile;
                row.Cells[3].Value = u.Address ?? "";
                row.Cells[4].Value = u.IsActive      ? "Y" : "N";
                row.Cells[5].Value = u.IsAdmin       ? "Y" : "N";
                row.Cells[6].Value = u.IsStopped     ? "Y" : "N";
                row.Cells[7].Value = u.IsBlacklisted ? "Y" : "N";
                row.Cells[8].Value = u.Balance.ToString("N2");
                row.Cells[9].Value = u.SubEnd ?? "";
            }

            var clientSize = firstPage.GetClientSize();
            var layoutFmt  = new PdfLayoutFormat { Layout = PdfLayoutType.Paginate };
            grid.Draw(firstPage,
                new Syncfusion.Drawing.RectangleF(0, 18, clientSize.Width, clientSize.Height - 18),
                layoutFmt);

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            doc.Save(fs);
        });
        Log("PDF written successfully.");
    }

    private async Task PdfSubsAsync(string filePath)
    {
        Log("Fetching subscriptions from server…");
        SetProgress(5, "Fetching…");
        var rows = await DesktopApiClient.ExportSubscriptionsAsync();
        Log($"Fetched {rows.Count:N0} subscriptions.");

        bool capped = rows.Count > PdfRowCap;
        if (capped)
        {
            MessageBox.Show(
                $"There are {rows.Count:N0} subscription rows. PDF export is capped at {PdfRowCap:N0} rows.",
                "PDF Row Cap", MessageBoxButton.OK, MessageBoxImage.Warning);
            rows = rows.GetRange(0, PdfRowCap);
        }

        SetProgress(80, "Writing PDF…");
        await Task.Run(() =>
        {
            string[] pdfHeaders = { "ID","User","Mobile","Start","End","Amount","Notes" };
            using var doc = new PdfDocument();
            doc.PageSettings.Orientation = PdfPageOrientation.Landscape;
            doc.PageSettings.Margins.All = 18;

            var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
            var dataFont  = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);
            var hdrFont   = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f, PdfFontStyle.Bold);

            var firstPage = doc.Pages.Add();
            string title = $"VK Enterprises — Subscriptions  ({DateTime.Today:dd MMM yyyy})  •  {rows.Count:N0} records" +
                           (capped ? $"  [capped at {PdfRowCap:N0}]" : "");
            firstPage.Graphics.DrawString(title, titleFont, PdfBrushes.Black,
                new Syncfusion.Drawing.PointF(0, 0));

            var grid = new PdfGrid();
            grid.Style.Font = dataFont;
            grid.Columns.Add(pdfHeaders.Length);

            var headerRow = grid.Headers.Add(1)[0];
            for (int c = 0; c < pdfHeaders.Length; c++)
            {
                headerRow.Cells[c].Value                 = pdfHeaders[c];
                headerRow.Cells[c].Style.Font            = hdrFont;
                headerRow.Cells[c].Style.BackgroundBrush = new PdfSolidBrush(
                    Syncfusion.Drawing.Color.FromArgb(255, 26, 26, 26));
                headerRow.Cells[c].Style.TextBrush       = PdfBrushes.White;
            }

            foreach (var s in rows)
            {
                var row = grid.Rows.Add();
                row.Cells[0].Value = s.Id.ToString();
                row.Cells[1].Value = s.UserName;
                row.Cells[2].Value = s.UserMobile;
                row.Cells[3].Value = s.StartDate;
                row.Cells[4].Value = s.EndDate;
                row.Cells[5].Value = s.Amount.ToString("N2");
                row.Cells[6].Value = s.Notes ?? "";
            }

            var clientSize = firstPage.GetClientSize();
            var layoutFmt  = new PdfLayoutFormat { Layout = PdfLayoutType.Paginate };
            grid.Draw(firstPage,
                new Syncfusion.Drawing.RectangleF(0, 18, clientSize.Width, clientSize.Height - 18),
                layoutFmt);

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            doc.Save(fs);
        });
        Log("PDF written successfully.");
    }

    private async Task PdfVehicleAsync(
        string filePath,
        string reportTitle,
        Func<int, int, Task<DesktopApiClient.ExportPage<DesktopApiClient.ExportVehicleRow>>> fetchPage)
    {
        // Phase 1 — fetch pages (0–80%)
        Log("Fetching page 1…");
        var firstPage = await fetchPage(0, 5000);
        long total      = firstPage.Total;
        int  pageSize   = firstPage.Size > 0 ? firstPage.Size : 5000;
        int  totalPages = (int)Math.Ceiling((double)total / pageSize);

        Log($"Total rows: {total:N0}  •  {totalPages} page(s)");
        SetProgress(5, $"Fetching page 1 of {totalPages}…");

        var allRows = new List<DesktopApiClient.ExportVehicleRow>(firstPage.Rows);

        for (int pg = 1; pg < totalPages; pg++)
        {
            double pct = (double)pg / totalPages * 80.0;
            SetProgress(pct, $"Fetching page {pg + 1} of {totalPages}…");
            Log($"Fetching page {pg + 1} of {totalPages}…");
            var page = await fetchPage(pg, 5000);
            allRows.AddRange(page.Rows);
        }

        bool capped = allRows.Count > PdfRowCap;
        if (capped)
        {
            MessageBox.Show(
                $"There are {allRows.Count:N0} rows. PDF export is capped at {PdfRowCap:N0} rows.",
                "PDF Row Cap", MessageBoxButton.OK, MessageBoxImage.Warning);
            allRows = allRows.GetRange(0, PdfRowCap);
        }

        Log($"Writing PDF for {allRows.Count:N0} rows…");
        SetProgress(80, "Writing PDF…");

        await Task.Run(() =>
        {
            string[] pdfHeaders =
            {
                "VRN","Chassis","Engine","Model","Agr No",
                "Customer","Contact","Address",
                "Financer","Branch",
                "Bucket","GV","OD","Season","TBR","Sec9","Sec17",
                "L1","L1 Cont","L2","L2 Cont","L3","L3 Cont","L4","L4 Cont",
                "Mail1","Mail2","Exec","POS","TOSS","Remark","Region","Area","Created"
            };

            using var doc = new PdfDocument();
            doc.PageSettings.Orientation = PdfPageOrientation.Landscape;
            doc.PageSettings.Margins.All = 10;

            var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8, PdfFontStyle.Bold);
            var dataFont  = new PdfStandardFont(PdfFontFamily.Helvetica, 5.5f);
            var hdrFont   = new PdfStandardFont(PdfFontFamily.Helvetica, 5.5f, PdfFontStyle.Bold);

            var firstDocPage = doc.Pages.Add();
            string titleText = $"VK Enterprises — {reportTitle}  ({DateTime.Today:dd MMM yyyy})  •  {allRows.Count:N0} records" +
                               (capped ? $"  [capped at {PdfRowCap:N0}]" : "");
            firstDocPage.Graphics.DrawString(titleText, titleFont, PdfBrushes.Black,
                new Syncfusion.Drawing.PointF(0, 0));

            var grid = new PdfGrid();
            grid.Style.Font = dataFont;
            grid.Columns.Add(pdfHeaders.Length);

            var headerRow = grid.Headers.Add(1)[0];
            for (int c = 0; c < pdfHeaders.Length; c++)
            {
                headerRow.Cells[c].Value                 = pdfHeaders[c];
                headerRow.Cells[c].Style.Font            = hdrFont;
                headerRow.Cells[c].Style.BackgroundBrush = new PdfSolidBrush(
                    Syncfusion.Drawing.Color.FromArgb(255, 26, 26, 26));
                headerRow.Cells[c].Style.TextBrush       = PdfBrushes.White;
            }

            foreach (var v in allRows)
            {
                var row = grid.Rows.Add();
                row.Cells[0].Value  = v.VehicleNo;
                row.Cells[1].Value  = v.ChassisNo;
                row.Cells[2].Value  = v.EngineNo;
                row.Cells[3].Value  = v.Model;
                row.Cells[4].Value  = v.AgreementNo;
                row.Cells[5].Value  = v.CustomerName;
                row.Cells[6].Value  = v.CustomerContact;
                row.Cells[7].Value  = v.CustomerAddress;
                row.Cells[8].Value  = v.Financer;
                row.Cells[9].Value  = v.BranchName;
                row.Cells[10].Value = v.Bucket;
                row.Cells[11].Value = v.Gv;
                row.Cells[12].Value = v.Od;
                row.Cells[13].Value = v.Seasoning;
                row.Cells[14].Value = v.TbrFlag;
                row.Cells[15].Value = v.Sec9;
                row.Cells[16].Value = v.Sec17;
                row.Cells[17].Value = v.Level1;
                row.Cells[18].Value = v.Level1Contact;
                row.Cells[19].Value = v.Level2;
                row.Cells[20].Value = v.Level2Contact;
                row.Cells[21].Value = v.Level3;
                row.Cells[22].Value = v.Level3Contact;
                row.Cells[23].Value = v.Level4;
                row.Cells[24].Value = v.Level4Contact;
                row.Cells[25].Value = v.SenderMail1;
                row.Cells[26].Value = v.SenderMail2;
                row.Cells[27].Value = v.ExecutiveName;
                row.Cells[28].Value = v.Pos;
                row.Cells[29].Value = v.Toss;
                row.Cells[30].Value = v.Remark;
                row.Cells[31].Value = v.Region;
                row.Cells[32].Value = v.Area;
                row.Cells[33].Value = v.CreatedOn;
            }

            var clientSize = firstDocPage.GetClientSize();
            var layoutFmt  = new PdfLayoutFormat { Layout = PdfLayoutType.Paginate };
            grid.Draw(firstDocPage,
                new Syncfusion.Drawing.RectangleF(0, 14, clientSize.Width, clientSize.Height - 14),
                layoutFmt);

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            doc.Save(fs);
        });

        SetProgress(100, "Done");
        Log("PDF written successfully.");
    }
}
