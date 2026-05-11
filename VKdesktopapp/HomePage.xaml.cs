using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VRASDesktopApp.Data;

namespace VRASDesktopApp;

public partial class HomePage : Page
{
    private DispatcherTimer? _refreshTimer;

    private record DeviceRequestRow(long Id, long UserId, string UserName, string UserMobile, string RequestedAt);
    private record LiveUserRow(long Id, string Name, string Mobile, string LastSeen, string GpsStatus);

    public HomePage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDashboardAsync();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += async (_, _) => await LoadDashboardAsync();
        _refreshTimer.Start();
    }

    private async System.Threading.Tasks.Task LoadDashboardAsync()
    {
        try
        {
            var statsTask = DesktopApiClient.GetDashboardStatsAsync();
            var usersTask = DesktopApiClient.GetUserStatsAsync();
            var reqTask   = DesktopApiClient.GetDeviceRequestsAsync();
            var liveTask  = DesktopApiClient.GetLiveUsersAsync();

            await System.Threading.Tasks.Task.WhenAll(statsTask, usersTask, reqTask, liveTask);

            var stats    = statsTask.Result;
            var uStats   = usersTask.Result;
            var requests = reqTask.Result;
            var live     = liveTask.Result;

            lblRecords.Text     = stats.TotalRecords.ToString("N0");
            lblFinances.Text    = stats.TotalFinances.ToString("N0");
            lblBranches.Text    = stats.TotalBranches.ToString("N0");

            var seizers = Math.Max(0, uStats.Total - uStats.Admins);
            lblSeizers.Text     = seizers.ToString("N0");
            lblActiveUsers.Text = uStats.Active.ToString("N0");
            lblAdmins.Text      = uStats.Admins.ToString("N0");

            var reqRows = requests
                .Select(r => new DeviceRequestRow(r.Id, r.UserId, r.UserName, r.UserMobile, r.RequestedAt))
                .ToList();
            lvDeviceRequests.ItemsSource = reqRows;
            lblDeviceReqCount.Text = $"{reqRows.Count} pending";
            txtNoRequests.Visibility    = reqRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            lvDeviceRequests.Visibility = reqRows.Count > 0  ? Visibility.Visible : Visibility.Collapsed;

            var liveRows = live
                .Select(u => new LiveUserRow(
                    u.Id, u.Name, u.Mobile, u.LastSeen,
                    (u.Lat.HasValue && u.Lng.HasValue) ? "📍 GPS" : "No GPS"))
                .ToList();
            lvLiveUsers.ItemsSource = liveRows;
            lblLiveCount.Text = $"{liveRows.Count} online in last 15 min";
            txtNoLiveUsers.Visibility = liveRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            lvLiveUsers.Visibility    = liveRows.Count > 0  ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load dashboard: {ex.Message}", "Dashboard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void AcceptDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DeviceRequestRow row) return;
        var confirm = MessageBox.Show(
            $"Accept device change for {row.UserName} ({row.UserMobile})?",
            "Confirm Accept", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            btn.IsEnabled = false;
            await DesktopApiClient.ApproveDeviceRequestAsync(row.Id);
            await LoadDashboardAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to approve: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            btn.IsEnabled = true;
        }
    }

    private async void DenyDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DeviceRequestRow row) return;
        var confirm = MessageBox.Show(
            $"Deny (delete) device change request for {row.UserName}?",
            "Confirm Deny", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            btn.IsEnabled = false;
            await DesktopApiClient.DenyDeviceRequestAsync(row.Id);
            await LoadDashboardAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to deny: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            btn.IsEnabled = true;
        }
    }
}
