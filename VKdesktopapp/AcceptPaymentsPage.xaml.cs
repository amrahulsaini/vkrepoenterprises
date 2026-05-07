using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp;

public partial class AcceptPaymentsPage : Page
{
    public AcceptPaymentsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            var dashboard = await App.HttpClient.GetFromJsonAsync<PaymentsDashboardResponse>(
                $"{App.ApiBaseUrl}api/Payments");

            if (dashboard == null)
            {
                return;
            }

            lblBanks.Text = dashboard.TotalBanks.ToString("N0");
            lblBillings.Text = dashboard.TotalBillings.ToString("N0");
            lblUploads.Text = dashboard.TotalUploads.ToString("N0");
            lblStatusNote.Text = dashboard.StatusNote;

            cmbPaymentMethod.ItemsSource = dashboard.PaymentMethods;
            dgMethods.ItemsSource = dashboard.PaymentMethods;
            dgUploads.ItemsSource = dashboard.RecentUploads;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load payments dashboard: {ex.Message}", "Payments", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
