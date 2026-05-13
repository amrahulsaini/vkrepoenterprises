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
        var search  = txtSearch.Text.Trim();
        var filtered = _allLogs.AsEnumerable();

        if (_pickedUserId.HasValue)
            filtered = filtered.Where(r => r.UserId == _pickedUserId);

        if (!string.IsNullOrEmpty(search))
            filtered = filtered.Where(r =>
                r.VehicleNo.Contains(search, StringComparison.OrdinalIgnoreCase)  ||
                r.ChassisNo.Contains(search, StringComparison.OrdinalIgnoreCase)  ||
                r.UserName.Contains(search, StringComparison.OrdinalIgnoreCase)   ||
                r.UserMobile.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (r.Address ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));

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
            _pickedUserId        = win.SelectedUserId;
            txtPickedUser.Text   = win.SelectedUserName.Length > 0
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

    // ── Export Excel ─────────────────────────────────────────────────────────
    private void btnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_displayed.Count == 0) { MessageBox.Show("No records to export."); return; }

        var dlg = new SaveFileDialog
        {
            Filter   = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"SearchLogs_{DateTime.Today:yyyyMMdd}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var engine = new ExcelEngine();
            var app  = engine.Excel;
            app.DefaultVersion = ExcelVersion.Xlsx;
            var wb   = app.Workbooks.Create(1);
            var ws   = wb.Worksheets[0];
            ws.Name  = "Search Logs";

            string[] headers = { "VRN","Chassis","Model","Agent","Mobile","Agent Address","Search Address","Latitude","Longitude","Device Time","Server Time" };
            for (int c = 0; c < headers.Length; c++)
            {
                ws[1, c + 1].Text = headers[c];
                ws[1, c + 1].CellStyle.Font.Bold = true;
                ws[1, c + 1].CellStyle.Color     = System.Drawing.Color.FromArgb(0x1A, 0x1A, 0x1A);
                ws[1, c + 1].CellStyle.Font.RGBColor = System.Drawing.Color.White;
            }

            int row = 2;
            foreach (var r in _displayed)
            {
                ws[row, 1].Text  = r.VehicleNo;
                ws[row, 2].Text  = r.ChassisNo;
                ws[row, 3].Text  = r.Model;
                ws[row, 4].Text  = r.UserName;
                ws[row, 5].Text  = r.UserMobile;
                ws[row, 6].Text  = r.UserAddress ?? "";
                ws[row, 7].Text  = r.Address ?? "";
                ws[row, 8].Text  = r.Lat?.ToString("F5") ?? "";
                ws[row, 9].Text  = r.Lng?.ToString("F5") ?? "";
                ws[row, 10].Text = r.DeviceTime;
                ws[row, 11].Text = r.ServerTime;
                row++;
            }

            ws.UsedRange.AutofitColumns();
            wb.SaveAs(dlg.FileName);
            MessageBox.Show($"Exported {_displayed.Count:N0} records.", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Export PDF ───────────────────────────────────────────────────────────
    private void btnExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_displayed.Count == 0) { MessageBox.Show("No records to export."); return; }

        var dlg = new SaveFileDialog
        {
            Filter   = "PDF Document (*.pdf)|*.pdf",
            FileName = $"SearchLogs_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var doc  = new PdfDocument();
            doc.PageSettings.Orientation = PdfPageOrientation.Landscape;
            var page  = doc.Pages.Add();
            var gfx   = page.Graphics;
            var bold  = new PdfStandardFont(PdfFontFamily.Helvetica, 10, PdfFontStyle.Bold);
            var font  = new PdfStandardFont(PdfFontFamily.Helvetica, 7);
            var hdr   = new PdfStandardFont(PdfFontFamily.Helvetica, 7, PdfFontStyle.Bold);

            gfx.DrawString($"VK Enterprises — Search Logs  ({DateTime.Today:dd MMM yyyy})",
                bold, PdfBrushes.Black, new Syncfusion.Drawing.PointF(0, 0));

            var grid = new PdfGrid();
            grid.Style.Font = font;
            grid.Columns.Add(11);

            var hRow = grid.Headers.Add(1)[0];
            string[] headers = { "VRN","Chassis","Model","Agent","Mobile","Agent Address","Search Address","Lat","Lng","Device Time","Server Time" };
            for (int c = 0; c < headers.Length; c++)
            {
                hRow.Cells[c].Value                   = headers[c];
                hRow.Cells[c].Style.Font              = hdr;
                hRow.Cells[c].Style.BackgroundBrush   = new PdfSolidBrush(Syncfusion.Drawing.Color.FromArgb(255, 26, 26, 26));
                hRow.Cells[c].Style.TextBrush         = PdfBrushes.White;
            }

            foreach (var r in _displayed)
            {
                var pRow = grid.Rows.Add();
                pRow.Cells[0].Value = r.VehicleNo;
                pRow.Cells[1].Value = r.ChassisNo;
                pRow.Cells[2].Value = r.Model;
                pRow.Cells[3].Value = r.UserName;
                pRow.Cells[4].Value = r.UserMobile;
                pRow.Cells[5].Value = r.UserAddress ?? "";
                pRow.Cells[6].Value = r.Address ?? "";
                pRow.Cells[7].Value = r.Lat?.ToString("F4") ?? "";
                pRow.Cells[8].Value = r.Lng?.ToString("F4") ?? "";
                pRow.Cells[9].Value = r.DeviceTime;
                pRow.Cells[10].Value = r.ServerTime;
            }

            grid.Draw(page, new Syncfusion.Drawing.PointF(0, 24));

            using var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write);
            doc.Save(fs);
            MessageBox.Show($"Exported {_displayed.Count:N0} records.", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PDF export failed: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
