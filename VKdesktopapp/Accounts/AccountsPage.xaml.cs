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

    // "billed" lives in bill_status rather than billing_action, so it is
    // matched separately when the filter runs.
    private readonly List<StatusPick> _statusPicks = new()
    {
        new StatusPick { Name = "OK for billing",      Key = "immediate" },
        new StatusPick { Name = "Hold for collection", Key = "hold" },
        new StatusPick { Name = "Collection done",     Key = "collection_done" },
        new StatusPick { Name = "Cancel",              Key = "cancel" },
        new StatusPick { Name = "Billing Done",        Key = "billed" },
    };

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
        lstStatusPicks.ItemsSource = _statusPicks;
        UpdateStatusButton();
        dpFrom.DisplayDateEnd = DateTime.Today;
        dpTo.DisplayDateEnd = DateTime.Today;
        dpPayDate.DisplayDateEnd = DateTime.Today;
        dpFrom.SelectedDate = DateTime.Today.AddDays(-30);
        dpTo.SelectedDate = DateTime.Today;
        Loaded += async (_, __) => { _ready = true; await LoadAsync(); };
    }

    internal class AcctRow : INotifyPropertyChanged
    {
        internal DesktopApiClient.RepoSubmissionDto Src { get; init; } = null!;
        private decimal? _repo;
        private decimal? _adv;

        internal static AcctRow From(DesktopApiClient.RepoSubmissionDto d) =>
            new() { Src = d, _repo = d.RepoCharges, _adv = d.Advance };

        public long Id => Src.Id;
        public string RepoDate => Src.CreatedAt;
        public string AgentName
        {
            get
            {
                var a = (Src.AgentName ?? "").Trim();
                return a.Length > 0 ? a : (Src.SubmittedByName ?? "").Trim();
            }
        }
        public string VehicleNo => string.IsNullOrWhiteSpace(Src.VehicleNo) ? Src.ChassisNo : Src.VehicleNo;
        public string CustomerName => Src.CustomerName;
        public string FinanceName => (Src.FinanceName ?? "").ToUpperInvariant();
        public string BranchName => (Src.BranchName ?? "").ToUpperInvariant();
        public string ActionText => Src.BillingAction switch
        {
            "immediate"       => "OK for billing",
            "hold"            => "Hold for collection",
            "collection_done" => "Collection done",
            "cancel"          => "Cancel",
            _                 => Src.BillingAction
        };
        public string GrossText => Src.TotalGross?.ToString("0.##") ?? "";
        public string PercentText => Src.CourierPercent?.ToString("0.##") ?? "";
        public Visibility HasScreenshot =>
            string.IsNullOrWhiteSpace(Src.ScreenshotUrl) ? Visibility.Collapsed : Visibility.Visible;
        public string ScreenshotUrl => Src.ScreenshotUrl;

        public decimal? RepoCharges => _repo;
        public decimal? Advance => _adv;
        public string ChassisNo => Src.ChassisNo;
        public string LoanNo => Src.LoanNo;
        public string Model => Src.Model;
        public string ParkingYardName => Src.ParkingYardName;
        public string CollectionUpdate => Src.CollectionUpdate;
        public string Remark => Src.Remark;
        public string UtrNo => Src.UtrNo;
        public string InvoiceNo => Src.InvoiceNo ?? "";
        public string PaymentDate => Src.PaymentDate;
        public string PaymentStatusText => (Src.PaymentStatus ?? "").Trim().ToLowerInvariant() switch
        {
            "paid"   => "PAID",
            "unpaid" => "UNPAID",
            _        => ""
        };
        public decimal CashAmount => Src.CashAmount ?? 0m;
        public string CashText => (Src.CashAmount ?? 0m) == 0m ? "" : (Src.CashAmount ?? 0m).ToString("0.##");

        public string RepoChargesText
        {
            get => _repo?.ToString("0.##") ?? "";
            set { _repo = ParseAmt(value); Changed(nameof(RepoChargesText)); Changed(nameof(FinalText)); }
        }
        public string AdvanceText
        {
            get => _adv?.ToString("0.##") ?? "";
            set { _adv = ParseAmt(value); Changed(nameof(AdvanceText)); Changed(nameof(FinalText)); }
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
                || (d.BillingAction == "immediate"
                    && string.Equals(d.CourierYn, "Yes", StringComparison.OrdinalIgnoreCase))
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

            _all = data.Select(AcctRow.From).ToList();
            RefreshAgentList();
            ApplyFilter();
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

        var keep = cmbAgent.Text;
        var names = _all.Select(r => (r.AgentName ?? "").Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();
        cmbAgent.ItemsSource = names;
        cmbAgent.Text = keep;

        var keepEdit = cmbEditAgent.Text;
        cmbEditAgent.ItemsSource = names;
        cmbEditAgent.Text = keepEdit;
    }

    // ── Full record popup, on double click only ─────────────────────────────
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

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RowUnder(e.OriginalSource)?.Item is not AcctRow r) return;
        e.Handled = true;
        OpenRecordPopup(r);
    }

    private void OpenRecord_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRow() is { } r) OpenRecordPopup(r);
    }

    // ── Excel-style copying ─────────────────────────────────────────────────
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

    /// Tabs and newlines inside a value would break the grid Excel reads, so
    /// they are flattened on the way to the clipboard.
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

    // Right-clicking a cell aims the copy at that cell without discarding the
    // rows already selected.
    private void Grid_PreviewRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as System.Windows.DependencyObject;
        while (src != null && src is not DataGridCell) src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        if (src is not DataGridCell cell || cell.DataContext is not AcctRow r) return;
        grid.CurrentCell = new DataGridCellInfo(r, cell.Column);
        if (!grid.SelectedItems.OfType<AcctRow>().Contains(r)) grid.SelectedItem = r;
    }

    // ── Agent name, edited in place ─────────────────────────────────────────
    private void SetAgentEditing(bool on)
    {
        cmbEditAgent.IsEnabled  = on;
        btnAgentEdit.Visibility   = on ? Visibility.Collapsed : Visibility.Visible;
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
        var term = (cmbAgent.Text ?? "").Trim();
        List<AcctRow> rows;
        if (term.Length == 0)
        {
            rows = _all;
        }
        else
        {
            // Prefer an exact agent match (so "J" doesn't also pull in "RAJA RAM");
            // fall back to contains for free-text discovery.
            var exact = _all.Where(r => string.Equals((r.AgentName ?? "").Trim(), term, StringComparison.OrdinalIgnoreCase)).ToList();
            rows = exact.Count > 0
                ? exact
                : _all.Where(r => CRMRSDesktopApp.Billing.ViewAllDetailsWindow.NameMatches(r.AgentName, term)).ToList();
        }

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

    private void Agent_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(new Action(ApplyFilter), System.Windows.Threading.DispatcherPriority.Input);
    }
    private void Agent_Key(object sender, System.Windows.Input.KeyEventArgs e) { if (_ready) ApplyFilter(); }

    private void Finance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(new Action(ApplyFilter), System.Windows.Threading.DispatcherPriority.Input);
    }
    private void Finance_Key(object sender, System.Windows.Input.KeyEventArgs e) { if (_ready) ApplyFilter(); }
    private void btnClearFinance_Click(object sender, RoutedEventArgs e) { cmbFinance.Text = ""; if (_ready) ApplyFilter(); }
    private void Inventory_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) ApplyFilter(); }
    private void RcLast4_Changed(object sender, TextChangedEventArgs e) { if (_ready) ApplyFilter(); }
    private void btnClearAgent_Click(object sender, RoutedEventArgs e) { cmbAgent.Text = ""; if (_ready) ApplyFilter(); }

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
        txtGrandRepo.Text  = "Total Repo: " + rows.Sum(x => x.RepoCharges ?? 0m).ToString("0.##");
        decimal cashTot = rows.Sum(x => x.CashAmount);
        decimal finalTot = rows.Sum(x => (x.RepoCharges ?? 0m) - (x.Advance ?? 0m) - x.CashAmount);
        txtGrandCash.Text = "Total Cash Collected: " + cashTot.ToString("0.##");
        txtGrandFinal.Text = (finalTot < 0m ? "Recoverable from agent: " : "Total Final: ") + finalTot.ToString("0.##");
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

    private async void grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not AcctRow r) return;
        // Let the binding push the new text into the VM first.
        await Dispatcher.BeginInvoke(new Action(async () => await SaveRow(r)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private async System.Threading.Tasks.Task SaveRow(AcctRow r)
    {
        try
        {
            await DesktopApiClient.UpdateCourierSubmissionAsync(r.Id, new
            {
                RepoCharges = r.RepoCharges,
                Advance = r.Advance
            });
            txtStatus.Text = "Saved.";
            BuildSummary(_shown.ToList());
        }
        catch (Exception ex) { txtStatus.Text = "Save failed: " + ex.Message; }
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
    }

    private bool _suppressAcCalc;

    private void LoadCharges(AcctRow r)
    {
        var vis  = Visibility.Visible;
        var gone = Visibility.Collapsed;
        bool isOk   = r.Src.BillingAction == "immediate";
        bool isHold = r.Src.BillingAction is "hold" or "collection_done";

        txtChargesHead.Text = "CHARGES — " + r.ActionText.ToUpperInvariant();
        txtChargesMsg.Text = "";

        lblGross.Visibility    = isOk ? vis : gone;
        txtAcGross.Visibility  = isOk ? vis : gone;
        lblAcPercent.Visibility   = isOk ? vis : gone;
        txtAcPercent.Visibility   = isOk ? vis : gone;

        var addl = JoinAddl(r.Src.AddlChargesNotes, r.Src.AddlChargesAmount);
        lblAcAddl.Visibility   = isHold ? vis : gone;
        txtAcAddl.Visibility   = isHold ? vis : gone;

        _suppressAcCalc = true;
        txtAcGross.Text   = r.Src.TotalGross?.ToString("0.##") ?? "";
        txtAcAddl.Text    = addl;
        txtAcPercent.Text = r.Src.CourierPercent?.ToString("0.##") ?? "";
        txtAcRepo.Text    = r.RepoCharges?.ToString("0.##") ?? "";
        txtAcAdvance.Text = r.Advance?.ToString("0.##") ?? "";
        txtAcCash.Text    = r.CashAmount == 0m ? "" : r.CashAmount.ToString("0.##");
        _suppressAcCalc = false;

        bool showCash = r.Src.BillingAction == "collection_done" || r.CashAmount > 0m;
        lblAcCash.Visibility = showCash ? vis : gone;
        txtAcCash.Visibility = showCash ? vis : gone;

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
        decimal cash = _selected?.CashAmount ?? 0m;
        decimal net  = repo - adv - cash;
        txtAcFinal.Text = cash > 0m
            ? $"Final: {repo:0.##} − {adv:0.##} − {cash:0.##} cash = {net:0.##}"
            : $"Final: {repo:0.##} − {adv:0.##} = {net:0.##}";
        txtAcFinal.Foreground = net < 0m
            ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1565C0")!;
    }

    private async void btnSaveCharges_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } r) return;
        btnSaveCharges.IsEnabled = false;
        txtChargesMsg.Foreground = System.Windows.Media.Brushes.Gray;
        txtChargesMsg.Text = "Saving…";
        try
        {
            await DesktopApiClient.UpdateCourierSubmissionAsync(r.Id, new
            {
                RepoCharges = ParseAmt(txtAcRepo.Text),
                Advance = ParseAmt(txtAcAdvance.Text),
                CourierPercent = ParseAmt(txtAcPercent.Text)
            });
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
        if (sender is FrameworkElement fe && fe.Tag is AcctRow r && !string.IsNullOrWhiteSpace(r.ScreenshotUrl))
            try { Process.Start(new ProcessStartInfo(r.ScreenshotUrl) { UseShellExecute = true }); } catch { }
    }

    private void btnAgentBill_Click(object sender, RoutedEventArgs e)
    {
        var sel = grid.SelectedItem as AcctRow ?? _selected;
        string agent = (sel?.AgentName ?? "").Trim();
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

        var rows = _all
            .Where(r => string.Equals((r.AgentName ?? "").Trim(), agent, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (rows.Count == 0) { txtStatus.Text = "No records to bill for " + agent + "."; return; }

        var w = new AgentBillWindow(agent, rows.Select(r => r.Src).ToList())
        { Owner = Window.GetWindow(this) };
        w.ShowDialog();
    }
}
