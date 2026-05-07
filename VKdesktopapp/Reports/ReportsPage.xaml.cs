using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Reports;

public partial class ReportsPage : Page
{
    public ReportsPage()
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
            var dashboard = await App.HttpClient.GetFromJsonAsync<ReportsDashboardResponse>(
                $"{App.ApiBaseUrl}api/Reports");

            if (dashboard == null)
            {
                return;
            }

            lblVehicles.Text = dashboard.TotalVehicles.ToString("N0");
            lblUploads.Text = dashboard.TotalUploads.ToString("N0");
            lblBranches.Text = dashboard.TotalBranches.ToString("N0");
            lblUsers.Text = dashboard.TotalUsers.ToString("N0");

            dgCollections.ItemsSource = dashboard.Collections;
            dgBranches.ItemsSource = dashboard.TopBranches;
            dgBanks.ItemsSource = dashboard.TopBanks;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load reports dashboard: {ex.Message}", "Reports", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
