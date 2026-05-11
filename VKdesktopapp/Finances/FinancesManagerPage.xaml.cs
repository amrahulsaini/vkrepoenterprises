using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Syncfusion.XlsIO;
using VRASDesktopApp.Models;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Finances;

public partial class FinancesManagerPage : Page
{
    public FinancesManagerPage()
    {
        InitializeComponent();
        _financeRepo = new FinanceRepository();
        _branchRepo  = new BranchRepository();
        // Chain after warmup so finance query reuses the warm pooled connection (0 ms vs 4-5 s cold)
        _preloadFinancesTask = App.WarmUpTask
            .ContinueWith(_ => _financeRepo.GetFinancesAsync(), TaskScheduler.Default)
            .Unwrap();
        // Set once; filter manages what's visible — no ItemsSource swapping needed
        dgBranches.ItemsSource = _displayedBranches;
    }

    private readonly FinanceRepository _financeRepo;
    private readonly BranchRepository  _branchRepo;
    private readonly ExportRepository  _exportRepo = new(); // kept for Excel export (direct DB — BulkCopy path)

    private Task<List<(int Id, string Name, long BranchCount, long TotalRecords)>>? _preloadFinancesTask;

    private List<FinanceListItem> _allFinances = new();
    private ICollectionView? _financesView;

    // Branch search — ObservableCollection so the DataGrid ItemsSource never changes
    private readonly ObservableCollection<BranchSummaryItem> _displayedBranches = new();
    private List<BranchSummaryItem> _allCurrentBranches = new();

    // Cache: branchId→ list so re-selecting a finance is instant
    private readonly Dictionary<int, List<BranchSummaryItem>> _branchCache = new();

    private int _loadingForFinanceId = -1;

    // Spinner storyboards — created once, reused
    private Storyboard? _financeSpinSb;
    private Storyboard? _branchSpinSb;

    // ─────────────────────────────────────────────────────
    //  Page load
    // ─────────────────────────────────────────────────────

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        SetFinanceLoading(true);
        try
        {
            var finances = _preloadFinancesTask != null
                ? await _preloadFinancesTask
                : await _financeRepo.GetFinancesAsync();
            _preloadFinancesTask = null;

            RebuildFinanceList(finances);

            if (_allFinances.Count > 0)
            {
                dgFinances.SelectedIndex = 0;
                await LoadBranchesForFinanceAsync(_allFinances[0].Id, _allFinances[0].Name);
            }
        }
        catch (Exception ex)
        {
            var connInfo = MySqlFactory.GetConnectionInfoMasked();
            MessageBox.Show(
                $"Failed to load finances.\n\nConnection: {connInfo}\n\nError: {ex.Message}",
                "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFinanceLoading(false);
        }
    }

    private void RebuildFinanceList(List<(int Id, string Name, long BranchCount, long TotalRecords)> finances)
    {
        _allFinances = new List<FinanceListItem>(finances.Count);
        foreach (var f in finances)
            _allFinances.Add(new FinanceListItem
            {
                Id           = f.Id,
                Name         = f.Name,
                BranchCount  = f.BranchCount,
                TotalRecords = f.TotalRecords
            });
        _financesView = CollectionViewSource.GetDefaultView(_allFinances);
        _financesView.Filter = FilterFinance;
        dgFinances.ItemsSource = _financesView;
    }

    private async Task ReloadFinancesAsync(int? selectId = null)
    {
        SetFinanceLoading(true);
        try
        {
            var finances = await _financeRepo.GetFinancesAsync();
            RebuildFinanceList(finances);
            if (selectId.HasValue)
            {
                var idx = _allFinances.FindIndex(x => x.Id == selectId.Value);
                if (idx >= 0) { dgFinances.SelectedIndex = idx; return; }
            }
            if (_allFinances.Count > 0) dgFinances.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to refresh: {ex.Message}", "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFinanceLoading(false);
        }
    }

    // ─────────────────────────────────────────────────────
    //  Search — pure in-memory, 0 ms
    // ─────────────────────────────────────────────────────

    private bool FilterFinance(object obj) =>
        obj is FinanceListItem item &&
        (string.IsNullOrEmpty(txtSearch.Text) ||
         item.Name.Contains(txtSearch.Text, StringComparison.OrdinalIgnoreCase));

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        => _financesView?.Refresh();

    // ─────────────────────────────────────────────────────
    //  Branch search — pure in-memory, 0 ms
    // ─────────────────────────────────────────────────────

    private void SetBranchItemsSource(List<BranchSummaryItem>? list)
    {
        _allCurrentBranches = list ?? new List<BranchSummaryItem>();
        ApplyBranchFilter();
    }

    private void ApplyBranchFilter()
    {
        var term = txtBranchSearch?.Text ?? string.Empty;
        _displayedBranches.Clear();
        foreach (var b in _allCurrentBranches)
        {
            if (string.IsNullOrEmpty(term) ||
                b.BranchName.Contains(term, StringComparison.OrdinalIgnoreCase))
                _displayedBranches.Add(b);
        }
    }

    private void txtBranchSearch_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyBranchFilter();

    // ─────────────────────────────────────────────────────
    //  Branch loading — cached for instant 2nd+ clicks
    // ─────────────────────────────────────────────────────

    private async void dgFinances_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgFinances.SelectedItem is FinanceListItem fi)
            await LoadBranchesForFinanceAsync(fi.Id, fi.Name);
    }

    private async Task LoadBranchesForFinanceAsync(int financeId, string financeName)
    {
        txtBranchSubtitle.Text = financeName;
        _loadingForFinanceId   = financeId;

        // Show cached data instantly — feels ~0 ms on repeat selection
        if (_branchCache.TryGetValue(financeId, out var cached))
            SetBranchItemsSource(cached);

        SetBranchLoading(true);
        try
        {
            var list = await FetchBranchesAsync(financeId);
            _branchCache[financeId] = list;
            if (_loadingForFinanceId == financeId)
                SetBranchItemsSource(list);
        }
        catch (Exception ex)
        {
            if (!_branchCache.ContainsKey(financeId))
                MessageBox.Show($"Failed to load branches: {ex.Message}",
                    "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBranchLoading(false);
        }
    }

    private async Task<List<BranchSummaryItem>> FetchBranchesAsync(int financeId)
    {
        var items = await _branchRepo.GetBranchesByFinanceAsync(financeId);
        var list  = new List<BranchSummaryItem>(items.Count);
        foreach (var it in items)
        {
            list.Add(new BranchSummaryItem
            {
                BranchId      = it.Id.ToString(),
                BranchName    = it.Name,
                ContactMobile = it.Contact1,
                Records       = it.TotalRecords,
                UpdatedOn     = it.UploadedAt
            });
        }
        return list;
    }

    // ─────────────────────────────────────────────────────
    //  View All — shows every branch across all finances
    // ─────────────────────────────────────────────────────

    private async void btnViewAll_Click(object sender, RoutedEventArgs e)
    {
        txtBranchSubtitle.Text = "All Finances";
        _loadingForFinanceId   = -1;
        SetBranchLoading(true);
        try
        {
            var items = await _branchRepo.GetAllBranchesWithFinanceAsync();
            var list = items.Select(it => new BranchSummaryItem
            {
                BranchId       = it.Id.ToString(),
                BranchName     = it.Name,
                HeadOfficeName = it.FinanceName,
                Records        = it.TotalRecords,
                UpdatedOn      = it.UploadedAt
            }).ToList();
            SetBranchItemsSource(list);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load all branches: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBranchLoading(false); }
    }

    // ─────────────────────────────────────────────────────
    //  Add Finance
    // ─────────────────────────────────────────────────────

    private async void btnAddFinance_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewFinanceDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        SetFinanceLoading(true);
        try
        {
            var id = await _financeRepo.CreateFinanceAsync(dlg.FinanceName, null);
            await ReloadFinancesAsync(id);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create finance: {ex.Message}",
                "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFinanceLoading(false);
        }
    }

    // ─────────────────────────────────────────────────────
    //  Add Branch
    // ─────────────────────────────────────────────────────

    private async void btnAddBranch_Click(object sender, RoutedEventArgs e)
    {
        if (dgFinances.SelectedItem is not FinanceListItem fi)
        {
            MessageBox.Show("Select a finance first.",
                "Finances", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var w = new BranchEditorWindow(fi.Id, fi.Name) { Owner = Window.GetWindow(this) };
        if (w.ShowDialog() == true)
        {
            _branchCache.Remove(fi.Id);
            await LoadBranchesForFinanceAsync(fi.Id, fi.Name);
        }
    }

    // ─────────────────────────────────────────────────────
    //  Context menus — select row on right-click
    // ─────────────────────────────────────────────────────

    private void dgFinances_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(
            dgFinances.InputHitTest(System.Windows.Input.Mouse.GetPosition(dgFinances)) as DependencyObject);
        if (row == null) { e.Handled = true; return; }
        row.IsSelected = true;
    }

    private void dgBranches_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(
            dgBranches.InputHitTest(System.Windows.Input.Mouse.GetPosition(dgBranches)) as DependencyObject);
        if (row == null) { e.Handled = true; return; }
        row.IsSelected = true;
    }

    private static T? FindVisualParent<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T t) return t;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    // ─────────────────────────────────────────────────────
    //  Finance Edit / Delete
    // ─────────────────────────────────────────────────────

    private async void FinanceEdit_Click(object sender, RoutedEventArgs e)
    {
        if (dgFinances.SelectedItem is not FinanceListItem fi) return;

        var dlg = new NewFinanceDialog(fi.Id, fi.Name) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await _financeRepo.UpdateFinanceAsync(fi.Id, dlg.FinanceName);
            await ReloadFinancesAsync(fi.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update finance: {ex.Message}",
                "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void FinanceDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgFinances.SelectedItem is not FinanceListItem fi) return;

        var result = MessageBox.Show(
            $"Delete \"{fi.Name}\"?\n\nThis will permanently delete the finance. Branches must be deleted first.",
            "Delete Finance", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _financeRepo.DeleteFinanceAsync(fi.Id);

            _branchCache.Remove(fi.Id);
            SetBranchItemsSource(null);
            txtBranchSubtitle.Text = "Select a finance to view branches";
            await ReloadFinancesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete finance: {ex.Message}",
                "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─────────────────────────────────────────────────────
    //  Branch Edit / Delete
    // ─────────────────────────────────────────────────────

    private async void BranchEdit_Click(object sender, RoutedEventArgs e)
    {
        if (dgBranches.SelectedItem is not BranchSummaryItem bi) return;
        if (dgFinances.SelectedItem is not FinanceListItem fi) return;
        if (!int.TryParse(bi.BranchId, out int branchId)) return;

        try
        {
            var branch = await _branchRepo.GetBranchAsync(branchId);
            if (branch == null) return;

            var w = new BranchEditorWindow(
                fi.Id, fi.Name, branchId,
                branch.Value.Name,
                branch.Value.Contact1, branch.Value.Contact2, branch.Value.Contact3,
                branch.Value.Address, branch.Value.BranchCode)
            { Owner = Window.GetWindow(this) };

            if (w.ShowDialog() == true)
            {
                _branchCache.Remove(fi.Id);
                await LoadBranchesForFinanceAsync(fi.Id, fi.Name);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load branch details: {ex.Message}",
                "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BranchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgBranches.SelectedItem is not BranchSummaryItem bi) return;
        if (dgFinances.SelectedItem is not FinanceListItem fi) return;
        if (!int.TryParse(bi.BranchId, out int branchId)) return;

        var result = MessageBox.Show(
            $"Delete branch \"{bi.BranchName}\"?\n\nThis will permanently delete the branch and ALL its vehicle records.",
            "Delete Branch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        txtBranchSubtitle.Text = "Deleting branch…";
        SetBranchLoading(true);
        try
        {
            await _branchRepo.DeleteBranchAsync(branchId);  // 1 HTTP call, server does all SQL

            _branchCache.Remove(fi.Id);
            await LoadBranchesForFinanceAsync(fi.Id, fi.Name);
            await ReloadFinancesAsync(fi.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete branch: {ex.Message}",
                "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBranchLoading(false); }
    }

    // ─────────────────────────────────────────────────────
    //  Finance — Download All Records
    // ─────────────────────────────────────────────────────

    private async void FinanceDownloadAll_Click(object sender, RoutedEventArgs e)
    {
        if (dgFinances.SelectedItem is not FinanceListItem fi) return;

        var dlg = new SaveFileDialog
        {
            Title      = $"Save records for {fi.Name}",
            Filter     = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName   = $"{fi.Name}_AllRecords_{DateTime.Now:yyyyMMdd}.xlsx",
            DefaultExt = "xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        SetFinanceLoading(true);
        try
        {
            var dt = await _exportRepo.GetFinanceRecordsAsync(fi.Id);
            ExportToExcel(dt, fi.Name, dlg.FileName);
            MessageBox.Show($"Exported {dt.Rows.Count:N0} records to:\n{dlg.FileName}",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetFinanceLoading(false); }
    }

    // ─────────────────────────────────────────────────────
    //  Branch — Download Records / Clear Records
    // ─────────────────────────────────────────────────────

    private async void BranchDownload_Click(object sender, RoutedEventArgs e)
    {
        if (dgBranches.SelectedItem is not BranchSummaryItem bi) return;
        if (!int.TryParse(bi.BranchId, out int branchId)) return;

        var dlg = new SaveFileDialog
        {
            Title      = $"Save records for {bi.BranchName}",
            Filter     = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName   = $"{bi.BranchName}_Records_{DateTime.Now:yyyyMMdd}.xlsx",
            DefaultExt = "xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        SetBranchLoading(true);
        try
        {
            var dt = await _exportRepo.GetBranchRecordsAsync(branchId);
            ExportToExcel(dt, bi.BranchName, dlg.FileName);
            MessageBox.Show($"Exported {dt.Rows.Count:N0} records to:\n{dlg.FileName}",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBranchLoading(false); }
    }

    private async void BranchClearRecords_Click(object sender, RoutedEventArgs e)
    {
        if (dgBranches.SelectedItem is not BranchSummaryItem bi) return;
        if (dgFinances.SelectedItem is not FinanceListItem fi) return;
        if (!int.TryParse(bi.BranchId, out int branchId)) return;

        var result = MessageBox.Show(
            $"Clear ALL records from \"{bi.BranchName}\"?\n\nThis deletes every vehicle record for this branch. The branch itself is kept.",
            "Clear Records", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        txtBranchSubtitle.Text = "Clearing records…";
        SetBranchLoading(true);
        try
        {
            await DesktopApiClient.ClearBranchRecordsAsync(branchId);  // 1 HTTP call, server does all SQL

            _branchCache.Remove(fi.Id);
            await LoadBranchesForFinanceAsync(fi.Id, fi.Name);
            await ReloadFinancesAsync(fi.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to clear records: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBranchLoading(false); }
    }

    // ─────────────────────────────────────────────────────
    //  Excel export helper (Syncfusion XlsIO)
    // ─────────────────────────────────────────────────────

    private static void ExportToExcel(DataTable dt, string sheetName, string filePath)
    {
        using var engine = new ExcelEngine();
        var app = engine.Excel;
        app.DefaultVersion = ExcelVersion.Xlsx;
        var workbook = app.Workbooks.Create(1);
        var sheet    = workbook.Worksheets[0];
        sheet.Name   = sheetName.Length > 31 ? sheetName[..31] : sheetName;

        // Write column headers from ExportRepository.Headers if available,
        // otherwise fall back to DataTable column names
        var headers = ExportRepository.Headers;
        for (int c = 0; c < dt.Columns.Count; c++)
        {
            var cell = sheet[1, c + 1];
            cell.Text = c < headers.Length ? headers[c] : dt.Columns[c].ColumnName;
            cell.CellStyle.Font.Bold = true;
            cell.CellStyle.Color = System.Drawing.Color.FromArgb(0xFF, 0xF5, 0xA6, 0x23); // orange header
            cell.CellStyle.Font.Color = ExcelKnownColors.White;
        }

        // Write data rows
        for (int r = 0; r < dt.Rows.Count; r++)
        {
            for (int c = 0; c < dt.Columns.Count; c++)
                sheet[r + 2, c + 1].Text = dt.Rows[r][c]?.ToString() ?? string.Empty;
        }

        sheet.UsedRange.AutofitColumns();
        workbook.SaveAs(filePath);
    }

    // ─────────────────────────────────────────────────────
    //  Spinner helpers
    // ─────────────────────────────────────────────────────

    private static Storyboard MakeSpinSb(RotateTransform rt)
    {
        var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.8)))
            { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(anim, rt);
        Storyboard.SetTargetProperty(anim, new PropertyPath(RotateTransform.AngleProperty));
        var sb = new Storyboard();
        sb.Children.Add(anim);
        return sb;
    }

    private void SetFinanceLoading(bool loading)
    {
        financeSpinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (loading)
        {
            _financeSpinSb ??= MakeSpinSb(financeSpinnerRt);
            _financeSpinSb.Begin();
        }
        else
            _financeSpinSb?.Stop();
    }

    private void SetBranchLoading(bool loading)
    {
        branchSpinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (loading)
        {
            _branchSpinSb ??= MakeSpinSb(branchSpinnerRt);
            _branchSpinSb.Begin();
        }
        else
            _branchSpinSb?.Stop();
    }
}
