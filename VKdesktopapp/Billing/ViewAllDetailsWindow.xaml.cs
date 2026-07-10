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
        public string VehicleNo => Src.VehicleNo;
        public string CustomerName => Src.CustomerName;
        public string FinanceName => Src.FinanceName;
        public string BranchName => Src.BranchName;
        public string LoanNo => Src.LoanNo;
        public string AgentName => Src.AgentName;
        public string ParkingYardName => Src.ParkingYardName;
        public string AddlChargesAmount => Src.AddlChargesAmount?.ToString("0.##") ?? "";
        public string ActionText => Src.BillingAction switch
        {
            "immediate" => "Bill immediately",
            "hold"      => "Hold" + (string.IsNullOrWhiteSpace(Src.HoldUntil)
                                ? (Src.HoldDays is int d ? $" {d}d" : "")
                                : $" till {Src.HoldUntil}"),
            "cancel"    => "Cancel",
            _           => Src.BillingAction
        };
        public string StatusText => Src.BillStatus == "billed" ? "Billed" : "Pending";
    }

    public ViewAllDetailsWindow(BillingPage parent, BillingSession? session, List<int> financeIds)
    {
        InitializeComponent();
        _parent = parent;
        _session = session;
        _financeIds = financeIds;
        dpFrom.SelectedDate = DateTime.Today.AddDays(-30);
        dpTo.SelectedDate = DateTime.Today;
        Loaded += async (_, __) => await LoadAsync();
    }

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
                3 => "cancel",
                _ => null
            };
            if (actionFilter != null) data = data.Where(d => d.BillingAction == actionFilter).ToList();

            _rows = data.Select(d => new Row { Src = d }).ToList();
            grid.ItemsSource = _rows;
            txtStatus.Text = $"{_rows.Count} record(s).";
        }
        catch (Exception ex) { txtStatus.Text = "Failed: " + ex.Message; }
    }

    private async void btnLoad_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void btnGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        var row = _rows.FirstOrDefault(r => r.Id == id);
        if (row == null) return;
        await _parent.LoadSubmission(row.Src);
        Close();
    }
}
