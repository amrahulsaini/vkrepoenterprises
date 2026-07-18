using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Couriers;

public partial class CouriersPage : Page
{
    private List<Row> _rows = new();

    private class Row
    {
        public DesktopApiClient.RepoSubmissionDto Src { get; set; } = null!;
        public long Id => Src.Id;
        public string CreatedAt => Src.CreatedAt;
        public string VehicleNo => Src.VehicleNo;
        public string CustomerName => Src.CustomerName;
        public string FinanceName => Src.FinanceName;
        public string ActionText => Src.BillingAction switch
        {
            "immediate" => "For Billing",
            "hold"      => "Hold for Billing",
            "cancel"    => "Cancel",
            _           => Src.BillingAction
        };
        public string RepoChargesText => Src.RepoCharges?.ToString("0.##") ?? "";
        public string AdvanceText => Src.Advance?.ToString("0.##") ?? "";
        public string CourierYn => Src.CourierYn;
        public string BankerAddress => Src.BankerAddress;
        public string PodNumber => Src.PodNumber;
    }

    public CouriersPage()
    {
        InitializeComponent();
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

            var data = await DesktopApiClient.GetRepoSubmissionsAsync(from, to, new List<int>(), null);

            string? actionFilter = cmbAction.SelectedIndex switch
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

    private void grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (grid.SelectedItem is not Row r)
        {
            pnlForm.IsEnabled = false;
            btnSubmit.IsEnabled = false;
            btnClear.IsEnabled = false;
            txtSel.Text = "Select a record from the list.";
            return;
        }

        txtSel.Text = $"{r.VehicleNo}  •  {r.CustomerName}  •  {r.FinanceName}";
        txtRepoCharges.Text = r.Src.RepoCharges?.ToString("0.##") ?? "";
        txtAdvance.Text = r.Src.Advance?.ToString("0.##") ?? "";
        cmbCourier.SelectedIndex = string.Equals(r.Src.CourierYn, "Yes", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        txtBankerAddress.Text = r.Src.BankerAddress;
        txtPod.Text = r.Src.PodNumber;

        pnlForm.IsEnabled = true;
        btnSubmit.IsEnabled = true;
        btnClear.IsEnabled = true;
        txtFormStatus.Text = "";
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
            Advance = ParseAmt(txtAdvance.Text),
            CourierYn = courier,
            BankerAddress = txtBankerAddress.Text.Trim(),
            PodNumber = txtPod.Text.Trim()
        }, "Saved.");
    }

    private async void btnClear_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r) return;
        if (MessageBox.Show("Clear this record's courier entries (Repo Charges, Advance, Courier, Banker Address, POD)?",
                "Couriers", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await SaveAsync(r.Id, new
        {
            RepoCharges = (decimal?)null,
            Advance = (decimal?)null,
            CourierYn = (string?)null,
            BankerAddress = (string?)null,
            PodNumber = (string?)null
        }, "Entries cleared.");
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
        }
        catch (Exception ex)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Failed: " + ex.Message;
        }
        finally { btnSubmit.IsEnabled = true; btnClear.IsEnabled = true; }
    }
}
