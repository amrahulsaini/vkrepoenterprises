using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.AppUsers;

public partial class AppUsersManagerPage : Page
{
    public AppUsersManagerPage()
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
            var dashboard = await App.HttpClient.GetFromJsonAsync<UsersDashboardResponse>(
                $"{App.ApiBaseUrl}api/AppUsers");

            if (dashboard == null)
            {
                return;
            }

            lblUsers.Text = dashboard.TotalUsers.ToString("N0");
            lblActive.Text = dashboard.ActiveUsers.ToString("N0");
            lblAdmin.Text = dashboard.AdminUsers.ToString("N0");
            lblPlans.Text = dashboard.TotalPlans.ToString("N0");
            lblDevices.Text = dashboard.RegisteredDevices.ToString("N0");

            dgUsers.ItemsSource = dashboard.Users;
            dgPlans.ItemsSource = dashboard.PlanAlerts;
            dgDevices.ItemsSource = dashboard.Users.Where(user =>
                !string.IsNullOrWhiteSpace(user.DeviceId) || !string.IsNullOrWhiteSpace(user.RequestDeviceId));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load user dashboard: {ex.Message}", "Users", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
