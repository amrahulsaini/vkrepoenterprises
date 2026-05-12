using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VRASDesktopApp.Data;

namespace VRASDesktopApp;

public partial class HomePage : Page
{
    private DispatcherTimer? _refreshTimer;
    private bool _mapReady = false;
    private List<DesktopApiClient.LiveUserDto> _lastLiveUsers = new();

    private record DeviceRequestRow(
        long Id, long UserId,
        string UserName, string UserMobile,
        string NewDeviceId, string RequestedAt)
    {
        public string Initials => string.Concat(
            UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Take(2)
                    .Select(w => char.ToUpper(w[0]).ToString()));
        public string ReqLabel => $"Req. On: {RequestedAt}";
    }

    public HomePage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate time pickers
        for (int h = 1; h <= 12; h++)
            cmbHour.Items.Add(h.ToString("D2"));
        for (int m = 0; m < 60; m += 5)
            cmbMinute.Items.Add(m.ToString("D2"));
        cmbAmPm.Items.Add("AM");
        cmbAmPm.Items.Add("PM");

        // Default: 12:00 AM = midnight = show all of today
        cmbHour.SelectedItem   = "12";
        cmbMinute.SelectedItem = "00";
        cmbAmPm.SelectedItem   = "AM";

        await InitMapAsync();
        await LoadDashboardAsync();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += async (_, _) => await LoadDashboardAsync();
        _refreshTimer.Start();
    }

    private async Task InitMapAsync()
    {
        try
        {
            await mapView.EnsureCoreWebView2Async(null);

            var publicDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "public");
            if (Directory.Exists(publicDir))
            {
                mapView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "vkapp.local", publicDir,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                mapView.CoreWebView2.Navigate("http://vkapp.local/map_live.html");
            }
            else
            {
                mapView.CoreWebView2.NavigateToString(FallbackMapHtml);
            }

            // Don't await — polling runs in background so LoadDashboardAsync starts immediately
            _ = PollForMapReadyAsync();
        }
        catch
        {
            // WebView2 runtime not installed — map panel will be blank
        }
    }

    // Polls every 250 ms until Leaflet has finished loading and updateMarkers is callable.
    // Needed because window.load postMessage is unreliable when the CDN script is slow.
    private async Task PollForMapReadyAsync()
    {
        for (int i = 0; i < 80; i++)   // up to 20 s
        {
            await Task.Delay(250);
            try
            {
                var result = await mapView.CoreWebView2.ExecuteScriptAsync(
                    "typeof updateMarkers === 'function'");
                if (result == "true")
                {
                    _mapReady = true;
                    PushMarkersToMap(_lastLiveUsers);
                    return;
                }
            }
            catch { }
        }
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) =>
        await LoadDashboardAsync();

    private async Task LoadDashboardAsync()
    {
        // Each call is individually wrapped — a 404 or network error shows 0/empty
        // instead of crashing the whole dashboard.
        var stats    = new DesktopApiClient.DashboardStatsDto(0, 0, 0);
        var uStats   = new DesktopApiClient.MgrStatsDto(0, 0, 0, 0);
        var requests = new List<DesktopApiClient.DeviceRequestDto>();
        var live     = new List<DesktopApiClient.LiveUserDto>();

        var since = GetSince24h();
        await Task.WhenAll(
            Safe(async () => stats    = await DesktopApiClient.GetDashboardStatsAsync()),
            Safe(async () => uStats   = await DesktopApiClient.GetUserStatsAsync()),
            Safe(async () => requests = await DesktopApiClient.GetDeviceRequestsAsync()),
            Safe(async () => live     = await DesktopApiClient.GetLiveUsersAsync(since))
        );

        // Records / Finances / Branches
        lblRecords.Text  = stats.TotalRecords.ToString("N0");
        lblFinances.Text = stats.TotalFinances.ToString("N0");
        lblBranches.Text = stats.TotalBranches.ToString("N0");

        // App users
        var seizers = Math.Max(0, uStats.Total - uStats.Admins);
        lblSeizers.Text     = seizers.ToString("N0");
        lblActiveUsers.Text = uStats.Active.ToString("N0");
        lblAdmins.Text      = uStats.Admins.ToString("N0");

        // Device requests
        var reqRows = requests
            .Select(r => new DeviceRequestRow(r.Id, r.UserId, r.UserName, r.UserMobile, r.NewDeviceId, r.RequestedAt))
            .ToList();
        lvDeviceRequests.ItemsSource = reqRows;
        lblDeviceReqCount.Text       = reqRows.Count.ToString();
        txtNoRequests.Visibility     = reqRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        lvDeviceRequests.Visibility  = reqRows.Count > 0  ? Visibility.Visible : Visibility.Collapsed;

        // Live users
        _lastLiveUsers = live;
        lblLiveCount.Text = since == "00:00" || string.IsNullOrWhiteSpace(since)
            ? $"{live.Count} users seen today"
            : $"{live.Count} users seen since {since}";
        PushMarkersToMap(live);
    }

    private string GetSince24h()
    {
        if (cmbHour.SelectedItem is not string hStr   || !int.TryParse(hStr, out var h)) return "00:00";
        if (cmbMinute.SelectedItem is not string mStr || !int.TryParse(mStr, out var m)) return "00:00";
        var isPm = cmbAmPm.SelectedItem as string == "PM";
        var hour24 = h == 12 ? (isPm ? 12 : 0) : (isPm ? h + 12 : h);
        return $"{hour24:D2}:{m:D2}";
    }

    private static async Task Safe(Func<Task> fn)
    {
        try { await fn(); } catch { }
    }

    private void PushMarkersToMap(List<DesktopApiClient.LiveUserDto> users)
    {
        if (!_mapReady || mapView.CoreWebView2 == null) return;
        var payload = users
            .Where(u => u.Lat.HasValue && u.Lng.HasValue)
            .Select(u => new { name = u.Name, mobile = u.Mobile, lastSeen = u.LastSeen, lat = u.Lat!.Value, lng = u.Lng!.Value });
        var json = JsonSerializer.Serialize(payload);
        _ = mapView.CoreWebView2.ExecuteScriptAsync($"updateMarkers({json})");
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
            $"Deny device change request for {row.UserName}?",
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

    // Shown if map_live.html is missing from the output directory
    private const string FallbackMapHtml =
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>" +
        "<style>html,body{width:100%;height:100%;margin:0;display:flex;align-items:center;" +
        "justify-content:center;background:#f5f5f5;font-family:Segoe UI,sans-serif;color:#888;}</style>" +
        "</head><body><div>Map file not found — rebuild the project.</div></body></html>";
}
