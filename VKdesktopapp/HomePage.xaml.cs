using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp;

public partial class HomePage : Page
{
    public HomePage()
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
            var dashboard = await App.HttpClient.GetFromJsonAsync<HomeDashboardResponse>(
                $"{App.ApiBaseUrl}api/Overview");

            if (dashboard == null)
            {
                return;
            }

            lblRecords.Text = dashboard.Overview.TotalRecords.ToString("N0");
            lblBranches.Text = dashboard.Overview.TotalBranches.ToString("N0");
            lblHeadOffices.Text = $"{dashboard.Overview.TotalHeadOffices:N0} head offices";
            lblUsers.Text = dashboard.Overview.TotalUsers.ToString("N0");
            lblUserStatus.Text = $"{dashboard.Overview.ActiveUsers:N0} active • {dashboard.Overview.AdminUsers:N0} admin";
            lblDetailViews.Text = dashboard.Overview.TotalDetailViews.ToString("N0");
            lblFoundDetails.Text = $"{dashboard.Overview.FoundDetails:N0} marked FOUND";
            lblUploads.Text = dashboard.Overview.TotalUploads.ToString("N0");
            lblOtps.Text = $"{dashboard.Overview.TotalOtps:N0} OTP records";
            lblBillings.Text = dashboard.Overview.TotalBillings.ToString("N0");

            dgUploads.ItemsSource = dashboard.RecentUploads;
            dgCollections.ItemsSource = dashboard.Collections;
            dgDetails.ItemsSource = dashboard.RecentDetails;
            dgBranches.ItemsSource = dashboard.TopBranches;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load home dashboard: {ex.Message}", "Dashboard", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
