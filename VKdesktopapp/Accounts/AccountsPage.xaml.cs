using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Accounts;

public partial class AccountsPage : Page
{
    private List<AcctRow> _all = new();
    private readonly ObservableCollection<AcctRow> _shown = new();
    private bool _ready;

    private class StatusPick : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string Key  { get; set; } = "";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly List<StatusPick> _statusPicks = new()
    {
        new StatusPick { Name = "OK for billing",      Key = "immediate" },
        new StatusPick { Name = "Hold for collection", Key = "hold" },
        new StatusPick { Name = "Collection done",     Key = "collection_done" },
        new StatusPick { Name = "Cancel",              Key = "cancel" },
        new StatusPick { Name = "Billing Done",        Key = "billed" },
    };

    private class AgentPick : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private List<AgentPick> _agentPicks = new();

    private void ShowAgentPicks()
    {
        var term = (txtAgentSearch.Text ?? "").Trim();
        lstAgentPicks.ItemsSource = term.Length == 0
            ? _agentPicks
            : _agentPicks.Where(a => a.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
    }

    private void UpdateAgentButton()
    {
        var picked = _agentPicks.Where(a => a.IsChecked).Select(a => a.Name).ToList();
        btnAgents.Content = picked.Count switch
        {
            0 => "All agents",
            1 => picked[0],
            _ => $"{picked.Count} agents",
        };
    }

    private void AgentSearch_Changed(object sender, TextChangedEventArgs e) => ShowAgentPicks();

    private void AgentPick_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAgentButton();
        if (_ready) ApplyFilter();
    }

    private void AgentPicker_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter && e.Key != System.Windows.Input.Key.Escape) return;
        e.Handled = true;
        if (e.Key == System.Windows.Input.Key.Enter && _ready) ApplyFilter();
        btnAgents.IsChecked = false;
    }

    private void btnApplyAgents_Click(object sender, RoutedEventArgs e)
    {
        if (_ready) ApplyFilter();
        btnAgents.IsChecked = false;
    }

    private string PickedAgentName()
    {
        var picked = _agentPicks.Where(a => a.IsChecked).Select(a => a.Name).ToList();
        if (picked.Count == 1) return picked[0];
        return (grid.SelectedItem as AcctRow)?.AgentName ?? "";
    }

    private void UpdateStatusButton()
    {
        var picked = _statusPicks.Where(p => p.IsChecked).Select(p => p.Name).ToList();
        btnStatuses.Content = picked.Count switch
        {
            0 => "All statuses",
            1 => picked[0],
            _ => $"{picked.Count} statuses",
        };
    }

    private async void StatusPick_Changed(object sender, RoutedEventArgs e)
    {
        UpdateStatusButton();
        if (_ready) await LoadAsync();
    }

    private async void btnClearStatus_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _statusPicks) p.IsChecked = false;
        UpdateStatusButton();
        if (_ready) await LoadAsync();
    }

    private static string Squash4(string? s) =>
        new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();


    public AccountsPage()
    {
        InitializeComponent();
        grid.ItemsSource = _shown;
        colBilling.ItemsSource = CRMRSDesktopApp.Couriers.CouriersPage.BillingPicks;
        colInventory.ItemsSource = CRMRSDesktopApp.Couriers.CouriersPage.YesNo;

        lstStatusPicks.ItemsSource = _statusPicks;
        UpdateStatusButton();
        dpFrom.DisplayDateEnd = DateTime.Today;
        dpTo.DisplayDateEnd = DateTime.Today;
        dpPayDate.DisplayDateEnd = DateTime.Today;
        dpFrom.SelectedDate = DateTime.Today.AddDays(-7);
        dpTo.SelectedDate = DateTime.Today;
        Loaded += async (_, __) => { _ready = true; await LoadAsync(); };
    }

    internal class AcctRow : INotifyPropertyChanged
    {
        internal DesktopApiClient.RepoSubmissionDto Src { get; private set; } = null!;
        internal HashSet<string> Dirty { get; } = new();
        private decimal? _repo;
        private decimal? _adv;

        internal static AcctRow From(DesktopApiClient.RepoSubmissionDto d) =>
            new() { Src = d, _repo = d.RepoCharges, _adv = d.Advance };

        private void Put(string field, string? old, string? value, Func<string, DesktopApiClient.RepoSubmissionDto> apply,
                         [CallerMemberName] string? prop = null)
        {
            var v = (value ?? "").Trim();
            if (v == (old ?? "").Trim()) return;
            Src = apply(v);
            Dirty.Add(field);
            Changed(prop);
        }

        public long Id => Src.Id;
        public string RepoDate
        {
            get => Src.CreatedAt;
            set { if (!DateTime.TryParse(value, out var date)) throw new FormatException("Enter a valid repo date.");
                Put("CreatedAt", Src.CreatedAt, date.ToString("yyyy-MM-dd HH:mm"), v => Src with { CreatedAt = v }); }
        }
        public string AgentName
        {
            get
            {
                var a = (Src.AgentName ?? "").Trim();
                return a.Length > 0 ? a : (Src.SubmittedByName ?? "").Trim();
            }
            set => Put(nameof(AgentName), AgentName, value, v => Src with { AgentName = v });
        }
        public string VehicleNo { get => string.IsNullOrWhiteSpace(Src.VehicleNo) ? Src.ChassisNo : Src.VehicleNo; set => Put(nameof(VehicleNo), VehicleNo, value, v => Src with { VehicleNo = v }); }
        public string CustomerName { get => Src.CustomerName; set => Put(nameof(CustomerName), Src.CustomerName, value, v => Src with { CustomerName = v }); }
        public string FinanceName { get => (Src.FinanceName ?? "").ToUpperInvariant(); set => Put(nameof(FinanceName), FinanceName, value, v => Src with { FinanceName = v }); }
        public string BranchName { get => (Src.BranchName ?? "").ToUpperInvariant(); set => Put(nameof(BranchName), BranchName, value, v => Src with { BranchName = v }); }
        public string ActionKey { get => Src.BillingAction; set => Put(nameof(ActionKey), Src.BillingAction, value, v => Src with { BillingAction = v }); }
        public string ActionText =>
            CRMRSDesktopApp.Couriers.CouriersPage.BillingPicks.FirstOrDefault(p => p.Key == Src.BillingAction)?.Name ?? Src.BillingAction;
        public string CourierYn
        {
            get => string.Equals(Src.CourierYn?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
            set => Put(nameof(CourierYn), CourierYn, value, v => Src with { CourierYn = v });
        }
        private void PutAmount(string field, decimal? old, string value, Func<decimal, DesktopApiClient.RepoSubmissionDto> apply, [CallerMemberName] string? prop = null)
        {
            decimal n = string.IsNullOrWhiteSpace(value) ? 0m : ParseAmt(value) ?? throw new FormatException("Enter a valid amount.");
            if (n == old) return;
            Src = apply(n); Dirty.Add(field); Changed(prop);
        }
        public string GrossText { get => Src.TotalGross?.ToString("0.##") ?? "";
            set => PutAmount("TotalGross", Src.TotalGross, value, v => Src with { TotalGross = v }); }
        public string BillingRepoChargesText { get => Src.BillingRepoCharges?.ToString("0.##") ?? "";
            set => PutAmount("BillingRepoCharges", Src.BillingRepoCharges, value, v => Src with { BillingRepoCharges = v }); }
        public string PercentText
        {
            get => Src.CourierPercent?.ToString("0.##") ?? "";
            set
            {
                var n = ParseAmt(value);
                if (n == Src.CourierPercent) return;
                Src = Src with { CourierPercent = n };
                Dirty.Add("CourierPercent");
                Changed();
            }
        }
        public Visibility HasScreenshot =>
            string.IsNullOrWhiteSpace(Src.ScreenshotUrl) ? Visibility.Collapsed : Visibility.Visible;
        public string ScreenshotUrl => Src.ScreenshotUrl;

        public decimal? RepoCharges => _repo;
        public decimal? Advance => _adv;
        public string ChassisNo { get => Src.ChassisNo; set => Put(nameof(ChassisNo), Src.ChassisNo, value, v => Src with { ChassisNo = v }); }
        public string EngineNo { get => Src.EngineNo; set => Put(nameof(EngineNo), Src.EngineNo, value, v => Src with { EngineNo = v }); }
        public string LoanNo { get => Src.LoanNo; set => Put(nameof(LoanNo), Src.LoanNo, value, v => Src with { LoanNo = v }); }
        public string Model { get => Src.Model; set => Put(nameof(Model), Src.Model, value, v => Src with { Model = v }); }
        public string ParkingYardName { get => Src.ParkingYardName; set => Put(nameof(ParkingYardName), Src.ParkingYardName, value, v => Src with { ParkingYardName = v }); }
        public string CollectionUpdate { get => Src.CollectionUpdate; set => Put(nameof(CollectionUpdate), Src.CollectionUpdate, value, v => Src with { CollectionUpdate = v }); }
        public string BillingRemark { get => Src.BillingRemark ?? ""; set => Put(nameof(BillingRemark), Src.BillingRemark, value, v => Src with { BillingRemark = v }); }
        public string InventoryRemark { get => Src.InventoryRemark ?? ""; set => Put(nameof(InventoryRemark), Src.InventoryRemark, value, v => Src with { InventoryRemark = v }); }
        public string AccountsRemark { get => Src.AccountsRemark ?? ""; set => Put("Payment", Src.AccountsRemark, value, v => Src with { AccountsRemark = v }); }
        public string Remark { get => Src.Remark; set => Put(nameof(Remark), Src.Remark, value, v => Src with { Remark = v }); }
        public string AddlAmountText { get => Src.AddlChargesAmount?.ToString("0.##") ?? ""; set => PutAmount("AddlChargesAmount", Src.AddlChargesAmount, value, v => Src with { AddlChargesAmount = v }); }
        public string UtrNo { get => Src.UtrNo; set => Put("Payment", Src.UtrNo, value, v => Src with { UtrNo = v }); }
        public string InvoiceNo => Src.InvoiceNo ?? "";
        public string PaymentDate { get => Src.PaymentDate; set => Put("Payment", Src.PaymentDate, value, v => Src with { PaymentDate = v }); }
        public string PaymentStatusText
        {
            get => (Src.PaymentStatus ?? "").Trim().ToLowerInvariant() switch
            {
                "paid"   => "PAID",
                "unpaid" => "UNPAID",
                _        => ""
            };
            set => Put("Payment", PaymentStatusText, value, v => Src with { PaymentStatus = v.ToLowerInvariant() });
        }
        public decimal CashAmount => Src.CashAmount ?? 0m;
        public string CashText
        {
            get => Src.CashAmount?.ToString("0.##") ?? "";
            set { var n = ParseAmt(value) ?? 0m; if (n == Src.CashAmount) return;
                Src = Src with { CashAmount = n }; Dirty.Add("CashAmount"); Changed(); Changed(nameof(FinalText)); }
        }

        public string RepoChargesText
        {
            get => _repo?.ToString("0.##") ?? "";
            set
            {
                var n = string.IsNullOrWhiteSpace(value) ? 0m : ParseAmt(value) ?? throw new FormatException("Enter a valid amount.");
                if (n == _repo) return;
                _repo = n; Src = Src with { RepoCharges = n }; Dirty.Add("RepoCharges");
                Changed(nameof(RepoChargesText)); Changed(nameof(FinalText));
            }
        }
        public string AdvanceText
        {
            get => _adv?.ToString("0.##") ?? "";
            set
            {
                var n = ParseAmt(value) ?? 0m;
                if (n == _adv) return;
                _adv = n; Src = Src with { Advance = n }; Dirty.Add("Advance");
                Changed(nameof(AdvanceText)); Changed(nameof(FinalText));
            }
        }
        public string FinalText => ((_repo ?? 0m) - (_adv ?? 0m) - CashAmount).ToString("0.##");

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private static decimal? ParseAmt(string? s)
        => decimal.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private async System.Threading.Tasks.Task LoadAsync()
    {
        txtStatus.Text = "Loading…";
        try
        {
            string? from = dpFrom.SelectedDate?.ToString("yyyy-MM-dd");
            string? to   = dpTo.SelectedDate?.ToString("yyyy-MM-dd");
            var data = await DesktopApiClient.GetRepoSubmissionsAsync(from, to, new List<int>(), null);

            data = data.Where(d =>
                d.BillingAction is "hold" or "collection_done"
                || d.BillingAction == "immediate"
            ).ToList();

            var wanted = _statusPicks.Where(p => p.IsChecked).Select(p => p.Key)
                                     .ToHashSet(StringComparer.Ordinal);
            if (wanted.Count > 0)
            {
                bool wantBilled = wanted.Contains("billed");
                var actions = wanted.Where(k => k != "billed").ToHashSet(StringComparer.Ordinal);
                data = data.Where(d =>
                    (wantBilled && d.BillStatus == "billed") ||
                    (actions.Count > 0 && actions.Contains(d.BillingAction ?? ""))).ToList();
            }

            var selectedId = _selected?.Id;
            _all = data.Select(AcctRow.From).ToList();
            RefreshAgentList();
            ApplyFilter();
            if (selectedId.HasValue) grid.SelectedItem = _shown.FirstOrDefault(r => r.Id == selectedId.Value);
        }
        catch (Exception ex) { txtStatus.Text = "Failed: " + ex.Message; }
    }

    // Populate the agent dropdown with the distinct agent names, preserving what
    // the user has typed so the search box isn't disturbed on reload.
    private void RefreshAgentList()
    {
        var keepFin = cmbFinance.Text;
        cmbFinance.ItemsSource = _all.Select(r => (r.Src.FinanceName ?? "").Trim())
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        cmbFinance.Text = keepFin;

        var names = _all.Select(r => (r.AgentName ?? "").Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ticked = new HashSet<string>(
            _agentPicks.Where(a => a.IsChecked).Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        _agentPicks = names.Select(a => new AgentPick { Name = a, IsChecked = ticked.Contains(a) }).ToList();
        ShowAgentPicks();
        UpdateAgentButton();

        var keepEdit = cmbEditAgent.Text;
        cmbEditAgent.ItemsSource = names;
        cmbEditAgent.Text = keepEdit;
    }

    private static List<(string, string, bool)> FieldsOf(AcctRow r) => new()
    {
        ("Vehicle No",       r.VehicleNo,        false),
        ("Chassis No",       r.Src.ChassisNo,    false),
        ("Engine No",        r.Src.EngineNo,     false),
        ("Customer",         r.CustomerName,     false),
        ("Loan No",          r.Src.LoanNo,       false),
        ("Model / Maker",    r.Src.Model,        false),
        ("Finance",          r.FinanceName,      false),
        ("Branch",           r.BranchName,       false),
        ("Agent",            r.AgentName,        false),
        ("Parking Yard",     r.Src.ParkingYardName, false),
        ("Repo Date",        r.RepoDate,         false),
        ("Status",           r.ActionText,       false),
        ("Invoice No",       r.InvoiceNo,        false),
        ("Gross",            r.GrossText,        false),
        ("Percentage",       r.PercentText,      false),
        ("Repo Charges",     r.RepoChargesText,  false),
        ("Advance",          r.AdvanceText,      false),
        ("Cash Collected",   r.CashText,         false),
        ("Final Amount",     r.FinalText,        false),
        ("Payment",          r.PaymentStatusText, false),
        ("UTR No",           r.UtrNo,            false),
        ("Payment Date",     r.Src.PaymentDate,  false),
        ("Collection Update", r.Src.CollectionUpdate, true),
        ("Remark",           r.Src.Remark,       true),
        ("Accounts Remark",  r.Src.AccountsRemark, true),
    };

    private bool _popupOpen;

    private void OpenRecordPopup(AcctRow r)
    {
        if (_popupOpen) return;
        _popupOpen = true;
        try
        {
            var win = new CRMRSDesktopApp.Couriers.CourierRecordWindow(
                r.VehicleNo,
                string.Join("  •  ", new[] { r.CustomerName, r.FinanceName, r.RepoDate }
                    .Where(x => !string.IsNullOrWhiteSpace(x))),
                FieldsOf(r))
            {
                Owner = Window.GetWindow(this),
                Title = "Accounts Record",
            };
            win.ShowDialog();
        }
        finally { _popupOpen = false; }
    }

    private static DataGridRow? RowUnder(object? originalSource)
    {
        var src = originalSource as System.Windows.DependencyObject;
        while (src != null && src is not DataGridRow &&
               src is not System.Windows.Controls.Primitives.DataGridColumnHeader)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        return src as DataGridRow;
    }

    private void Grid_CellClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => CRMRSDesktopApp.Couriers.CouriersPage.BeginEditOnClick(grid, e);

    private void Pick_Changed(object sender, SelectionChangedEventArgs e)
        => CRMRSDesktopApp.Couriers.CouriersPage.CommitPick(grid, sender);

    private void OpenRecord_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRow() is { } r) OpenRecordPopup(r);
    }

    private AcctRow? CurrentRow() => grid.CurrentItem as AcctRow ?? grid.SelectedItem as AcctRow;

    private DataGridColumn? CurrentColumn() =>
        grid.CurrentColumn ?? (grid.SelectedCells.Count > 0 ? grid.SelectedCells[0].Column : null);

    private List<AcctRow> SelectedRows()
    {
        var rows = grid.SelectedItems.OfType<AcctRow>().ToList();
        if (rows.Count == 0 && CurrentRow() is { } one) rows.Add(one);
        var order = _shown.ToList();
        return rows.OrderBy(r => order.IndexOf(r)).ToList();
    }

    private static string CellText(DataGridColumn col, object item)
    {
        if (col is DataGridBoundColumn { Binding: System.Windows.Data.Binding b } &&
            !string.IsNullOrEmpty(b.Path?.Path))
        {
            var prop = item.GetType().GetProperty(b.Path.Path);
            if (prop != null) return prop.GetValue(item)?.ToString() ?? "";
        }
        return "";
    }

    private static string Flat(string? v) =>
        (v ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static void ToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy:\n{ex.Message}", "Copy",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private List<DataGridColumn> OrderedColumns() =>
        grid.Columns.Where(c => c.Visibility == Visibility.Visible)
                    .OrderBy(c => c.DisplayIndex).ToList();

    private string BuildBlock(IEnumerable<AcctRow> rows, List<DataGridColumn> cols, bool headers)
    {
        var sb = new System.Text.StringBuilder();
        if (headers) sb.AppendLine(string.Join("\t", cols.Select(c => Flat(c.Header?.ToString()))));
        foreach (var r in rows)
            sb.AppendLine(string.Join("\t", cols.Select(c => Flat(CellText(c, r)))));
        return sb.ToString();
    }

    private void CopyCell_Click(object sender, RoutedEventArgs e)
    {
        var col = CurrentColumn();
        if (CurrentRow() is { } r && col != null) ToClipboard(Flat(CellText(col, r)));
    }

    private void CopySelection_Click(object sender, RoutedEventArgs e) => CopySelected(false);
    private void CopySelectionHdr_Click(object sender, RoutedEventArgs e) => CopySelected(true);

    private void CopySelected(bool headers)
    {
        var rows = SelectedRows();
        if (rows.Count > 0) ToClipboard(BuildBlock(rows, OrderedColumns(), headers));
    }

    private void CopyColumn_Click(object sender, RoutedEventArgs e)
    {
        var col = CurrentColumn();
        if (col == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Flat(col.Header?.ToString()));
        foreach (var r in _shown) sb.AppendLine(Flat(CellText(col, r)));
        ToClipboard(sb.ToString());
    }

    private void CopyTable_Click(object sender, RoutedEventArgs e)
        => ToClipboard(BuildBlock(_shown, OrderedColumns(), true));

    private void Grid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.C ||
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
        e.Handled = true;
        CopySelected((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0);
    }

    private void Grid_PreviewRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as System.Windows.DependencyObject;
        while (src != null && src is not DataGridCell) src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        if (src is not DataGridCell cell || cell.DataContext is not AcctRow r) return;
        grid.CurrentCell = new DataGridCellInfo(r, cell.Column);
        if (!grid.SelectedItems.OfType<AcctRow>().Contains(r)) grid.SelectedItem = r;
    }

    private void SetAgentEditing(bool on)
    {
        cmbEditAgent.IsEnabled  = true;
        btnAgentEdit.Visibility   = Visibility.Collapsed;
        btnAgentSave.Visibility   = on ? Visibility.Visible   : Visibility.Collapsed;
        btnAgentCancel.Visibility = on ? Visibility.Visible   : Visibility.Collapsed;
    }

    private void btnAgentEdit_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not AcctRow) return;
        SetAgentEditing(true);
        cmbEditAgent.Focus();
    }

    private void btnAgentCancel_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is AcctRow r) cmbEditAgent.Text = r.AgentName;
        SetAgentEditing(false);
    }

    private async void btnAgentSave_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not AcctRow r) return;
        var agent = (cmbEditAgent.Text ?? "").Trim();
        if (agent.Length == 0)
        {
            txtPayMsg.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtPayMsg.Text = "Agent name cannot be blank.";
            return;
        }
        if (string.Equals(agent, (r.AgentName ?? "").Trim(), StringComparison.Ordinal))
        {
            SetAgentEditing(false);
            return;
        }
        try
        {
            btnAgentSave.IsEnabled = false;
            txtPayMsg.Foreground = System.Windows.Media.Brushes.Gray;
            txtPayMsg.Text = "Saving agent name…";
            await DesktopApiClient.UpdateSubmissionFieldsAsync(r.Id, new { AgentName = agent });
            txtPayMsg.Foreground = System.Windows.Media.Brushes.Green;
            txtPayMsg.Text = "Agent name saved.";
            SetAgentEditing(false);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            txtPayMsg.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtPayMsg.Text = "Failed: " + ex.Message;
        }
        finally { btnAgentSave.IsEnabled = true; }
    }

    private void ApplyFilter()
    {
        var picked = new HashSet<string>(
            _agentPicks.Where(a => a.IsChecked).Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        List<AcctRow> rows = picked.Count == 0
            ? _all
            : _all.Where(r => picked.Contains((r.AgentName ?? "").Trim())).ToList();

        var fin = (cmbFinance.Text ?? "").Trim();
        if (fin.Length > 0)
        {
            var exactFin = rows.Where(r => string.Equals((r.Src.FinanceName ?? "").Trim(), fin, StringComparison.OrdinalIgnoreCase)).ToList();
            rows = exactFin.Count > 0
                ? exactFin
                : rows.Where(r => CRMRSDesktopApp.Billing.ViewAllDetailsWindow.NameMatches(r.Src.FinanceName, fin)).ToList();
        }

        if (cmbInventory.SelectedIndex > 0)
        {
            var want = cmbInventory.SelectedIndex == 1 ? "Yes" : "No";
            rows = rows.Where(r => string.Equals(
                string.IsNullOrWhiteSpace(r.Src.CourierYn) ? "No" : r.Src.CourierYn.Trim(),
                want, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var last4 = Squash4(txtRcLast4?.Text);
        if (last4.Length > 0)
            rows = rows.Where(r => Squash4(r.VehicleNo).Contains(last4) ||
                                   Squash4(r.Src.ChassisNo).Contains(last4)).ToList();

        _shown.Clear();
        foreach (var r in rows) _shown.Add(r);
        txtStatus.Text = $"{rows.Count} record(s).";
        BuildSummary(rows);
    }


    private void Finance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(new Action(ApplyFilter), System.Windows.Threading.DispatcherPriority.Input);
    }
    private void Finance_Key(object sender, System.Windows.Input.KeyEventArgs e) { if (_ready) ApplyFilter(); }
    private void btnClearFinance_Click(object sender, RoutedEventArgs e) { cmbFinance.Text = ""; if (_ready) ApplyFilter(); }
    private void Inventory_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) ApplyFilter(); }
    private void RcLast4_Changed(object sender, TextChangedEventArgs e) { if (_ready) ApplyFilter(); }
    private void btnClearAgent_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in _agentPicks) a.IsChecked = false;
        txtAgentSearch.Text = "";
        ShowAgentPicks();
        UpdateAgentButton();
        if (_ready) ApplyFilter();
    }

    private async void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        long keepId = _selected?.Id ?? 0L;
        btnRefresh.IsEnabled = false;
        try
        {
            await LoadAsync();
            if (keepId > 0)
            {
                var again = _shown.FirstOrDefault(x => x.Id == keepId);
                if (again != null) { grid.SelectedItem = again; grid.ScrollIntoView(again); }
            }
        }
        finally { btnRefresh.IsEnabled = true; }
    }


    private void BuildSummary(List<AcctRow> rows)
    {
        txtGrandVehicles.Text = $"Vehicles: {rows.Count}";
        txtGrandRepo.Text  = "Total Seizing Charges: " + rows.Sum(x => x.RepoCharges ?? 0m).ToString("0.##");
        decimal cashTot = rows.Sum(x => x.CashAmount);
        decimal finalTot = rows.Sum(x => (x.RepoCharges ?? 0m) - (x.Advance ?? 0m) - x.CashAmount);
        txtGrandCash.Text = "Total Cash Collected: " + cashTot.ToString("0.##");
        txtGrandFinal.Text = (finalTot < 0m ? "Recoverable from agent: " : "Total Final Amount: ") + finalTot.ToString("0.##");
        txtGrandFinal.Foreground = finalTot < 0m
            ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1565C0")!;
    }

    private async void Reload_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) await LoadAsync();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) ApplyFilter();
    }

    private void grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (!EnterOnlyGridSave.AllowCommit(grid, e)) return;
        if (e.Row.Item is not AcctRow r) return;
        Dispatcher.BeginInvoke(new Action(async () => await SaveRow(r)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private async System.Threading.Tasks.Task SaveRow(AcctRow r)
    {
        if (r.Dirty.Count == 0) return;
        var snapshot = r.Src;
        var dirty = r.Dirty.ToList();
        r.Dirty.Clear();
        txtStatus.Text = "Saving…";
        try
        {
            var courier = new Dictionary<string, object?>();
            if (dirty.Remove("RepoCharges"))    courier["RepoCharges"] = r.RepoCharges;
            if (dirty.Remove("Advance"))        courier["Advance"] = r.Advance;
            if (dirty.Remove("CourierPercent")) courier["CourierPercent"] = snapshot.CourierPercent;
            if (courier.Count > 0) await DesktopApiClient.UpdateCourierSubmissionAsync(r.Id, courier);

            if (dirty.Remove("Payment"))
                await DesktopApiClient.UpdateAccountsPaymentAsync(r.Id, new
                {
                    UtrNo = snapshot.UtrNo ?? "",
                    PaymentDate = DateTime.TryParse(snapshot.PaymentDate, out var pd) ? pd.ToString("yyyy-MM-dd") : null,
                    PaymentStatus = snapshot.PaymentStatus ?? "",
                    AccountsRemark = snapshot.AccountsRemark ?? ""
                });

            await CRMRSDesktopApp.Couriers.CouriersPage.SaveFields(r.Id, snapshot, dirty);
            txtStatus.Text = "Saved.";
            BuildSummary(_shown.ToList());
            if (ReferenceEquals(r, _selected)) ShowPanel(r);
        }
        catch (Exception ex)
        {
            await LoadAsync();
            txtStatus.Text = "Save failed: " + ex.Message;
        }
    }

    private AcctRow? _selected;

    private void grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = grid.SelectedItem as AcctRow;
        txtPayMsg.Text = "";
        if (_selected is not { } r)
        {
            pnlPay.IsEnabled = false;
            txtPaySel.Text = "Click a vehicle row to enter its payment details.";
            return;
        }
        ShowPanel(r);
    }

    private bool _loadingPanel;

    private void ShowPanel(AcctRow r)
    {
        _loadingPanel = true;
        var veh = string.IsNullOrWhiteSpace(r.VehicleNo) ? r.Src.ChassisNo : r.VehicleNo;
        txtPaySel.Text = $"{veh}  •  {r.CustomerName}  •  Agent: {r.AgentName}";
        cmbEditAgent.Text = r.AgentName;
        SetAgentEditing(false);
        txtUtr.Text = r.Src.UtrNo;
        dpPayDate.SelectedDate = DateTime.TryParse(r.Src.PaymentDate, out var d) ? d : (DateTime?)null;
        cmbPayStatus.SelectedIndex = (r.Src.PaymentStatus ?? "").Trim().ToLowerInvariant() switch
        {
            "paid"   => 1,
            "unpaid" => 2,
            _        => 0
        };
        txtAcRemark.Text = r.Src.AccountsRemark;
        LoadCharges(r);
        pnlPay.IsEnabled = true;
        _loadingPanel = false;
    }

    private bool _suppressAcCalc;

    private void LoadCharges(AcctRow r)
    {
        var vis  = Visibility.Visible;

        txtChargesHead.Text = "CHARGES — " + r.ActionText.ToUpperInvariant();
        txtChargesMsg.Text = "";

        lblGross.Visibility    = vis;
        txtAcGross.Visibility  = vis;
        lblAcPercent.Visibility   = vis;
        txtAcPercent.Visibility   = vis;

        lblAcAddl.Visibility   = vis;
        txtAcAddl.Visibility   = vis;

        _suppressAcCalc = true;
        txtAcGross.Text   = r.Src.TotalGross?.ToString("0.##") ?? "";
        txtAcAddl.Text    = r.AddlAmountText;
        txtAcBillingRepo.Text = r.BillingRepoChargesText;
        txtAcPercent.Text = r.Src.CourierPercent?.ToString("0.##") ?? "";
        txtAcRepo.Text    = r.RepoCharges?.ToString("0.##") ?? "";
        txtAcAdvance.Text = r.Advance?.ToString("0.##") ?? "";
        txtAcCash.Text    = r.CashAmount == 0m ? "" : r.CashAmount.ToString("0.##");
        _suppressAcCalc = false;

        lblAcCash.Visibility = vis;
        txtAcCash.Visibility = vis;

        UpdateAcFinal();
        LoadAcScreenshot(r.Src.ScreenshotUrl);
    }

    private string? _acShotUrl;

    private async void LoadAcScreenshot(string? url)
    {
        _acShotUrl = url;
        if (string.IsNullOrWhiteSpace(url))
        {
            lblAcShot.Visibility = Visibility.Collapsed;
            pnlAcShot.Visibility = Visibility.Collapsed;
            imgAcShot.Source = null;
            return;
        }
        lblAcShot.Visibility = Visibility.Visible;
        pnlAcShot.Visibility = Visibility.Visible;
        try
        {
            var bytes = await App.HttpClient.GetByteArrayAsync(url);
            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            imgAcShot.Source = bmp;
        }
        catch { imgAcShot.Source = null; }
    }

    private void imgAcShot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_acShotUrl))
            try { Process.Start(new ProcessStartInfo(_acShotUrl) { UseShellExecute = true }); } catch { }
    }

    private static string JoinAddl(string? notes, decimal? amount)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(notes)) parts.Add(notes!.Trim());
        if (amount.HasValue && amount.Value != 0m) parts.Add(amount.Value.ToString("0.##"));
        return string.Join(", ", parts);
    }

    private void AcCalc_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressAcCalc) return;
        if (ReferenceEquals(sender, txtAcPercent))
        {
            var gross = ParseAmt(txtAcGross.Text);
            var pct   = ParseAmt(txtAcPercent.Text);
            if (gross.HasValue && pct.HasValue)
            {
                _suppressAcCalc = true;
                txtAcRepo.Text = (gross.Value * pct.Value / 100m).ToString("0.##");
                _suppressAcCalc = false;
            }
        }
        UpdateAcFinal();
    }

    private void UpdateAcFinal()
    {
        decimal repo = ParseAmt(txtAcRepo.Text) ?? 0m;
        decimal adv  = ParseAmt(txtAcAdvance.Text) ?? 0m;
        decimal cash = ParseAmt(txtAcCash.Text) ?? 0m;
        decimal net  = repo - adv - cash;
        txtAcFinal.Text = cash > 0m
            ? $"Final Amount: {repo:0.##} − {adv:0.##} − {cash:0.##} cash = {net:0.##}"
            : $"Final Amount: {repo:0.##} − {adv:0.##} = {net:0.##}";
        txtAcFinal.Foreground = net < 0m
            ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1565C0")!;
    }

    private async void Panel_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || _selected is not { } r) return;
        e.Handled = true;
        try
        {
        if (e.OriginalSource == txtAcAdvance) r.AdvanceText = txtAcAdvance.Text;
        else if (e.OriginalSource == txtAcCash) r.CashText = txtAcCash.Text;
        else if (e.OriginalSource == txtAcRepo) r.RepoChargesText = txtAcRepo.Text;
        else if (e.OriginalSource == txtAcGross) r.GrossText = txtAcGross.Text;
        else if (e.OriginalSource == txtAcAddl) r.AddlAmountText = txtAcAddl.Text;
        else if (e.OriginalSource == txtAcBillingRepo) r.BillingRepoChargesText = txtAcBillingRepo.Text;
        else if (e.OriginalSource == txtAcPercent)
        {
            r.PercentText = txtAcPercent.Text;
            var percent = ParseAmt(txtAcPercent.Text) ?? 0m;
            r.RepoChargesText = ((r.Src.TotalGross ?? 0m) * percent / 100m).ToString("0.##");
        }
        else if (e.OriginalSource == txtAcRemark) r.AccountsRemark = txtAcRemark.Text;
        else if (e.OriginalSource == txtUtr) r.UtrNo = txtUtr.Text;
        else if (cmbEditAgent.IsKeyboardFocusWithin) r.AgentName = cmbEditAgent.Text;
        else if (dpPayDate.IsKeyboardFocusWithin)
        {
            var dateText = (e.OriginalSource as TextBox)?.Text ?? dpPayDate.Text;
            if (!DateTime.TryParse(dateText, out var date)) throw new FormatException("Enter a valid payment date.");
            r.PaymentDate = date.ToString("yyyy-MM-dd");
        }
        else return;
        await SaveRow(r);
        }
        catch (Exception ex) { txtStatus.Text = "Save failed: " + ex.Message; }
    }

    private async void AgentSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPanel || !_ready || _selected is not { } r || !cmbEditAgent.IsDropDownOpen || cmbEditAgent.SelectedItem is not string agent) return;
        r.AgentName = agent;
        await SaveRow(r);
    }

    private async void PaymentDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPanel || !_ready || _selected is not { } r || !dpPayDate.IsDropDownOpen) return;
        r.PaymentDate = dpPayDate.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
        await SaveRow(r);
    }

    private async void PaymentStatus_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPanel || !_ready || _selected is not { } r || !cmbPayStatus.IsKeyboardFocusWithin) return;
        r.PaymentStatusText = cmbPayStatus.SelectedIndex == 1 ? "PAID" : "UNPAID";
        await SaveRow(r);
    }

    private async void btnSaveCharges_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } r) return;
        btnSaveCharges.IsEnabled = false;
        txtChargesMsg.Foreground = System.Windows.Media.Brushes.Gray;
        txtChargesMsg.Text = "Saving…";
        try
        {
            r.RepoChargesText = txtAcRepo.Text;
            r.PercentText = txtAcPercent.Text;
            r.GrossText = txtAcGross.Text;
            r.AddlAmountText = txtAcAddl.Text;
            r.BillingRepoChargesText = txtAcBillingRepo.Text;
            r.AdvanceText = txtAcAdvance.Text;
            r.CashText = txtAcCash.Text;
            await SaveRow(r);
            long keepId = r.Id;
            await LoadAsync();
            var again = _shown.FirstOrDefault(x => x.Id == keepId);
            if (again != null) { grid.SelectedItem = again; grid.ScrollIntoView(again); }
            txtChargesMsg.Foreground = System.Windows.Media.Brushes.Green;
            txtChargesMsg.Text = "Charges saved.";
        }
        catch (Exception ex)
        {
            txtChargesMsg.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtChargesMsg.Text = "Save failed: " + ex.Message;
        }
        finally { btnSaveCharges.IsEnabled = true; }
    }

    private async void btnSavePay_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } r) return;
        if (cmbPayStatus.SelectedIndex <= 0)
        {
            txtPayMsg.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtPayMsg.Text = "Choose Paid or Unpaid before saving.";
            cmbPayStatus.Focus();
            return;
        }
        btnSavePay.IsEnabled = false;
        txtPayMsg.Foreground = System.Windows.Media.Brushes.Gray;
        txtPayMsg.Text = "Saving…";
        try
        {
            await DesktopApiClient.UpdateAccountsPaymentAsync(r.Id, new
            {
                UtrNo = txtUtr.Text.Trim(),
                PaymentDate = dpPayDate.SelectedDate?.ToString("yyyy-MM-dd"),
                PaymentStatus = cmbPayStatus.SelectedIndex == 1 ? "paid" : "unpaid",
                AccountsRemark = txtAcRemark.Text.Trim()
            });
            long keepId = r.Id;
            await LoadAsync();
            var again = _shown.FirstOrDefault(x => x.Id == keepId);
            if (again != null) { grid.SelectedItem = again; grid.ScrollIntoView(again); }
            txtPayMsg.Foreground = System.Windows.Media.Brushes.Green;
            txtPayMsg.Text = "Saved.";
        }
        catch (Exception ex)
        {
            txtPayMsg.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtPayMsg.Text = "Save failed: " + ex.Message;
        }
        finally { btnSavePay.IsEnabled = true; }
    }

    private void ViewScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not AcctRow r) return;
        var urls = (r.Src.ScreenshotUrls ?? new List<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        if (urls.Count == 0 && !string.IsNullOrWhiteSpace(r.ScreenshotUrl)) urls.Add(r.ScreenshotUrl);
        foreach (var u in urls)
            try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { }
    }

    private async void btnAgentBill_Click(object sender, RoutedEventArgs e)
    {
        var sel = grid.SelectedItem as AcctRow ?? _selected;
        string agent = (sel?.AgentName ?? "").Trim();
        if (agent.Length == 0) agent = PickedAgentName().Trim();
        if (agent.Length == 0)
        {
            var agents = _shown.Select(r => (r.AgentName ?? "").Trim())
                .Where(a => a.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (agents.Count == 1) agent = agents[0];
        }
        if (agent.Length == 0)
        {
            MessageBox.Show(
                "Click any vehicle row (or type an agent name) to choose the agent, " +
                "then Generate Agent Bill — it bills all that agent's vehicles for the selected dates.",
                "Agent Bill", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await LoadAsync();
        var rows = _all
            .Where(r => string.Equals((r.AgentName ?? "").Trim(), agent, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (rows.Count == 0) { txtStatus.Text = "No records to bill for " + agent + "."; return; }

        var w = new AgentBillWindow(agent, rows.Select(r => r.Src).ToList())
        { Owner = Window.GetWindow(this) };
        w.ShowDialog();
    }
}
