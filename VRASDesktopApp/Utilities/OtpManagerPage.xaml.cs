using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Utilities;

public partial class OtpManagerPage : Page
{
    public OtpManagerPage()
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
            var dashboard = await App.HttpClient.GetFromJsonAsync<OtpDashboardResponse>(
                $"{App.ApiBaseUrl}api/OTPs");

            if (dashboard == null)
            {
                return;
            }

            lblOtps.Text = dashboard.TotalOtps.ToString("N0");
            lblUsers.Text = dashboard.TotalUsers.ToString("N0");
            lblRecent.Text = dashboard.Last24Hours.ToString("N0");
            dgOtps.ItemsSource = dashboard.Items;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load OTP dashboard: {ex.Message}", "OTPs", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
