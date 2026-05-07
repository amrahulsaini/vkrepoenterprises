using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VRASDesktopApp.Models;
using VRASDesktopApp.Data;
using System.Collections.Generic;

namespace VRASDesktopApp.Finances;

public partial class FinancesManagerPage : Page
{
    public FinancesManagerPage()
    {
        InitializeComponent();
        _financeRepo = new FinanceRepository();
        _branchRepo  = new BranchRepository();
        _preloadFinancesTask = _financeRepo.GetFinancesAsync();
    }

    private readonly FinanceRepository _financeRepo;
    private readonly BranchRepository  _branchRepo;

    private Task<List<(int Id, string Name, long BranchCount, long TotalRecords)>>? _preloadFinancesTask;

    private List<FinanceListItem> _allFinances = new();
    private ICollectionView? _financesView;

    private readonly Dictionary<int, List<BranchSummaryItem>> _branchCache = new();
    private int _loadingForFinanceId = -1;

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

        if (_branchCache.TryGetValue(financeId, out var cached))
        {
            dgBranches.ItemsSource = cached;
            _ = RefreshBranchCacheAsync(financeId);
            return;
        }

        _loadingForFinanceId = financeId;
        SetBranchLoading(true);
        try
        {
            var list = await FetchBranchesAsync(financeId);
            if (_loadingForFinanceId == financeId)
            {
                _branchCache[financeId] = list;
                dgBranches.ItemsSource  = list;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load branches: {ex.Message}",
                "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBranchLoading(false);
        }
    }

    private async Task RefreshBranchCacheAsync(int financeId)
    {
        try
        {
            var list = await FetchBranchesAsync(financeId);
            _branchCache[financeId] = list;
            if (dgFinances.SelectedItem is FinanceListItem fi && fi.Id == financeId)
                dgBranches.ItemsSource = list;
        }
        catch { /* silent */ }
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
            dgBranches.ItemsSource = null;
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
            $"Delete branch \"{bi.BranchName}\"?\n\nThe branch will be deactivated.",
            "Delete Branch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _branchRepo.DeleteBranchAsync(branchId);
            _branchCache.Remove(fi.Id);
            await LoadBranchesForFinanceAsync(fi.Id, fi.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete branch: {ex.Message}",
                "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─────────────────────────────────────────────────────
    //  Scoped loading helpers
    // ─────────────────────────────────────────────────────

    private void SetFinanceLoading(bool loading)
        => financeLoadingGrid.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;

    private void SetBranchLoading(bool loading)
        => branchLoadingGrid.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
}
