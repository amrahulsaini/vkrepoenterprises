using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Grid;
using Syncfusion.XlsIO;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Records;

public partial class DetailsViewsPage : Page
{
    private List<DesktopApiClient.SearchLogRow> _allLogs = new();
    private readonly ObservableCollection<DesktopApiClient.SearchLogRow> _displayed = new();
    private bool _filterSuspended;
    private long? _pickedUserId;

    public DetailsViewsPage()
    {
        InitializeComponent();
        dgLogs.ItemsSource = _displayed;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _filterSuspended = true;
        dpFrom.SelectedDate = DateTime.Today;
        dpTo.SelectedDate   = DateTime.Today;
        _filterSuspended    = false;
        await RefreshLogsAsync();
    }

    private async void btnRefresh_Click(object sender, RoutedEventArgs e)
        => await RefreshLogsAsync();

    private async Task RefreshLogsAsync()
    {
        SetLoading(true);
        try
        {
            var from = dpFrom.SelectedDate?.ToString("yyyy-MM-dd");
            var to   = dpTo.SelectedDate?.ToString("yyyy-MM-dd");
            _allLogs = await DesktopApiClient.GetSearchLogsAsync(from, to);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load logs:\n{ex.Message}", "Search Logs",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetLoading(false); }
    }

    private void SetLoading(bool on)
    {
        icnLoading.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        lblCount.Text         = on ? "Loading…" : "";
    }

    private void ApplyFilter()
    {
        var search   = txtSearch.Text.Trim();
        var filtered = _allLogs.AsEnumerable();

        if (_pickedUserId.HasValue)
            filtered = filtered.Where(r => r.UserId == _pickedUserId);

        if (!string.IsNullOrEmpty(search))
            filtered = filtered.Where(r =>
                r.VehicleNo.Contains(search, StringComparison.OrdinalIgnoreCase)    ||
                r.ChassisNo.Contains(search, StringComparison.OrdinalIgnoreCase)    ||
                r.UserName.Contains(search, StringComparison.OrdinalIgnoreCase)     ||
                r.UserMobile.Contains(search, StringComparison.OrdinalIgnoreCase)   ||
                (r.Address     ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (r.UserAddress ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));

        _displayed.Clear();
        foreach (var row in filtered) _displayed.Add(row);
        lblCount.Text = $"{_displayed.Count:N0} records";
        UpdateMapButton();
    }

    // ── Filters ─────────────────────────────────────────────────────────────

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filterSuspended) return;
        _ = RefreshLogsAsync();
    }

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter();

    private void btnPickUser_Click(object sender, RoutedEventArgs e)
    {
        var win = new UserPickerWindow { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            _pickedUserId      = win.SelectedUserId;
            txtPickedUser.Text = win.SelectedUserName.Length > 0
                ? win.SelectedUserName : "All Agents";
            ApplyFilter();
        }
    }

    // ── Grid interactions ────────────────────────────────────────────────────

    private void dgLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateMapButton();

    private void dgLogs_MouseDoubleClick(object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = dgLogs.SelectedItem as DesktopApiClient.SearchLogRow;
        if (row?.Lat.HasValue == true) OpenMapWindow(row);
    }

    private void UpdateMapButton()
    {
        var row = dgLogs.SelectedItem as DesktopApiClient.SearchLogRow;
        btnShowMap.IsEnabled = row?.Lat.HasValue == true && row.Lng.HasValue == true;
    }

    private void btnShowMap_Click(object sender, RoutedEventArgs e)
    {
        var row = dgLogs.SelectedItem as DesktopApiClient.SearchLogRow;
        if (row?.Lat.HasValue == true) OpenMapWindow(row);
    }

    private void OpenMapWindow(DesktopApiClient.SearchLogRow row)
    {
        var win = new MapPointWindow(
            row.VehicleNo, row.ChassisNo, row.Model,
            row.UserName, row.UserMobile, row.ServerTime,
            row.Lat, row.Lng, row.Address)
        { Owner = Window.GetWindow(this) };
        win.Show();
    }

    // ── Shared export helpers ────────────────────────────────────────────────

    private void SetExportBusy(bool busy, string? status = null)
    {
        btnExportExcel.IsEnabled = !busy;
        btnExportPdf.IsEnabled   = !busy;
        icnLoading.Visibility    = busy ? Visibility.Visible : Visibility.Collapsed;
        lblCount.Text            = busy ? (status ?? "Working…") : $"{_displayed.Count:N0} records";
    }

    private static readonly string[] ExportHeaders =
        { "VRN", "Chassis", "Model", "Agent", "Mobile",
          "Agent Address", "Search Address",
          "Latitude", "Longitude", "Device Time", "Server Time" };

    private async Task<List<DesktopApiClient.SearchLogRow>?> FetchExportRowsAsync()
    {
        var from   = dpFrom.SelectedDate?.ToString("yyyy-MM-dd");
        var to     = dpTo.SelectedDate?.ToString("yyyy-MM-dd");
        var search = txtSearch.Text.Trim();

        SetExportBusy(true, "Fetching all records from server…");
        var rows = await DesktopApiClient.GetSearchLogsAsync(from, to, _pickedUserId, export: true);

        if (!string.IsNullOrEmpty(search))
            rows = rows.Where(r =>
                r.VehicleNo.Contains(search, StringComparison.OrdinalIgnoreCase)    ||
                r.ChassisNo.Contains(search, StringComparison.OrdinalIgnoreCase)    ||
                r.UserName.Contains(search, StringComparison.OrdinalIgnoreCase)     ||
                r.UserMobile.Contains(search, StringComparison.OrdinalIgnoreCase)   ||
                (r.Address     ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (r.UserAddress ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        return rows;
    }

    private static void FillRow(DesktopApiClient.SearchLogRow r, Action<int, string> set)
    {
        set(0,  r.VehicleNo);
        set(1,  r.ChassisNo);
        set(2,  r.Model);
        set(3,  r.UserName);
        set(4,  r.UserMobile);
        set(5,  r.UserAddress ?? "");
        set(6,  r.Address ?? "");
        set(7,  r.Lat?.ToString("F5") ?? "");
        set(8,  r.Lng?.ToString("F5") ?? "");
        set(9,  r.DeviceTime);
        set(10, r.ServerTime);
    }

    // ── Export Excel ─────────────────────────────────────────────────────────

    private async void btnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter   = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"SearchLogs_{DateTime.Today:yyyyMMdd}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        List<DesktopApiClient.SearchLogRow> rows;
        try   { rows = await FetchExportRowsAsync() ?? new(); }
        catch (Exception ex)
        {
            SetExportBusy(false);
            MessageBox.Show($"Failed to fetch export data:\n{ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (rows.Count == 0)
        {
            SetExportBusy(false);
            MessageBox.Show("No records match the current filters.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetExportBusy(true, $"Writing {rows.Count:N0} records to Excel…");
        try
        {
            var filePath = dlg.FileName;
            await Task.Run(() =>
            {
                using var engine = new ExcelEngine();
                var app = engine.Excel;
                app.DefaultVersion = ExcelVersion.Xlsx;
                var wb = app.Workbooks.Create(1);
                var ws = wb.Worksheets[0];
                ws.Name = "Search Logs";

                for (int c = 0; c < ExportHeaders.Length; c++)
                {
                    ws[1, c + 1].Text                     = ExportHeaders[c];
                    ws[1, c + 1].CellStyle.Font.Bold      = true;
                    ws[1, c + 1].CellStyle.Color          = System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1A);
                    ws[1, c + 1].CellStyle.Font.RGBColor  = System.Drawing.Color.White;
                }

                int rowIdx = 2;
                foreach (var r in rows)
                {
                    FillRow(r, (col, val) => ws[rowIdx, col + 1].Text = val);
                    rowIdx++;
                }

                ws.UsedRange.AutofitColumns();
                wb.SaveAs(filePath);
            });

            MessageBox.Show($"Exported {rows.Count:N0} records successfully.", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Excel export failed:\n{ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetExportBusy(false); }
    }

    // ── Export PDF ───────────────────────────────────────────────────────────

    private async void btnExportPdf_Click(object sender, RoutedEventArgs e)
    {
        const int PdfRowCap = 50_000;

        var dlg = new SaveFileDialog
        {
            Filter   = "PDF Document (*.pdf)|*.pdf",
            FileName = $"SearchLogs_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        List<DesktopApiClient.SearchLogRow> rows;
        try   { rows = await FetchExportRowsAsync() ?? new(); }
        catch (Exception ex)
        {
            SetExportBusy(false);
            MessageBox.Show($"Failed to fetch export data:\n{ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (rows.Count == 0)
        {
            SetExportBusy(false);
            MessageBox.Show("No records match the current filters.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool capped = rows.Count > PdfRowCap;
        if (capped)
        {
            var ans = MessageBox.Show(
                $"PDF is limited to {PdfRowCap:N0} rows to keep file size manageable.\n" +
                $"Total matching records: {rows.Count:N0}.\n\n" +
                $"The latest {PdfRowCap:N0} rows will be included.\n" +
                $"Use Excel export to get the full dataset.\n\nContinue with PDF?",
                "Large Export Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ans != MessageBoxResult.Yes) { SetExportBusy(false); return; }
            rows = rows.Take(PdfRowCap).ToList();
        }

        SetExportBusy(true, $"Writing {rows.Count:N0} records to PDF…");
        try
        {
            var filePath = dlg.FileName;
            var snapshot = rows;
            await Task.Run(() =>
            {
                using var doc = new PdfDocument();
                doc.PageSettings.Orientation = PdfPageOrientation.Landscape;
                doc.PageSettings.Margins.All = 18;

                var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
                var dataFont  = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);
                var hdrFont   = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f, PdfFontStyle.Bold);

                var firstPage = doc.Pages.Add();
                var title = $"VK Enterprises — Search Logs  ({DateTime.Today:dd MMM yyyy})  •  {snapshot.Count:N0} records" +
                            (capped ? $"  [showing latest {PdfRowCap:N0}]" : "");
                firstPage.Graphics.DrawString(title, titleFont, PdfBrushes.Black,
                    new Syncfusion.Drawing.PointF(0, 0));

                var grid = new PdfGrid();
                grid.Style.Font = dataFont;
                grid.Columns.Add(11);

                var headerRow = grid.Headers.Add(1)[0];
                string[] pdfHeaders = { "VRN","Chassis","Model","Agent","Mobile","Agent Addr","Search Addr","Lat","Lng","Device Time","Server Time" };
                for (int c = 0; c < pdfHeaders.Length; c++)
                {
                    headerRow.Cells[c].Value                 = pdfHeaders[c];
                    headerRow.Cells[c].Style.Font            = hdrFont;
                    headerRow.Cells[c].Style.BackgroundBrush = new PdfSolidBrush(
                        Syncfusion.Drawing.Color.FromArgb(255, 26, 26, 26));
                    headerRow.Cells[c].Style.TextBrush       = PdfBrushes.White;
                }

                foreach (var r in snapshot)
                {
                    var pRow = grid.Rows.Add();
                    FillRow(r, (col, val) => pRow.Cells[col].Value = val);
                    // PDF uses 4 decimal places for coords
                    pRow.Cells[7].Value = r.Lat?.ToString("F4") ?? "";
                    pRow.Cells[8].Value = r.Lng?.ToString("F4") ?? "";
                }

                var clientSize = firstPage.GetClientSize();
                var layoutFmt  = new PdfLayoutFormat { Layout = PdfLayoutType.Paginate };
                grid.Draw(firstPage,
                    new Syncfusion.Drawing.RectangleF(0, 18, clientSize.Width, clientSize.Height - 18),
                    layoutFmt);

                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                doc.Save(fs);
            });

            MessageBox.Show($"Exported {rows.Count:N0} records successfully.", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF export failed:\n{ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetExportBusy(false); }
    }
}
