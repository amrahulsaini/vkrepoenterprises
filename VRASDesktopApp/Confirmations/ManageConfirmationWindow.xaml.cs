using System.Net.Http.Json;
using System.Windows;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Confirmations;

public partial class ManageConfirmationWindow : Window
{
    private readonly VehicleSearchItem _record;

    public ManageConfirmationWindow(VehicleSearchItem record)
    {
        InitializeComponent();
        _record = record;
    }

    private class SeizerDisplayItem
    {
        public string UserId { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Display { get; set; } = "";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate Vehicle Details
        txtVehicleNo.Text = _record.VehicleNo;
        txtChassisNo.Text = _record.ChassisNo;
        txtModel.Text = _record.Model;
        txtEngineNo.Text = _record.EngineNo;
        txtCreatedOn.Text = _record.CreatedOn;
        txtUpdatedOn.Text = _record.UpdatedOn;
        txtCustomerName.Text = _record.CustomerName;
        txtCustomerContactNos.Text = _record.CustomerContactNos;
        txtCustomerAddress.Text = _record.CustomerAddress;
        txtFinanceName.Text = _record.Financer;
        txtBranchName.Text = _record.BranchName;
        txtBranchFirstContactDetails.Text = _record.FirstContactDetails;
        txtBranchSecondContactDetails.Text = _record.SecondContactDetails;
        txtBranchThirdContactDetails.Text = _record.ThirdContactDetails;

        try
        {
            var response = await App.HttpClient.GetFromJsonAsync<UsersDashboardResponse>($"{App.ApiBaseUrl}api/AppUsers");
            if (response != null && response.Users != null)
            {
                cmbSeizer.ItemsSource = response.Users.Select(u => new SeizerDisplayItem
                {
                    UserId = u.UserId,
                    FullName = u.FullName,
                    Display = $"{u.FullName} - {u.MobileNo}"
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load users: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void btnSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (cmbSeizer.SelectedItem == null)
        {
            MessageBox.Show("Please select a Seizer.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // selectedUser is anonymous type, so we use dynamic
        var selectedUser = (SeizerDisplayItem)cmbSeizer.SelectedItem;
        var status = (cmbStatus.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Pending";

        decimal.TryParse(txtAmountCredited.Text, out decimal amountCredited);

        var request = new ConfirmationRequest
        {
            VehicleNo = txtVehicleNo.Text,
            ChassisNo = txtChassisNo.Text,
            Model = txtModel.Text,
            EngineNo = txtEngineNo.Text,
            CustomerName = txtCustomerName.Text,
            CustomerContactNos = txtCustomerContactNos.Text,
            CustomerAddress = txtCustomerAddress.Text,
            FinanceName = txtFinanceName.Text,
            BranchName = txtBranchName.Text,
            BranchFirstContactDetails = txtBranchFirstContactDetails.Text,
            BranchSecondContactDetails = txtBranchSecondContactDetails.Text,
            BranchThirdContactDetails = txtBranchThirdContactDetails.Text,

            SeizerId = selectedUser.UserId,
            SeizerName = selectedUser.FullName,
            VehicleContainsLoad = chkContainsLoad.IsChecked ?? false,
            LoadDescription = txtLoadDescription.Text,
            ConfirmBy = txtConfirmBy.Text,
            Status = status,
            Yard = txtYard.Text,
            ApplyAmtCredited = chkApplyAmtCredited.IsChecked ?? false,
            AmountCredited = amountCredited
        };

        try
        {
            btnSubmit.IsEnabled = false;
            var response = await App.HttpClient.PostAsJsonAsync($"{App.ApiBaseUrl}api/Confirmations", request);
            response.EnsureSuccessStatusCode();

            MessageBox.Show("Confirmation submitted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to submit confirmation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            btnSubmit.IsEnabled = true;
        }
    }
}
