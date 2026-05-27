using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Data;
using System.IO;
using Microsoft.Win32;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Tables;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Confirmations;

public partial class ConfirmationsManagerPage : Page
{
    // ── Paging state ──────────────────────────────────────────────────────
    // Server returns rows in 200-row pages. We keep an accumulating
    // ObservableCollection so infinite-scroll just appends to the grid.
    // _total mirrors the COUNT(*) the server returned so the header label
    // always knows the unfiltered grand total.
    private const int PageSize = 200;
    private readonly ObservableCollection<ConfirmationResponseItem> _items = new();
    private int  _page     = 0;
    private long _total    = 0;
    private bool _hasMore  = true;
    private bool _isLoading = false;

    // Debounce ticking for the search box so we don't fire a server call on
    // every keystroke.
    private CancellationTokenSource? _searchDebounce;

    public ConfirmationsManagerPage()
    {
        InitializeComponent();
        dgConfirmations.ItemsSource = _items;
        Loaded += async (s, e) =>
        {
            HookScrollListener();
            await ReloadAsync();
        };
    }

    // ── Loading ───────────────────────────────────────────────────────────

    private async Task ReloadAsync()
    {
        _items.Clear();
        _page    = 0;
        _hasMore = true;
        _total   = 0;
        UpdateCountLabel();
        await LoadNextPageAsync();
    }

    private async Task LoadNextPageAsync()
    {
        if (_isLoading || !_hasMore) return;
        _isLoading = true;
        try
        {
            string q    = Uri.EscapeDataString(txtSearch.Text.Trim());
            string from = dpFrom.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
            string to   = dpTo.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
            var url = $"{App.ApiBaseUrl}api/Confirmations/paged" +
                      $"?page={_page}&size={PageSize}" +
                      $"&q={q}&from={from}&to={to}";

            var resp = await App.HttpClient.GetFromJsonAsync<PagedResponse>(url);
            if (resp == null) return;

            _total   = resp.Total;
            _hasMore = resp.HasMore;
            foreach (var r in resp.Rows) _items.Add(r);
            _page++;

            UpdateCountLabel();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load confirmations: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateCountLabel()
    {
        lblCount.Text = _hasMore
            ? $"Showing {_items.Count:N0} of {_total:N0}"
            : $"{_items.Count:N0} records";
    }

    // ── Infinite scroll ───────────────────────────────────────────────────
    // The DataGrid's internal ScrollViewer is built once the control is
    // loaded. Hook into ScrollChanged and fire LoadNextPage when the user
    // is within 200px of the bottom.
    private void HookScrollListener()
    {
        var sv = FindVisualChild<ScrollViewer>(dgConfirmations);
        if (sv != null) sv.ScrollChanged += OnGridScrollChanged;
    }

    private async void OnGridScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        double remaining = sv.ScrollableHeight - sv.VerticalOffset;
        if (remaining < 200 && _hasMore && !_isLoading)
            await LoadNextPageAsync();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var deeper = FindVisualChild<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // ── User input handlers ───────────────────────────────────────────────

    private async void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        lblSearchWatermark.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;

        // Cancel any in-flight debounce and start a fresh one. After 400ms
        // of inactivity we re-query the server. Avoids hammering the API
        // while the admin is mid-type.
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;
        try
        {
            await Task.Delay(400, token);
            await ReloadAsync();
        }
        catch (TaskCanceledException) { /* later keystroke superseded this */ }
    }

    private async void Date_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        await ReloadAsync();
    }

    private async void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    // ── PDF export ────────────────────────────────────────────────────────
    // Exports whatever's CURRENTLY in the grid (the loaded pages). To export
    // everything across all pages, the admin clears the filters first and
    // scrolls to the bottom — the grid auto-fetches until _hasMore is false.
    private void btnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = "ConfirmationsReport"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                btnExport.IsEnabled = false;
                using (var pdfDocument = new PdfDocument())
                {
                    pdfDocument.PageSettings.Size = PdfPageSize.A4;
                    var pdfPage  = pdfDocument.Pages.Add();
                    var graphics = pdfPage.Graphics;
                    var font     = new PdfStandardFont(PdfFontFamily.Helvetica, 9f);

                    using (var fileStream = File.Create(saveFileDialog.FileName))
                    {
                        var pdfLightTable = new PdfLightTable();
                        var pdfLightTableStyle = new PdfLightTableStyle
                        {
                            CellPadding = 2f,
                            ShowHeader = true
                        };
                        pdfLightTable.Style = pdfLightTableStyle;

                        var defaultStyle = new PdfCellStyle(font, PdfBrushes.Black, new PdfPen(PdfBrushes.DarkGray, 0.5f));
                        pdfLightTable.Style.DefaultStyle = defaultStyle;

                        var dataTable = new DataTable();
                        dataTable.Columns.Add("Sr.No.");
                        dataTable.Columns.Add("Vehicle No");
                        dataTable.Columns.Add("Chassis No");
                        dataTable.Columns.Add("Model");
                        dataTable.Columns.Add("Seizer");
                        dataTable.Columns.Add("Status");
                        dataTable.Columns.Add("Confirmed On");

                        int num = 1;
                        foreach (var confirmation in _items)
                        {
                            dataTable.Rows.Add(
                                num.ToString(),
                                confirmation.VehicleNo ?? "",
                                confirmation.ChassisNo ?? "",
                                confirmation.Model ?? "",
                                confirmation.SeizerName ?? "",
                                confirmation.Status ?? "",
                                confirmation.ConfirmedOn ?? ""
                            );
                            num++;
                        }

                        pdfLightTable.DataSource = dataTable;
                        pdfLightTable.Draw(pdfPage, new Syncfusion.Drawing.PointF(0f, 0f));
                        pdfDocument.Save(fileStream);
                        fileStream.Flush();
                    }
                    pdfDocument.Close(completely: true);
                }
                MessageBox.Show($"Exported {_items.Count:N0} loaded records to PDF.\n" +
                                $"Total in tenant: {_total:N0}. Scroll to bottom to load all before exporting.",
                    "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnExport.IsEnabled = true;
        }
    }

    // ── DTO matching /api/Confirmations/paged response shape ──────────────
    private record PagedResponse(long Total, int Page, int Size, bool HasMore, List<ConfirmationResponseItem> Rows);
}
