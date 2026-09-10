using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Couriers;

public partial class CouriersPage : Page
{
    private List<Row> _rows = new();

    private class Row
    {
        public DesktopApiClient.RepoSubmissionDto Src { get; set; } = null!;
        public long Id => Src.Id;
        public string RepoDate => Src.CreatedAt;
        public string LoanNo => Src.LoanNo;
        public string InvoiceNo => Src.InvoiceNo;
        public string VehicleNo => Src.VehicleNo;
        public string CustomerName => Src.CustomerName;
        public string BranchName => (Src.BranchName ?? "").ToUpperInvariant();
        public string Model => Src.Model;
        public string ChassisNo => Src.ChassisNo;
        public string EngineNo => Src.EngineNo;
        public string AgentName => Src.AgentName;
        public string ParkingYardName => Src.ParkingYardName;
        public string ParkingYardMobile => Src.ParkingYardMobile;
        public string LoadDetails => Src.LoadDetails;
        public string AddlCharges => JoinParts(Src.AddlChargesNotes, Src.AddlChargesAmount?.ToString("0.##"));
        public string ConfirmationBy => JoinParts(Src.ConfirmationByName, Src.ConfirmationByMobile);
        public string ExecutiveName => Src.ExecutiveName;
        public string CollectionUpdate => Src.CollectionUpdate;
        public string FinanceName => (Src.FinanceName ?? "").ToUpperInvariant();
        public string ActionText => Src.BillingAction switch
        {
            "immediate"       => "OK for billing",
            "hold"            => "Hold for collection",
            "collection_done" => "Collection done",
            "cancel"          => "Cancel",
            _                 => Src.BillingAction
        };
        public string RepoChargesText => Src.RepoCharges?.ToString("0.##") ?? "";
        public string AdvanceText => Src.Advance?.ToString("0.##") ?? "";
        // Nothing saved yet reads as "No" rather than an empty cell, so the
        // column always says where a record stands.
        public string CourierYn =>
            string.Equals(Src.CourierYn?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
        public string InventoryRemark => Src.InventoryRemark ?? "";
        public string BankerAddress => Src.BankerAddress;
        public string PodNumber => Src.PodNumber;

        /// Joins the paired fields the way the app's OK-for-repo message does,
        /// skipping whichever side is blank so no stray comma is left behind.
        private static string JoinParts(params string?[] parts)
            => string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
    }

    private class AdvRow : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public DateTime Date { get; set; }
        public string DateText => Date.ToString("dd-MM-yyyy");
        public string Note { get; set; } = "";

        private string _amountText = "";
        public string AmountText
        {
            get => _amountText;
            set { _amountText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmountText))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<AdvRow> _advances = new();

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

    // "billed" is not a billing_action — it lives in bill_status — so it is
    // matched separately when the filter runs.
    private readonly List<StatusPick> _statusPicks = new()
    {
        new StatusPick { Name = "OK for billing",       Key = "immediate" },
        new StatusPick { Name = "Hold for collection",  Key = "hold" },
        new StatusPick { Name = "Collection done",      Key = "collection_done" },
        new StatusPick { Name = "Cancel",               Key = "cancel" },
        new StatusPick { Name = "Billing Done",         Key = "billed" },
    };

    private bool _ready;

    private static long RowIdOf(object? item) => item is Row r ? r.Id : 0;

    private static string Squash4(string? s) =>
        new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();


    public CouriersPage()
    {
        InitializeComponent();
        dpFrom.DisplayDateEnd = DateTime.Today;
        dpTo.DisplayDateEnd = DateTime.Today;
        dpFrom.SelectedDate = DateTime.Today.AddDays(-7);
        dpTo.SelectedDate = DateTime.Today;
        dpAdvDate.SelectedDate = DateTime.Today;
        lstAdvances.ItemsSource = _advances;
        lstStatusPicks.ItemsSource = _statusPicks;
        UpdateStatusButton();
        Loaded += async (_, __) => { _ready = true; await LoadAsync(); };
    }

    // Instant filtering — no Load button; any filter/date change reloads.
    private async void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        long keepId = (grid.SelectedItem as Row)?.Id ?? 0L;
        btnRefresh.IsEnabled = false;
        try
        {
            await LoadAsync();
            if (keepId > 0)
            {
                var again = _rows.FirstOrDefault(x => x.Id == keepId);
                if (again != null) { grid.SelectedItem = again; grid.ScrollIntoView(again); }
            }
        }
        finally { btnRefresh.IsEnabled = true; }
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) await LoadAsync();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        txtStatus.Text = "Loading…";
        try
        {
            string? from = dpFrom.SelectedDate?.ToString("yyyy-MM-dd");
            string? to   = dpTo.SelectedDate?.ToString("yyyy-MM-dd");

            var data = await DesktopApiClient.GetRepoSubmissionsAsync(from, to, new List<int>(), null);

            var picked = _statusPicks.Where(p => p.IsChecked).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            if (picked.Count > 0)
            {
                bool wantBilled = picked.Contains("billed");
                var actions = picked.Where(k => k != "billed").ToHashSet(StringComparer.Ordinal);
                data = data.Where(d =>
                    (wantBilled && d.BillStatus == "billed") ||
                    (actions.Count > 0 && actions.Contains(d.BillingAction ?? ""))).ToList();
            }

            _rows = data.Select(d => new Row { Src = d }).ToList();
            RefreshFilterLists();
            ApplyFilters();
        }
        catch (Exception ex) { txtStatus.Text = "Failed: " + ex.Message; }
    }

    private void RefreshFilterLists()
    {
        var keepFinance = cmbFinance.Text;
        cmbFinance.ItemsSource = Distinct(_rows.Select(r => r.FinanceName));
        cmbFinance.Text = keepFinance;

        var agents = Distinct(_rows.Select(r => r.AgentName));
        var ticked = new HashSet<string>(
            _agentPicks.Where(a => a.IsChecked).Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        _agentPicks = agents.Select(a => new AgentPick { Name = a, IsChecked = ticked.Contains(a) }).ToList();
        ShowAgentPicks();
        UpdateAgentButton();

        var keepEdit = cmbEditAgent.Text;
        cmbEditAgent.ItemsSource = agents;
        cmbEditAgent.Text = keepEdit;

        static List<string> Distinct(IEnumerable<string?> values) => values
            .Select(v => (v ?? "").Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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
            _ => $"{picked.Count} agents"
        };
    }

    private static List<Row> Narrow(List<Row> rows, string term, Func<Row, string?> field)
    {
        if (term.Length == 0) return rows;
        var exact = rows.Where(r => string.Equals((field(r) ?? "").Trim(), term, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count > 0
            ? exact
            : rows.Where(r => CRMRSDesktopApp.Billing.ViewAllDetailsWindow.NameMatches(field(r), term)).ToList();
    }

    private void ApplyFilters()
    {
        var shown = Narrow(_rows, (cmbFinance.Text ?? "").Trim(), r => r.FinanceName);

        var picked = new HashSet<string>(
            _agentPicks.Where(a => a.IsChecked).Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        if (picked.Count > 0)
            shown = shown.Where(r => picked.Contains((r.AgentName ?? "").Trim())).ToList();

        var last4 = Squash4(txtRcLast4?.Text);
        if (last4.Length > 0)
            shown = shown.Where(r => Squash4(r.VehicleNo).Contains(last4) ||
                                     Squash4(r.ChassisNo).Contains(last4)).ToList();

        grid.ItemsSource = shown;
        txtStatus.Text = shown.Count == _rows.Count
            ? $"{shown.Count} record(s)."
            : $"{shown.Count} of {_rows.Count} record(s).";
    }

    private void RcLast4_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) ApplyFilters();
    }

    private void Finance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(new Action(ApplyFilters), System.Windows.Threading.DispatcherPriority.Input);
    }
    private void Finance_Key(object sender, KeyEventArgs e) { if (_ready) ApplyFilters(); }
    private void btnClearFinance_Click(object sender, RoutedEventArgs e) { cmbFinance.Text = ""; if (_ready) ApplyFilters(); }

    // ── Billing status picker ────────────────────────────────────────────────
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

    // ── Agent picker: Enter applies and shuts the popup ──────────────────────
    private void AgentPicker_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Escape) return;
        e.Handled = true;
        if (e.Key == Key.Enter && _ready) ApplyFilters();
        btnAgents.IsChecked = false;
    }

    private void btnApplyAgents_Click(object sender, RoutedEventArgs e)
    {
        if (_ready) ApplyFilters();
        btnAgents.IsChecked = false;
    }

    // ── Full record popup ────────────────────────────────────────────────────
    private static List<(string, string, bool)> FieldsOf(Row r) => new()
    {
        ("Vehicle No",        r.VehicleNo,          false),
        ("Chassis No",        r.ChassisNo,          false),
        ("Engine No",         r.EngineNo,           false),
        ("Customer",          r.CustomerName,       false),
        ("Loan No",           r.LoanNo,             false),
        ("Model / Maker",     r.Model,              false),
        ("Finance",           r.FinanceName,        false),
        ("Branch",            r.BranchName,         false),
        ("Agent",             r.AgentName,          false),
        ("Parking Yard",      r.ParkingYardName,    false),
        ("Yard Mobile",       r.ParkingYardMobile,  false),
        ("Executive",         r.ExecutiveName,      false),
        ("Repo Date",         r.RepoDate,           false),
        ("Billing Status",    r.ActionText,         false),
        ("Repo Charges",      r.RepoChargesText,    false),
        ("Advance",           r.AdvanceText,        false),
        ("Inventory",         r.CourierYn,          false),
        ("POD Number",        r.PodNumber,          false),
        ("Invoice No",        r.InvoiceNo,          false),
        ("Load Details",      r.LoadDetails,        false),
        ("Inventory Remark",  r.InventoryRemark,    true),
        ("Additional Charges (Notes, Amount)", r.AddlCharges,    true),
        ("Confirmation By (Name, Mobile)",     r.ConfirmationBy, true),
        ("Collection Update", r.CollectionUpdate,   true),
        ("Banker Address",    r.BankerAddress,      true),
    };

    private bool _popupOpen;

    private void OpenRecordPopup(Row r)
    {
        if (_popupOpen) return;
        _popupOpen = true;
        try { ShowRecordPopup(r); } finally { _popupOpen = false; }
    }

    private void ShowRecordPopup(Row r)
    {
        // Selecting first lets the existing form logic fill the panel in, then
        // the panel itself is lent to the window — same controls, same
        // handlers, just a different parent for the duration.
        if (!ReferenceEquals(grid.SelectedItem, r)) grid.SelectedItem = r;

        var veh = string.IsNullOrWhiteSpace(r.VehicleNo) ? r.ChassisNo : r.VehicleNo;
        var panel = pnlRight;
        editHostLocal.Children.Remove(panel);

        var win = new CourierRecordWindow(
            veh,
            string.Join("  •  ", new[] { r.CustomerName, r.FinanceName, r.RepoDate }
                .Where(x => !string.IsNullOrWhiteSpace(x))),
            FieldsOf(r),
            panel)
        {
            Owner = Window.GetWindow(this),
        };
        win.EditPanelReleased += p =>
        {
            if (!editHostLocal.Children.Contains(p)) editHostLocal.Children.Add(p);
        };
        win.ShowDialog();
    }

    private static DataGridRow? RowUnder(object? originalSource)
    {
        var src = originalSource as DependencyObject;
        while (src != null && src is not DataGridRow &&
               src is not System.Windows.Controls.Primitives.DataGridColumnHeader)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        return src as DataGridRow;
    }

    // A plain click on a row opens the full record. Ctrl/Shift clicks are left
    // alone — those are how you build a selection to copy out of the table.
    private void Grid_RowLeftClick(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
        if (RowUnder(e.OriginalSource)?.Item is not Row r) return;
        // Let the click finish selecting first, so the edit panel on the right
        // is already filled in behind the popup.
        Dispatcher.BeginInvoke(new Action(() => OpenRecordPopup(r)),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowUnder(e.OriginalSource) != null) e.Handled = true;
    }

    private void OpenRecord_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRow() is { } r) OpenRecordPopup(r);
    }

    // ── Excel-style copying ──────────────────────────────────────────────────
    private Row? CurrentRow() => grid.CurrentItem as Row ?? grid.SelectedItem as Row;

    private DataGridColumn? CurrentColumn() =>
        grid.CurrentColumn ?? (grid.SelectedCells.Count > 0 ? grid.SelectedCells[0].Column : null);

    private List<Row> SelectedRows()
    {
        var rows = grid.SelectedItems.OfType<Row>().ToList();
        if (rows.Count == 0 && CurrentRow() is { } one) rows.Add(one);
        var order = (grid.ItemsSource as IEnumerable<Row>)?.ToList();
        if (order != null) rows = rows.OrderBy(r => order.IndexOf(r)).ToList();
        return rows;
    }

    private static string Cell(DataGridColumn col, object item)
    {
        if (col is DataGridBoundColumn { Binding: System.Windows.Data.Binding b } &&
            !string.IsNullOrEmpty(b.Path?.Path))
        {
            var prop = item.GetType().GetProperty(b.Path.Path);
            if (prop != null) return prop.GetValue(item)?.ToString() ?? "";
        }
        return "";
    }

    /// Tabs and newlines inside a value would break the row/column grid Excel
    /// reads, so they are flattened to spaces on the way out.
    private static string Clean(string? v) =>
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

    private string BuildBlock(IEnumerable<Row> rows, List<DataGridColumn> cols, bool headers)
    {
        var sb = new System.Text.StringBuilder();
        if (headers)
            sb.AppendLine(string.Join("\t", cols.Select(c => Clean(c.Header?.ToString()))));
        foreach (var r in rows)
            sb.AppendLine(string.Join("\t", cols.Select(c => Clean(Cell(c, r)))));
        return sb.ToString();
    }

    private void CopyCell_Click(object sender, RoutedEventArgs e)
    {
        var col = CurrentColumn();
        if (CurrentRow() is { } r && col != null) ToClipboard(Clean(Cell(col, r)));
    }

    private void CopySelection_Click(object sender, RoutedEventArgs e) => CopySelected(false);
    private void CopySelectionHdr_Click(object sender, RoutedEventArgs e) => CopySelected(true);

    /// Copies the highlighted rows across every visible column, in the order
    /// they appear on screen, so the block pastes into Excel unchanged.
    private void CopySelected(bool headers)
    {
        var rows = SelectedRows();
        if (rows.Count == 0) return;
        ToClipboard(BuildBlock(rows, OrderedColumns(), headers));
    }

    private void CopyColumn_Click(object sender, RoutedEventArgs e)
    {
        var rows = (grid.ItemsSource as IEnumerable<Row>)?.ToList() ?? new List<Row>();
        CopyOneColumn(rows);
    }

    private void CopyColumnSel_Click(object sender, RoutedEventArgs e) => CopyOneColumn(SelectedRows());

    private void CopyOneColumn(List<Row> rows)
    {
        var col = CurrentColumn();
        if (col == null || rows.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Clean(col.Header?.ToString()));
        foreach (var r in rows) sb.AppendLine(Clean(Cell(col, r)));
        ToClipboard(sb.ToString());
    }

    private void CopyTable_Click(object sender, RoutedEventArgs e)
    {
        var rows = (grid.ItemsSource as IEnumerable<Row>)?.ToList() ?? new List<Row>();
        ToClipboard(BuildBlock(rows, OrderedColumns(), true));
    }

    // Ctrl+C copies the highlighted rows; Ctrl+Shift+C adds the header line.
    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        CopySelected((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
    }

    // Right-clicking a cell should aim the copy at that cell, not at whatever
    // was current before, and must not throw away the existing row selection.
    private void Grid_PreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        while (src != null && src is not DataGridCell) src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        if (src is not DataGridCell cell || cell.DataContext is not Row r) return;
        grid.CurrentCell = new DataGridCellInfo(r, cell.Column);
        if (!grid.SelectedItems.OfType<Row>().Contains(r)) grid.SelectedItem = r;
    }

    // ── Inventory remark, saved on its own ───────────────────────────────────
    private async void btnSaveInventoryRemark_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r)
        {
            txtInvRemarkStatus.Text = "Pick a record first.";
            return;
        }
        btnSaveInventoryRemark.IsEnabled = false;
        txtInvRemarkStatus.Text = "Saving…";
        try
        {
            var remark = txtInventoryRemark.Text?.Trim() ?? "";
            await DesktopApiClient.SaveInventoryRemarkAsync(r.Id, remark);
            r.Src = r.Src with { InventoryRemark = remark };
            grid.Items.Refresh();
            txtInvRemarkStatus.Text = "Saved.";
        }
        catch (Exception ex)
        {
            txtInvRemarkStatus.Text = "Failed: " + ex.Message;
        }
        finally { btnSaveInventoryRemark.IsEnabled = true; }
    }

    private void AgentSearch_Changed(object sender, TextChangedEventArgs e) => ShowAgentPicks();

    private void AgentPick_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAgentButton();
        if (_ready) ApplyFilters();
    }

    private void btnClearAgent_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in _agentPicks) a.IsChecked = false;
        txtAgentSearch.Text = "";
        ShowAgentPicks();
        UpdateAgentButton();
        if (_ready) ApplyFilters();
    }

    private async void btnLoad_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (grid.SelectedItem is not Row r)
        {
            pnlForm.IsEnabled = false;
            btnSubmit.IsEnabled = false;
            btnClear.IsEnabled = false;
            btnDetails.IsEnabled = false;
            pnlBilled.Visibility = System.Windows.Visibility.Collapsed;
            txtSel.Text = "Select a record from the list.";
            return;
        }

        btnDetails.IsEnabled = true;

        var veh = string.IsNullOrWhiteSpace(r.VehicleNo) ? r.Src.ChassisNo : r.VehicleNo;
        txtSel.Text = $"{veh}  •  {r.CustomerName}  •  {r.FinanceName}";

        bool hasRealBill = !string.IsNullOrWhiteSpace(r.Src.InvoiceNo)
                        || !string.IsNullOrWhiteSpace(r.Src.BillUrl);
        if (hasRealBill)
        {
            pnlBilled.Visibility = System.Windows.Visibility.Visible;
            txtInvoice.Text = string.IsNullOrWhiteSpace(r.Src.InvoiceNo)
                ? "Bill generated." : "Invoice No: " + r.Src.InvoiceNo;
            btnDownloadBill.IsEnabled = !string.IsNullOrWhiteSpace(r.Src.BillUrl);
        }
        else pnlBilled.Visibility = System.Windows.Visibility.Collapsed;

        _suppressCalc = true;
        txtInventoryRemark.Text = r.InventoryRemark;
        txtInvRemarkStatus.Text = "";
        txtBillingStatus.Text = r.ActionText;
        txtRemark.Text = r.Src.Remark;
        txtGross.Text = r.Src.TotalGross?.ToString("0.##") ?? "";
        txtPercent.Text = r.Src.CourierPercent?.ToString("0.##") ?? "";
        txtRepoCharges.Text = r.Src.RepoCharges?.ToString("0.##") ?? "";
        cmbCourier.SelectedIndex = string.Equals(r.Src.CourierYn, "Yes", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        cmbEditAgent.Text = r.AgentName;
        SetAgentEditing(false);
        txtBankerAddress.Text = r.Src.BankerAddress;
        txtPod.Text = r.Src.PodNumber;
        _suppressCalc = false;

        ShowAppInfo(r);
        ConfigureForStatus(r.Src.BillingAction);
        _ = LoadAdvancesAsync(r);
        LoadScreenshot(r.Src.ScreenshotUrl);

        pnlForm.IsEnabled = true;
        btnSubmit.IsEnabled = true;
        btnClear.IsEnabled = true;
        txtFormStatus.Text = "";
    }

    private void ShowAppInfo(Row r)
    {
        var vis = System.Windows.Visibility.Visible;
        var gone = System.Windows.Visibility.Collapsed;
        bool showUpdate = r.Src.BillingAction is "hold" or "collection_done";
        lblCollectionUpdate.Visibility = showUpdate ? vis : gone;
        txtCollectionUpdate.Visibility = showUpdate ? vis : gone;
        txtCollectionUpdate.Text = r.Src.CollectionUpdate;

        lblAddlCharges.Visibility = showUpdate ? vis : gone;
        txtAddlChargesInfo.Visibility = showUpdate ? vis : gone;
        txtAddlChargesInfo.Text = r.AddlCharges;

        decimal cash = r.Src.CashAmount ?? 0m;
        bool showCash = r.Src.BillingAction == "collection_done" && cash > 0m;
        pnlCash.Visibility = showCash ? vis : gone;
        if (!showCash) return;

        txtCashPaid.Text = cash.ToString("0.##");
        decimal repo = ParseAmt(txtRepoCharges.Text) ?? 0m;
        decimal net = repo - cash;
        txtCashNote.Text = net < 0m
            ? $"Agent holds this cash. Against repo charges {repo:0.##}, the agent owes the agency {(-net):0.##}."
            : $"Agent holds this cash. Against repo charges {repo:0.##}, agency still owes {net:0.##}.";
    }

    private bool _suppressCalc;

    /// Percentage only applies to "OK for billing" (gross × %). Hold-for-collection
    /// disables charge entry entirely; Collection-done allows manual repo charges.
    private void ConfigureForStatus(string action)
    {
        bool ok = action == "immediate";
        bool chargesEditable = action is "immediate" or "hold" or "collection_done";

        var showIfOk = ok ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        lblPercent.Visibility = showIfOk;
        txtPercent.Visibility = showIfOk;
        lblGross.Visibility   = showIfOk;
        txtGross.Visibility   = showIfOk;

        txtPercent.IsReadOnly     = !ok;
        txtRepoCharges.IsReadOnly = !chargesEditable;
        pnlAddAdvance.IsEnabled   = chargesEditable;
        lstAdvances.IsEnabled     = chargesEditable;

        var disabled = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#F0F0F0")!;
        txtRepoCharges.Background = chargesEditable ? System.Windows.Media.Brushes.White : disabled;
    }

    private long _advLoadedFor;

    private async System.Threading.Tasks.Task LoadAdvancesAsync(Row r)
    {
        _advLoadedFor = r.Id;
        _suppressCalc = true;
        _advances.Clear();
        _suppressCalc = false;
        RecalcAdvance();

        List<DesktopApiClient.CourierAdvanceDto> list;
        try { list = await DesktopApiClient.GetCourierAdvancesAsync(r.Id); }
        catch { return; }
        if (_advLoadedFor != r.Id) return;

        _suppressCalc = true;
        if (list.Count == 0 && (r.Src.Advance ?? 0m) != 0m)
            _advances.Add(new AdvRow
            {
                Date = DateTime.TryParse(r.Src.CreatedAt, out var seeded) ? seeded.Date : DateTime.Today,
                AmountText = r.Src.Advance!.Value.ToString("0.##")
            });
        foreach (var a in list)
            _advances.Add(new AdvRow
            {
                Id = a.Id,
                Date = DateTime.TryParse(a.Date, out var d) ? d.Date : DateTime.Today,
                AmountText = a.Amount.ToString("0.##"),
                Note = a.Note ?? ""
            });
        _suppressCalc = false;
        RecalcAdvance();
    }

    private void RecalcAdvance()
    {
        var total = _advances.Sum(a => ParseAmt(a.AmountText) ?? 0m);
        txtAdvance.Text = total == 0m ? "" : total.ToString("0.##");
        txtNoAdvances.Visibility = _advances.Count == 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        UpdateFinal();
    }

    private void btnAddAdvance_Click(object sender, RoutedEventArgs e)
    {
        var amt = ParseAmt(txtAdvAmount.Text);
        if (amt is null || amt == 0m)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Enter the advance amount first.";
            return;
        }
        _advances.Add(new AdvRow { Date = dpAdvDate.SelectedDate ?? DateTime.Today, AmountText = amt.Value.ToString("0.##") });
        txtAdvAmount.Text = "";
        dpAdvDate.SelectedDate = DateTime.Today;
        txtFormStatus.Foreground = System.Windows.Media.Brushes.Gray;
        txtFormStatus.Text = "Advance added — press Submit to save it.";
        RecalcAdvance();
    }

    private void AdvAmount_Key(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) btnAddAdvance_Click(sender, new RoutedEventArgs());
    }

    private void btnRemoveAdvance_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AdvRow a) return;
        _advances.Remove(a);
        txtFormStatus.Foreground = System.Windows.Media.Brushes.Gray;
        txtFormStatus.Text = "Advance removed — press Submit to save it.";
        RecalcAdvance();
    }

    private void Advance_Edited(object sender, TextChangedEventArgs e)
    {
        if (_suppressCalc) return;
        Dispatcher.BeginInvoke(new Action(RecalcAdvance), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Calc_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressCalc) return;
        var gross = ParseAmt(txtGross.Text);
        if (ReferenceEquals(sender, txtPercent))
        {
            var pct = ParseAmt(txtPercent.Text);
            if (gross.HasValue && pct.HasValue)
            {
                _suppressCalc = true;
                txtRepoCharges.Text = (gross.Value * pct.Value / 100m).ToString("0.##");
                _suppressCalc = false;
            }
        }
        else if (ReferenceEquals(sender, txtRepoCharges))
        {
            var repo = ParseAmt(txtRepoCharges.Text);
            if (gross.HasValue && gross.Value != 0m && repo.HasValue)
            {
                _suppressCalc = true;
                txtPercent.Text = (repo.Value * 100m / gross.Value).ToString("0.##");
                _suppressCalc = false;
            }
        }
        UpdateFinal();
    }

    private void UpdateFinal()
    {
        var repo = ParseAmt(txtRepoCharges.Text) ?? 0m;
        var adv  = ParseAmt(txtAdvance.Text) ?? 0m;
        txtFinal.Text = (repo - adv).ToString("0.##");
        if (pnlCash.Visibility == System.Windows.Visibility.Visible &&
            grid.SelectedItem is Row sel)
        {
            decimal cash = sel.Src.CashAmount ?? 0m;
            decimal net = repo - cash;
            txtCashNote.Text = net < 0m
                ? $"Agent holds this cash. Against repo charges {repo:0.##}, the agent owes the agency {(-net):0.##}."
                : $"Agent holds this cash. Against repo charges {repo:0.##}, agency still owes {net:0.##}.";
        }
    }

    private string? _screenshotUrl;
    private async void LoadScreenshot(string? url)
    {
        _screenshotUrl = url;
        if (string.IsNullOrWhiteSpace(url))
        {
            lblScreenshot.Visibility = System.Windows.Visibility.Collapsed;
            pnlScreenshot.Visibility = System.Windows.Visibility.Collapsed;
            imgScreenshot.Source = null;
            return;
        }
        lblScreenshot.Visibility = System.Windows.Visibility.Visible;
        pnlScreenshot.Visibility = System.Windows.Visibility.Visible;
        try
        {
            var bytes = await App.HttpClient.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            imgScreenshot.Source = bmp;
        }
        catch { imgScreenshot.Source = null; }
    }

    private void imgScreenshot_Click(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_screenshotUrl)) return;
        try { Process.Start(new ProcessStartInfo(_screenshotUrl) { UseShellExecute = true }); } catch { }
    }

    private void btnDetails_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is Row r) OpenRecordPopup(r);
    }

    /// Fetches the bill and saves it locally rather than handing the URL to a
    /// browser, which blocks the download. The suggested name carries the
    /// vehicle, invoice and a timestamp so no two saves collide.
    private async void btnDownloadBill_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r || string.IsNullOrWhiteSpace(r.Src.BillUrl)) return;

        var ext = Path.GetExtension(new Uri(r.Src.BillUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".pdf";

        var veh = new string((string.IsNullOrWhiteSpace(r.VehicleNo) ? r.ChassisNo : r.VehicleNo)
            .Where(char.IsLetterOrDigit).ToArray());
        if (veh.Length == 0) veh = "bill";
        var inv = new string((r.InvoiceNo ?? "").Where(char.IsLetterOrDigit).ToArray());

        var name = $"RepoBill_{veh}"
                 + (inv.Length > 0 ? $"_INV{inv}" : "")
                 + $"_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";

        var dlg = new SaveFileDialog
        {
            Title = "Save bill",
            FileName = name,
            Filter = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "PDF document (*.pdf)|*.pdf|All files (*.*)|*.*"
                : "Word document (*.docx)|*.docx|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            btnDownloadBill.IsEnabled = false;
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Gray;
            txtFormStatus.Text = "Downloading bill…";

            var bytes = await App.HttpClient.GetByteArrayAsync(r.Src.BillUrl);
            await File.WriteAllBytesAsync(dlg.FileName, bytes);

            txtFormStatus.Foreground = System.Windows.Media.Brushes.Green;
            txtFormStatus.Text = "Bill saved.";
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Could not download the bill: " + ex.Message;
        }
        finally { btnDownloadBill.IsEnabled = true; }
    }

    private static decimal? ParseAmt(string s)
        => decimal.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private async void btnSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r) return;
        var courier = cmbCourier.SelectedIndex == 1 ? "Yes" : "No";

        await SaveAsync(r.Id, new
        {
            RepoCharges = ParseAmt(txtRepoCharges.Text),
            Advances = _advances
                .Where(a => (ParseAmt(a.AmountText) ?? 0m) != 0m)
                .Select(a => new
                {
                    Amount = ParseAmt(a.AmountText) ?? 0m,
                    Date = a.Date.ToString("yyyy-MM-dd"),
                    Note = a.Note
                }).ToList(),
            CourierYn = courier,
            BankerAddress = txtBankerAddress.Text.Trim(),
            PodNumber = txtPod.Text.Trim(),
            CourierPercent = ParseAmt(txtPercent.Text)
        }, "Saved.");
    }

    private void SetAgentEditing(bool on)
    {
        cmbEditAgent.IsEnabled = on;
        btnAgentEdit.Visibility   = on ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        btnAgentSave.Visibility   = on ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        btnAgentCancel.Visibility = on ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void btnAgentEdit_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row) return;
        SetAgentEditing(true);
        cmbEditAgent.Focus();
    }

    private void btnAgentCancel_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is Row r) cmbEditAgent.Text = r.AgentName;
        SetAgentEditing(false);
    }

    private async void btnAgentSave_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r) return;
        var agent = (cmbEditAgent.Text ?? "").Trim();
        if (agent.Length == 0)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Agent name cannot be blank.";
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
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Gray;
            txtFormStatus.Text = "Saving agent name…";

            await DesktopApiClient.UpdateSubmissionFieldsAsync(r.Id, new { AgentName = agent });

            txtFormStatus.Foreground = System.Windows.Media.Brushes.Green;
            txtFormStatus.Text = "Agent name saved.";
            SetAgentEditing(false);
            await LoadAsync();

            var again = _rows.FirstOrDefault(x => x.Id == r.Id);
            if (again != null) { grid.SelectedItem = again; grid.ScrollIntoView(again); }
        }
        catch (Exception ex)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Could not save the agent name: " + ex.Message;
        }
        finally { btnAgentSave.IsEnabled = true; }
    }

    private async void btnClear_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r) return;
        if (MessageBox.Show("Clear this record's courier entries (Repo Charges, Advance, Inventory, Banker Address, POD)?",
                "Couriers", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await SaveAsync(r.Id, new { ClearEntries = true }, "Entries cleared.");
    }

    private async System.Threading.Tasks.Task SaveAsync(long id, object dto, string okText)
    {
        try
        {
            btnSubmit.IsEnabled = false;
            btnClear.IsEnabled = false;
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Gray;
            txtFormStatus.Text = "Saving…";

            await DesktopApiClient.UpdateCourierSubmissionAsync(id, dto);

            txtFormStatus.Foreground = System.Windows.Media.Brushes.Green;
            txtFormStatus.Text = okText;
            await LoadAsync();

            // Keep the same record selected so it can be edited again right away.
            var again = _rows.FirstOrDefault(x => x.Id == id);
            if (again != null)
            {
                grid.SelectedItem = again;
                grid.ScrollIntoView(again);
            }
        }
        catch (Exception ex)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Failed: " + ex.Message;
        }
        finally { btnSubmit.IsEnabled = true; btnClear.IsEnabled = true; }
    }
}
