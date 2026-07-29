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

        public static Row From(DesktopApiClient.RepoSubmissionDto d) => new()
        {
            Src = d,
            VehicleNo = d.VehicleNo, ChassisNo = d.ChassisNo,
            CustomerName = d.CustomerName, FinanceName = d.FinanceName, BranchName = d.BranchName,
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
        dpFrom.SelectedDate = DateTime.Today.AddDays(-30);
        dpTo.SelectedDate = DateTime.Today;
        Loaded += async (_, __) => { _ready = true; await LoadAsync(); };
    }

    private bool _ready;

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
            grid.ItemsSource = _rows;
            txtStatus.Text = $"{_rows.Count} record(s).";
        }
        catch (Exception ex) { txtStatus.Text = "Failed: " + ex.Message; }
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

        if (w.ShowDialog() == true) await LoadAsync();
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
