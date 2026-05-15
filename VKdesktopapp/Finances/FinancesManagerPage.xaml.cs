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
        // Kick off the finances fetch immediately so the page paints fast
        _preloadFinancesTask = _financeRepo.GetFinancesAsync();
        // Set once; filter manages what's visible — no ItemsSource swapping needed
        dgBranches.ItemsSource = _displayedBranches;
    }

    private readonly FinanceRepository _financeRepo;
    private readonly BranchRepository  _branchRepo;

    private Task<List<(int Id, string Name, long BranchCount, long TotalRecords)>>? _preloadFinancesTask;

    private List<FinanceListItem> _allFinances = new();
    private ICollectionView? _financesView;

    // Branch search — ObservableCollection so the DataGrid ItemsSource never changes
    private readonly ObservableCollection<BranchSummaryItem> _displayedBranches = new();
    private List<BranchSummaryItem> _allCurrentBranches = new();

    // Cache: branchId→ list so re-selecting a finance is instant
    private readonly Dictionary<int, List<BranchSummaryItem>> _branchCache = new();

    private int _loadingForFinanceId = -1;
    private bool _isViewAll = false;
    private bool _suppressSelectionChange = false;

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
            MessageBox.Show(
                $"Failed to load finances.\n\nServer: {App.ApiBaseUrl}\n\nError: {ex.Message}",
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
                Name         = (f.Name ?? string.Empty).ToUpperInvariant(),
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
            _suppressSelectionChange = true;
            RebuildFinanceList(finances);
            if (selectId.HasValue)
            {
                var idx = _allFinances.FindIndex(x => x.Id == selectId.Value);
                if (idx >= 0) dgFinances.SelectedIndex = idx;
            }
            else if (!_isViewAll && _allFinances.Count > 0)
            {
                dgFinances.SelectedIndex = 0;
            }
            _suppressSelectionChange = false;
        }
        catch (Exception ex)
        {
            _suppressSelectionChange = false;
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
        if (_suppressSelectionChange) return;
        _isViewAll = false;
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
                BranchName    = (it.Name ?? string.Empty).ToUpperInvariant(),
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
        => await LoadViewAllAsync();

    private async Task LoadViewAllAsync()
    {
        _isViewAll             = true;
        _loadingForFinanceId   = -1;
        txtBranchSubtitle.Text = "All Finances";
        SetBranchLoading(true);
        try
        {
            var items = await _branchRepo.GetAllBranchesWithFinanceAsync();
            var list = items.Select(it => new BranchSummaryItem
            {
                BranchId       = it.Id.ToString(),
                BranchName     = (it.Name ?? string.Empty).ToUpperInvariant(),
                HeadOfficeName = (it.FinanceName ?? string.Empty).ToUpperInvariant(),
                FinanceId      = it.FinanceId,
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
            $"Delete \"{fi.Name}\"?\n\nThis will permanently delete the finance, ALL its branches, and ALL vehicle records.",
            "Delete Finance", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var dlg = new BulkOperationDialog($"Deleting \"{fi.Name}\" and all associated data…")
        { Owner = Window.GetWindow(this) };
        dlg.Show();
        try
        {
            await _financeRepo.DeleteFinanceAsync(fi.Id);
            dlg.SignalSuccess($"\"{fi.Name}\" and all its data deleted.");

            _branchCache.Remove(fi.Id);
            _isViewAll = false;
            SetBranchItemsSource(null);
            txtBranchSubtitle.Text = "Select a finance to view branches";
            await ReloadFinancesAsync();
        }
        catch (Exception ex)
        {
            dlg.SignalError($"Failed to delete finance: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────
    //  Branch Edit / Delete
    // ─────────────────────────────────────────────────────

    private async void BranchEdit_Click(object sender, RoutedEventArgs e)
    {
        if (dgBranches.SelectedItem is not BranchSummaryItem bi) return;
        if (!int.TryParse(bi.BranchId, out int branchId)) return;

        int financeId = GetFinanceIdForBranch(bi);
        if (financeId == 0) return;
        string financeName = _allFinances.FirstOrDefault(f => f.Id == financeId)?.Name ?? bi.HeadOfficeName;

        try
        {
            var branch = await _branchRepo.GetBranchAsync(branchId);
            if (branch == null) return;

            var w = new BranchEditorWindow(
                financeId, financeName, branchId,
                branch.Value.Name,
                branch.Value.Contact1, branch.Value.Contact2, branch.Value.Contact3,
                branch.Value.Address, branch.Value.BranchCode)
            { Owner = Window.GetWindow(this) };

            if (w.ShowDialog() == true)
            {
                _branchCache.Remove(financeId);
                if (_isViewAll) await LoadViewAllAsync();
                else await LoadBranchesForFinanceAsync(financeId, financeName);
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
        if (!int.TryParse(bi.BranchId, out int branchId)) return;

        int financeId = GetFinanceIdForBranch(bi);
        string financeName = _allFinances.FirstOrDefault(f => f.Id == financeId)?.Name ?? bi.HeadOfficeName;

        var result = MessageBox.Show(
            $"Delete branch \"{bi.BranchName}\"?\n\nThis will permanently delete the branch and ALL its vehicle records.",
            "Delete Branch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var dlg = new BulkOperationDialog($"Deleting \"{bi.BranchName}\" and all its records…")
        { Owner = Window.GetWindow(this) };
        dlg.Show();
        try
        {
            await _branchRepo.DeleteBranchAsync(branchId);
            dlg.SignalSuccess($"\"{bi.BranchName}\" deleted.");

            if (financeId > 0) _branchCache.Remove(financeId);
            if (_isViewAll)
            {
                await ReloadFinancesAsync();
                await LoadViewAllAsync();
            }
            else
            {
                await LoadBranchesForFinanceAsync(financeId, financeName);
                await ReloadFinancesAsync(financeId);
            }
        }
        catch (Exception ex)
        {
            dlg.SignalError($"Failed to delete branch: {ex.Message}");
        }
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
            var rows = await DesktopApiClient.ExportFinanceRecordsAsync(fi.Id);
            ExportToExcel(rows, fi.Name, dlg.FileName);
            MessageBox.Show($"Exported {rows.Count:N0} records to:\n{dlg.FileName}",
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
            var rows = await DesktopApiClient.ExportBranchRecordsAsync(branchId);
            ExportToExcel(rows, bi.BranchName, dlg.FileName);
            MessageBox.Show($"Exported {rows.Count:N0} records to:\n{dlg.FileName}",
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
        if (!int.TryParse(bi.BranchId, out int branchId)) return;

        int financeId = GetFinanceIdForBranch(bi);
        string financeName = _allFinances.FirstOrDefault(f => f.Id == financeId)?.Name ?? bi.HeadOfficeName;

        var result = MessageBox.Show(
            $"Clear ALL records from \"{bi.BranchName}\"?\n\nThis deletes every vehicle record for this branch. The branch itself is kept.",
            "Clear Records", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var dlg = new BulkOperationDialog($"Clearing all records from \"{bi.BranchName}\"…")
        { Owner = Window.GetWindow(this) };
        dlg.Show();
        try
        {
            var deleted = await DesktopApiClient.ClearBranchRecordsAsync(branchId);
            dlg.SignalSuccess($"Cleared {deleted:N0} records from \"{bi.BranchName}\".");

            if (financeId > 0) _branchCache.Remove(financeId);
            if (_isViewAll)
            {
                await ReloadFinancesAsync();
                await LoadViewAllAsync();
            }
            else
            {
                await LoadBranchesForFinanceAsync(financeId, financeName);
                await ReloadFinancesAsync(financeId);
            }
        }
        catch (Exception ex)
        {
            dlg.SignalError($"Failed to clear records: {ex.Message}");
        }
    }

    // Returns the finance ID for a branch — from the branch item (View All) or selected finance
    private int GetFinanceIdForBranch(BranchSummaryItem bi)
    {
        if (bi.FinanceId > 0) return bi.FinanceId;
        if (dgFinances.SelectedItem is FinanceListItem fi) return fi.Id;
        return 0;
    }

    // ─────────────────────────────────────────────────────
    //  Excel export helper (Syncfusion XlsIO)
    // ─────────────────────────────────────────────────────

    private static readonly string[] ExportHeaders =
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

    private static void ExportToExcel(
        List<DesktopApiClient.ExportVehicleRow> rows, string sheetName, string filePath)
    {
        using var engine = new ExcelEngine();
        var app = engine.Excel;
        app.DefaultVersion = ExcelVersion.Xlsx;
        var workbook = app.Workbooks.Create(1);
        var sheet    = workbook.Worksheets[0];
        sheet.Name   = sheetName.Length > 31 ? sheetName[..31] : sheetName;

        // Header row
        for (int c = 0; c < ExportHeaders.Length; c++)
        {
            var cell = sheet[1, c + 1];
            cell.Text = ExportHeaders[c];
            cell.CellStyle.Font.Bold = true;
            cell.CellStyle.Color = System.Drawing.Color.FromArgb(0xFF, 0xF5, 0xA6, 0x23); // orange header
            cell.CellStyle.Font.Color = ExcelKnownColors.White;
        }

        // Data rows — field order must match ExportHeaders
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
                sheet[r + 2, c + 1].Text = vals[c] ?? string.Empty;
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
