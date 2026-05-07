using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        // Kick off the finance query IMMEDIATELY in the constructor —
        // before the page is even shown. By the time Page_Loaded fires and
        // the UI renders, the server round-trip may already be done.
        _preloadFinancesTask = _financeRepo.GetFinancesAsync();
    }

    private readonly FinanceRepository _financeRepo;
    private readonly BranchRepository  _branchRepo;

    // Pre-started DB task (fired in constructor for lowest perceived latency)
    private Task<List<(int Id, string Name, long BranchCount, long TotalRecords)>>? _preloadFinancesTask;

    // Finance list kept in memory for instant search (0 ms, no DB)
    private List<FinanceListItem> _allFinances = new();
    private ICollectionView? _financesView;

    // Branch cache: financeId → list. 2nd+ clicks on same finance are instant.
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
            // Reuse the pre-started task from the constructor when possible.
            // If it's already done, await returns immediately (0 ms wait).
            var finances = _preloadFinancesTask != null
                ? await _preloadFinancesTask
                : await _financeRepo.GetFinancesAsync();
            _preloadFinancesTask = null;

            _allFinances = new List<FinanceListItem>(finances.Count);
            foreach (var f in finances)
                _allFinances.Add(new FinanceListItem
                {
                    Id          = f.Id,
                    Name        = f.Name,
                    BranchCount = f.BranchCount,
                    TotalRecords = f.TotalRecords
                });

            _financesView = CollectionViewSource.GetDefaultView(_allFinances);
            _financesView.Filter = FilterFinance;
            dgFinances.ItemsSource = _financesView;

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

        // Cache hit → instant display, silent background refresh
        if (_branchCache.TryGetValue(financeId, out var cached))
        {
            dgBranches.ItemsSource = cached;
            _ = RefreshBranchCacheAsync(financeId);
            return;
        }

        // First load → show branch panel spinner only
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

            var finances = await _financeRepo.GetFinancesAsync();
            _allFinances = new List<FinanceListItem>(finances.Count);
            foreach (var f in finances)
                _allFinances.Add(new FinanceListItem
                {
                    Id          = f.Id,
                    Name        = f.Name,
                    BranchCount = f.BranchCount,
                    TotalRecords = f.TotalRecords
                });

            _financesView = CollectionViewSource.GetDefaultView(_allFinances);
            _financesView.Filter = FilterFinance;
            dgFinances.ItemsSource = _financesView;

            var idx = _allFinances.FindIndex(x => x.Id == id);
            if (idx >= 0) dgFinances.SelectedIndex = idx;
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
            _branchCache.Remove(fi.Id); // invalidate so next load is fresh
            await LoadBranchesForFinanceAsync(fi.Id, fi.Name);
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
