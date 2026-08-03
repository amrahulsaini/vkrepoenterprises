using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Billing;

public partial class ViewAllDetailsWindow : Window
{
    private readonly BillingPage _parent;
    private readonly BillingSession? _session;
    private readonly List<int> _financeIds;
    private List<Row> _rows = new();

    private class Row
    {
        public DesktopApiClient.RepoSubmissionDto Src { get; set; } = null!;
        public long Id => Src.Id;
        public string CreatedAt => Src.CreatedAt;
        public string VehicleOrChassis => !string.IsNullOrWhiteSpace(VehicleNo) ? VehicleNo : ChassisNo;

        // Editable (app-filled) fields — edited inline in the grid, saved to the server.
        public string VehicleNo { get; set; } = "";
        public string ChassisNo { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string FinanceName { get; set; } = "";
        public string BranchName { get; set; } = "";
        public string LoanNo { get; set; } = "";
        public string AgentName { get; set; } = "";
        public string ParkingYardName { get; set; } = "";
        public string AddlChargesAmount { get; set; } = "";
        public string CollectionUpdate { get; set; } = "";
        public string Remark { get; set; } = "";

        public string ActionText => Src.BillingAction switch
        {
            "immediate"       => "OK for billing",
            "hold"            => "Hold for collection",
            "collection_done" => "Collection done",
            "cancel"          => "Cancel",
            _                 => Src.BillingAction
        };
        public string StatusText => Src.BillStatus == "billed" ? "Billed" : "Pending";
        public string BillStatusText => StatusText;
        public string RepoDate => Src.CreatedAt;
        public string EngineNo => Src.EngineNo;
        public string Model => Src.Model;
        public string ParkingYardMobile => Src.ParkingYardMobile;
        public string ExecutiveName => Src.ExecutiveName;
        public string ConfirmationByName => Src.ConfirmationByName;
        public string ConfirmationByMobile => Src.ConfirmationByMobile;
        public string LoadDetails => Src.LoadDetails;
        public string InvoiceNo => Src.InvoiceNo;
        public string AddlCharges =>
            string.Join(", ", new[] { Src.AddlChargesNotes, AddlChargesAmount }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        public string ConfirmationBy =>
            string.Join(", ", new[] { Src.ConfirmationByName, Src.ConfirmationByMobile }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        public static Row From(DesktopApiClient.RepoSubmissionDto d) => new()
        {
            Src = d,
            VehicleNo = d.VehicleNo, ChassisNo = d.ChassisNo,
            CustomerName = d.CustomerName,
            FinanceName = (d.FinanceName ?? "").ToUpperInvariant(),
            BranchName = (d.BranchName ?? "").ToUpperInvariant(),
            LoanNo = d.LoanNo, AgentName = d.AgentName, ParkingYardName = d.ParkingYardName,
            AddlChargesAmount = d.AddlChargesAmount?.ToString("0.##") ?? "",
            CollectionUpdate = d.CollectionUpdate, Remark = d.Remark
        };
    }

    public ViewAllDetailsWindow(BillingPage parent, BillingSession? session, List<int> financeIds)
    {
        InitializeComponent();
        _parent = parent;
        _session = session;
        _financeIds = financeIds;
        dpFrom.DisplayDateEnd = DateTime.Today;
        dpTo.DisplayDateEnd = DateTime.Today;
        dpFrom.SelectedDate = DateTime.Today.AddDays(-30);
        dpTo.SelectedDate = DateTime.Today;
        Loaded += async (_, __) => { _ready = true; await LoadAsync(); };
    }

    private bool _ready;

    private static long RowIdOf(object? item) => item is Row r ? r.Id : 0;

    private long _lastRowId;

    /// Clicking the already-expanded row collapses it again.
    private void XlGrid_RowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as System.Windows.DependencyObject;
        while (src != null && src is not DataGridRow && src is not System.Windows.Controls.Primitives.DataGridColumnHeader)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        if (src is not DataGridRow row) return;

        long id = RowIdOf(row.Item);
        if (id != 0 && id == _lastRowId && row.IsSelected)
        {
            row.IsSelected = false;
            _lastRowId = 0;
            e.Handled = true;
            return;
        }
        _lastRowId = id;
    }


    // Instant filtering — no Load button; any filter/date change reloads.
    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) await LoadAsync();
    }

    private async void btnRefresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async System.Threading.Tasks.Task LoadAsync()
    {
        txtStatus.Text = "Loading…";
        try
        {
            string? from = dpFrom.SelectedDate?.ToString("yyyy-MM-dd");
            string? to   = dpTo.SelectedDate?.ToString("yyyy-MM-dd");
            string? status = (cmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant();
            if (status == "all") status = null;

            var data = await DesktopApiClient.GetRepoSubmissionsAsync(from, to, _financeIds, status);

            string? actionFilter = (cmbAction.SelectedIndex) switch
            {
                1 => "immediate",
                2 => "hold",
                3 => "collection_done",
                4 => "cancel",
                _ => null
            };
            if (actionFilter != null) data = data.Where(d => d.BillingAction == actionFilter).ToList();

            _rows = data.Select(Row.From).ToList();
            RefreshFinanceList();
            ApplyFinanceFilter();
        }
        catch (Exception ex) { txtStatus.Text = "Failed: " + ex.Message; }
    }

    private void RefreshFinanceList()
    {
        var keep = cmbFinance.Text;
        var names = _rows.Select(r => (r.FinanceName ?? "").Trim())
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        cmbFinance.ItemsSource = names;
        cmbFinance.Text = keep;
        txtFinanceCount.Text = names.Count == 1 ? "1 finance" : names.Count + " finances";
    }

    private static string Squash(string? s) =>
        new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    internal static bool NameMatches(string? name, string term)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return false;
        if (n.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;

        var squashedName = Squash(n);
        if (squashedName.Contains(Squash(term))) return true;

        var words = term.Split(new[] { ' ', ',', '-', '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 && words.All(w => n.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool RcLast4Matches(string? vehicleNo, string? chassisNo, string last4)
    {
        if (last4.Length == 0) return true;
        var v = Squash(vehicleNo);
        var c = Squash(chassisNo);
        return v.EndsWith(last4) || v.Contains(last4) || c.EndsWith(last4) || c.Contains(last4);
    }

    private void ApplyFinanceFilter()
    {
        var term = (cmbFinance.Text ?? "").Trim();
        var last4 = Squash(txtRcLast4?.Text);
        List<Row> shown;
        if (term.Length == 0)
        {
            shown = _rows;
        }
        else
        {
            var exact = _rows.Where(r => string.Equals((r.FinanceName ?? "").Trim(), term,
                StringComparison.OrdinalIgnoreCase)).ToList();
            shown = exact.Count > 0
                ? exact
                : _rows.Where(r => NameMatches(r.FinanceName, term)).ToList();
        }
        if (last4.Length > 0)
            shown = shown.Where(r => RcLast4Matches(r.VehicleNo, r.ChassisNo, last4)).ToList();

        grid.ItemsSource = shown;
        txtStatus.Text = shown.Count == _rows.Count
            ? shown.Count + " record(s)."
            : shown.Count + " of " + _rows.Count + " record(s).";
    }

    private void Finance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(new Action(ApplyFinanceFilter), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Finance_Key(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_ready) ApplyFinanceFilter();
    }

    private void RcLast4_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) ApplyFinanceFilter();
    }

    private void btnClearFinance_Click(object sender, RoutedEventArgs e)
    {
        cmbFinance.Text = "";
        if (_ready) ApplyFinanceFilter();
    }

    private async void btnGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row == null) return;

        // Only "OK for billing" records may be billed. Hold-for-collection,
        // Collection-done and Cancel must be switched to OK for billing first.
        if (row.Src.BillingAction != "immediate")
        {
            MessageBox.Show(
                "This record is \"" + row.ActionText + "\".\n\n" +
                "Only \"OK for billing\" records can be billed. Change the billing status to " +
                "\"OK for billing\" first (use \"Change status\" on this row), then generate the bill.",
                "Billing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _parent.LoadSubmission(row.Src);
        Close();
    }

    private async void btnChangeAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row == null) return;

        var veh = string.IsNullOrWhiteSpace(row.Src.VehicleNo) ? row.Src.ChassisNo : row.Src.VehicleNo;
        var w = new BillingActionWindow(id, row.Src.BillingAction,
            $"{veh}  •  {row.Src.CustomerName}") { Owner = this };

        if (w.ShowDialog() != true) return;
        await LoadAsync();
        var again = _rows.FirstOrDefault(x => x.Id == id);
        if (again != null)
        {
            grid.SelectedItem = again;
            grid.ScrollIntoView(again);
            grid.Focus();
        }
    }

    // Inline editing of the app-filled fields; saves the edited row to the server.
    private async void grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not Row r) return;
        await Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                decimal? addl = decimal.TryParse((r.AddlChargesAmount ?? "").Trim(),
                    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var a)
                    ? a : (decimal?)null;
                await DesktopApiClient.UpdateSubmissionFieldsAsync(r.Id, new
                {
                    r.CustomerName, r.FinanceName, r.BranchName, r.LoanNo, r.AgentName,
                    r.ParkingYardName, r.VehicleNo, r.ChassisNo, r.CollectionUpdate, r.Remark,
                    AddlChargesAmount = addl
                });
                txtStatus.Text = "Saved.";
            }
            catch (Exception ex) { txtStatus.Text = "Save failed: " + ex.Message; }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private readonly VehicleSearchRepository _search = new();

    private async void btnVehicle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row == null) return;

        VehicleDetailsWindow? win = null;
        if (row.Src.RecordId is long recId && recId > 0)
        {
            try
            {
                var rec = await _search.GetRecordByIdAsync(recId);
                if (rec != null) win = VehicleDetailsWindow.FromRecord(rec);
            }
            catch { }
        }

        win ??= new VehicleDetailsWindow(
            !string.IsNullOrWhiteSpace(row.Src.VehicleNo) ? row.Src.VehicleNo : row.Src.ChassisNo,
            new (string, string)[]
            {
                ("Vehicle No", row.Src.VehicleNo),
                ("Chassis No (VIN)", row.Src.ChassisNo),
                ("Engine No", row.Src.EngineNo),
                ("Model", row.Src.Model),
                ("Customer Name", row.Src.CustomerName),
                ("Finance", row.Src.FinanceName),
                ("Branch", row.Src.BranchName),
                ("Loan No", row.Src.LoanNo),
                ("Agent", row.Src.AgentName),
            });

        win.Owner = this;
        win.ShowDialog();
    }
}
