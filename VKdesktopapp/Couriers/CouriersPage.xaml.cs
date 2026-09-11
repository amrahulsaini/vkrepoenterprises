using System;
using System.Collections.Generic;
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

    internal record Pick(string Key, string Name);

    internal static readonly List<Pick> BillingPicks = new()
    {
        new("immediate",       "OK for billing"),
        new("hold",            "Hold for collection"),
        new("collection_done", "Collection done"),
        new("cancel",          "Cancel"),
    };

    internal static readonly List<string> YesNo = new() { "Yes", "No" };

    internal static readonly HashSet<string> CourierFields = new()
    {
        "CourierYn", "InventoryRemark", "BankerAddress", "PodNumber"
    };

    private class Row
    {
        public DesktopApiClient.RepoSubmissionDto Src { get; set; } = null!;
        public HashSet<string> Dirty { get; } = new();

        private void Put(string field, string? old, string? value, Func<string, DesktopApiClient.RepoSubmissionDto> apply)
        {
            var v = (value ?? "").Trim();
            if (v == (old ?? "").Trim()) return;
            Src = apply(v);
            Dirty.Add(field);
        }

        public long Id => Src.Id;
        public string RepoDate => Src.CreatedAt;
        public string InvoiceNo => Src.InvoiceNo;
        public string VehicleNo => Src.VehicleNo;
        public string LoanNo { get => Src.LoanNo; set => Put(nameof(LoanNo), Src.LoanNo, value, v => Src with { LoanNo = v }); }
        public string CustomerName { get => Src.CustomerName; set => Put(nameof(CustomerName), Src.CustomerName, value, v => Src with { CustomerName = v }); }
        public string BranchName { get => (Src.BranchName ?? "").ToUpperInvariant(); set => Put(nameof(BranchName), BranchName, value, v => Src with { BranchName = v }); }
        public string Model { get => Src.Model; set => Put(nameof(Model), Src.Model, value, v => Src with { Model = v }); }
        public string ChassisNo { get => Src.ChassisNo; set => Put(nameof(ChassisNo), Src.ChassisNo, value, v => Src with { ChassisNo = v }); }
        public string EngineNo { get => Src.EngineNo; set => Put(nameof(EngineNo), Src.EngineNo, value, v => Src with { EngineNo = v }); }
        public string AgentName { get => Src.AgentName; set => Put(nameof(AgentName), Src.AgentName, value, v => Src with { AgentName = v }); }
        public string ParkingYardName { get => Src.ParkingYardName; set => Put(nameof(ParkingYardName), Src.ParkingYardName, value, v => Src with { ParkingYardName = v }); }
        public string ParkingYardMobile { get => Src.ParkingYardMobile; set => Put(nameof(ParkingYardMobile), Src.ParkingYardMobile, value, v => Src with { ParkingYardMobile = v }); }
        public string LoadDetails { get => Src.LoadDetails; set => Put(nameof(LoadDetails), Src.LoadDetails, value, v => Src with { LoadDetails = v }); }
        public string ExecutiveName { get => Src.ExecutiveName; set => Put(nameof(ExecutiveName), Src.ExecutiveName, value, v => Src with { ExecutiveName = v }); }
        public string CollectionUpdate { get => Src.CollectionUpdate; set => Put(nameof(CollectionUpdate), Src.CollectionUpdate, value, v => Src with { CollectionUpdate = v }); }
        public string FinanceName { get => (Src.FinanceName ?? "").ToUpperInvariant(); set => Put(nameof(FinanceName), FinanceName, value, v => Src with { FinanceName = v }); }
        public string BillingRemark { get => Src.BillingRemark ?? ""; set => Put(nameof(BillingRemark), Src.BillingRemark, value, v => Src with { BillingRemark = v }); }
        public string InventoryRemark { get => Src.InventoryRemark ?? ""; set => Put(nameof(InventoryRemark), Src.InventoryRemark, value, v => Src with { InventoryRemark = v }); }
        public string BankerAddress { get => Src.BankerAddress; set => Put(nameof(BankerAddress), Src.BankerAddress, value, v => Src with { BankerAddress = v }); }
        public string PodNumber { get => Src.PodNumber; set => Put(nameof(PodNumber), Src.PodNumber, value, v => Src with { PodNumber = v }); }

        public string CourierYn
        {
            get => string.Equals(Src.CourierYn?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
            set => Put(nameof(CourierYn), CourierYn, value, v => Src with { CourierYn = v });
        }

        public string ActionKey
        {
            get => Src.BillingAction;
            set => Put(nameof(ActionKey), Src.BillingAction, value, v => Src with { BillingAction = v });
        }

        public string ActionText => BillingPicks.FirstOrDefault(p => p.Key == Src.BillingAction)?.Name ?? Src.BillingAction;

        public string AddlCharges
        {
            get => JoinParts(Src.AddlChargesNotes, Src.AddlChargesAmount?.ToString("0.##"));
            set
            {
                var (head, tail) = SplitLast(value);
                decimal? amt = ParseAmt(tail);
                var notes = amt.HasValue ? head : (value ?? "").Trim();
                Put(nameof(AddlCharges), AddlCharges, value, _ => Src with
                {
                    AddlChargesNotes = notes,
                    AddlChargesAmount = amt ?? Src.AddlChargesAmount
                });
            }
        }

        public string ConfirmationBy
        {
            get => JoinParts(Src.ConfirmationByName, Src.ConfirmationByMobile);
            set
            {
                var (head, tail) = SplitLast(value);
                Put(nameof(ConfirmationBy), ConfirmationBy, value, _ => Src with
                {
                    ConfirmationByName = head,
                    ConfirmationByMobile = tail
                });
            }
        }

        private static (string, string) SplitLast(string? value)
        {
            var v = (value ?? "").Trim();
            int i = v.LastIndexOf(',');
            return i < 0 ? (v, "") : (v[..i].Trim(), v[(i + 1)..].Trim());
        }

        private static string JoinParts(params string?[] parts)
            => string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
    }

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

    private readonly List<StatusPick> _statusPicks = new()
    {
        new StatusPick { Name = "OK for billing",       Key = "immediate" },
        new StatusPick { Name = "Hold for collection",  Key = "hold" },
        new StatusPick { Name = "Collection done",      Key = "collection_done" },
        new StatusPick { Name = "Cancel",               Key = "cancel" },
        new StatusPick { Name = "Billing Done",         Key = "billed" },
    };

    private bool _ready;

    private static string Squash4(string? s) =>
        new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();


    public CouriersPage()
    {
        InitializeComponent();
        colBilling.ItemsSource = BillingPicks;
        colInventory.ItemsSource = YesNo;
        dpFrom.DisplayDateEnd = DateTime.Today;
        dpTo.DisplayDateEnd = DateTime.Today;
        dpFrom.SelectedDate = DateTime.Today.AddDays(-7);
        dpTo.SelectedDate = DateTime.Today;
        lstStatusPicks.ItemsSource = _statusPicks;
        UpdateStatusButton();
        Loaded += async (_, __) => { _ready = true; await LoadAsync(); };
    }

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

    internal static void BeginEditOnClick(DataGrid grid, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
        var src = e.OriginalSource as DependencyObject;
        while (src != null && src is not DataGridCell)
            src = src is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(src) : null;
        if (src is not DataGridCell cell || cell.IsEditing || cell.IsReadOnly) return;
        if (!cell.IsFocused) cell.Focus();
        grid.BeginEdit(e);
    }

    private void Grid_CellClick(object sender, MouseButtonEventArgs e) => BeginEditOnClick(grid, e);

    internal static void CommitPick(DataGrid grid, object sender)
    {
        if (sender is not ComboBox { IsLoaded: true }) return;
        grid.Dispatcher.BeginInvoke(new Action(() => grid.CommitEdit(DataGridEditingUnit.Cell, true)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Pick_Changed(object sender, SelectionChangedEventArgs e) => CommitPick(grid, sender);

    private void grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not Row r) return;
        Dispatcher.BeginInvoke(new Action(async () => await SaveRow(r)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private async System.Threading.Tasks.Task SaveRow(Row r)
    {
        if (r.Dirty.Count == 0) return;
        var dirty = r.Dirty.ToList();
        r.Dirty.Clear();
        txtStatus.Text = "Saving…";
        try
        {
            await SaveFields(r.Id, r.Src, dirty);
            txtStatus.Text = "Saved.";
        }
        catch (Exception ex)
        {
            txtStatus.Text = "Save failed: " + ex.Message;
            await LoadAsync();
        }
    }

    internal static async System.Threading.Tasks.Task SaveFields(long id, DesktopApiClient.RepoSubmissionDto s, List<string> dirty)
    {
        var fields  = new Dictionary<string, object?>();
        var courier = new Dictionary<string, object?>();
        foreach (var f in dirty)
        {
            switch (f)
            {
                case "ActionKey":
                    await DesktopApiClient.UpdateBillingActionAsync(id, s.BillingAction);
                    break;
                case "AddlCharges":
                    fields["AddlChargesNotes"]  = s.AddlChargesNotes;
                    fields["AddlChargesAmount"] = s.AddlChargesAmount;
                    break;
                case "ConfirmationBy":
                    fields["ConfirmationByName"]   = s.ConfirmationByName;
                    fields["ConfirmationByMobile"] = s.ConfirmationByMobile;
                    break;
                default:
                    var value = typeof(DesktopApiClient.RepoSubmissionDto).GetProperty(f)?.GetValue(s);
                    if (CourierFields.Contains(f)) courier[f] = value ?? "";
                    else fields[f] = value ?? "";
                    break;
            }
        }
        if (courier.Count > 0) await DesktopApiClient.UpdateCourierSubmissionAsync(id, courier);
        if (fields.Count > 0)  await DesktopApiClient.UpdateSubmissionFieldsAsync(id, fields);
    }

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
        var path = col switch
        {
            DataGridBoundColumn { Binding: System.Windows.Data.Binding b } => b.Path?.Path,
            DataGridComboBoxColumn c => c.SortMemberPath,
            _ => null
        };
        if (string.IsNullOrEmpty(path)) return "";
        return item.GetType().GetProperty(path)?.GetValue(item)?.ToString() ?? "";
    }

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

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        CopySelected((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
    }

    private void Grid_PreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        while (src != null && src is not DataGridCell) src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        if (src is not DataGridCell cell || cell.DataContext is not Row r) return;
        grid.CurrentCell = new DataGridCellInfo(r, cell.Column);
        if (!grid.SelectedItems.OfType<Row>().Contains(r)) grid.SelectedItem = r;
    }

    private void OpenScreenshots_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRow() is not { } r) return;
        var urls = (r.Src.ScreenshotUrls ?? new List<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        if (urls.Count == 0 && !string.IsNullOrWhiteSpace(r.Src.ScreenshotUrl)) urls.Add(r.Src.ScreenshotUrl);
        if (urls.Count == 0) { txtStatus.Text = "No payment screenshot on this record."; return; }
        foreach (var u in urls)
            try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { }
    }

    private async void DownloadBill_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRow() is not { } r) return;
        if (string.IsNullOrWhiteSpace(r.Src.BillUrl)) { txtStatus.Text = "No bill generated for this record yet."; return; }

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
            txtStatus.Text = "Downloading bill…";
            var bytes = await App.HttpClient.GetByteArrayAsync(r.Src.BillUrl);
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            txtStatus.Text = "Bill saved.";
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            txtStatus.Text = "Could not download the bill: " + ex.Message;
        }
    }

    private static decimal? ParseAmt(string? s)
        => decimal.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;
}
