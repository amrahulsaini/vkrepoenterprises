using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public partial class HomePage : Page
{
    private DispatcherTimer? _refreshTimer;
    private bool _mapReady = false;
    private bool _mapInitStarted = false;
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
        for (int h = 1; h <= 12; h++)
            cmbHour.Items.Add(h.ToString("D2"));
        for (int m = 0; m < 60; m += 5)
            cmbMinute.Items.Add(m.ToString("D2"));
        cmbAmPm.Items.Add("AM");
        cmbAmPm.Items.Add("PM");

        var istZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
        var istNow  = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);
        var minute5 = (istNow.Minute / 5) * 5;
        var hour12  = istNow.Hour % 12; if (hour12 == 0) hour12 = 12;
        var ampm    = istNow.Hour >= 12 ? "PM" : "AM";
        cmbHour.SelectedItem   = hour12.ToString("D2");
        cmbMinute.SelectedItem = minute5.ToString("D2");
        cmbAmPm.SelectedItem   = ampm;

        await InitMapAsync();
        await LoadDashboardAsync();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += async (_, _) => await LoadDashboardAsync();
        _refreshTimer.Start();
    }

    private string _mapUrl = "";

    private async Task InitMapAsync()
    {
        if (_mapInitStarted) return;
        _mapInitStarted = true;
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VKEnterprises", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                .CreateAsync(null, userDataFolder);
            await mapView.EnsureCoreWebView2Async(env);

            try
            {
                await mapView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache);
            }
            catch { }

            var bust = DateTime.UtcNow.Ticks;
            _mapUrl = $"{App.ApiBaseUrl.TrimEnd('/')}/public/map_live.html?v={bust}";

            mapView.CoreWebView2.NavigationCompleted += (_, ev) =>
            {
                if (ev.IsSuccess) HideMapError();
                else ShowMapError($"Could not load the map page.\n{_mapUrl}\n(WebErrorStatus: {ev.WebErrorStatus})");
            };
            mapView.CoreWebView2.ProcessFailed += (_, ev) =>
                ShowMapError($"The map browser process stopped ({ev.ProcessFailedKind}). Click Retry.");

            mapView.CoreWebView2.Navigate(_mapUrl);
            _ = PollForMapReadyAsync();
        }
        catch (Exception ex)
        {
            ShowMapError("The map component could not start — the WebView2 runtime may be missing.\n\n" + ex.Message);
        }
    }

    private void MapRetry_Click(object sender, RoutedEventArgs e)
    {
        HideMapError();
        if (mapView.CoreWebView2 != null && !string.IsNullOrEmpty(_mapUrl))
        {
            mapView.CoreWebView2.Navigate(_mapUrl);
            _ = PollForMapReadyAsync();
        }
        else
        {
            _mapInitStarted = false;
            _ = InitMapAsync();
        }
    }

    private void ShowMapError(string detail)
    {
        LogMapError(detail);
        try
        {
            if (mapErrorDetail  != null) mapErrorDetail.Text       = detail;
            if (mapErrorOverlay != null) mapErrorOverlay.Visibility = Visibility.Visible;
        }
        catch { }
    }

    private void HideMapError()
    {
        try
        {
            if (mapErrorOverlay != null) mapErrorOverlay.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private static void LogMapError(string msg)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VKEnterprises");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "map-errors.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
        }
        catch { }
    }

    private async Task PollForMapReadyAsync()
    {
        for (int i = 0; i < 80; i++)
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

        lblRecords.Text  = stats.TotalRecords.ToString("N0");
        lblFinances.Text = stats.TotalFinances.ToString("N0");
        lblBranches.Text = stats.TotalBranches.ToString("N0");

        var seizers = Math.Max(0, uStats.Total - uStats.Admins);
        lblSeizers.Text     = seizers.ToString("N0");
        lblActiveUsers.Text = uStats.Active.ToString("N0");
        lblAdmins.Text      = uStats.Admins.ToString("N0");

        var reqRows = requests
            .Select(r => new DeviceRequestRow(r.Id, r.UserId, r.UserName, r.UserMobile, r.NewDeviceId, r.RequestedAt))
            .ToList();
        lvDeviceRequests.ItemsSource = reqRows;
        lblDeviceReqCount.Text       = reqRows.Count.ToString();
        txtNoRequests.Visibility     = reqRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        lvDeviceRequests.Visibility  = reqRows.Count > 0  ? Visibility.Visible : Visibility.Collapsed;

        _lastLiveUsers = live;
        var pinned = live.Count(HasRealLocation);
        var noLoc  = live.Count - pinned;
        var period = since == "00:00" || string.IsNullOrWhiteSpace(since) ? "today" : $"since {since}";
        lblLiveCount.Text = noLoc > 0
            ? $"{live.Count} users seen {period}  •  {pinned} pinned  •  {noLoc} no location yet"
            : $"{live.Count} users seen {period}  •  {pinned} pinned";
        PushMarkersToMap(live);
    }

    private static bool HasRealLocation(DesktopApiClient.LiveUserDto u) =>
        u.Lat.HasValue && u.Lng.HasValue && u.Lat.Value != 0 && u.Lng.Value != 0;

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
            .Where(HasRealLocation)
            .Select(u => new {
                name = u.Name, mobile = u.Mobile, lastSeen = u.LastSeen,
                lat = u.Lat!.Value, lng = u.Lng!.Value,
                pfp = ResolvePfpUrl(u.Pfp)
            });
        var json = JsonSerializer.Serialize(payload);
        _ = mapView.CoreWebView2.ExecuteScriptAsync($"updateMarkers({json})");
    }

    private static string ResolvePfpUrl(string? pfp)
    {
        if (string.IsNullOrWhiteSpace(pfp)) return "";
        var p = pfp.Trim();
        if (p.StartsWith("http://") || p.StartsWith("https://") || p.StartsWith("data:"))
            return p;
        if (p.Length > 200 && !p.Contains('/'))
            return "data:image/jpeg;base64," + p;
        var baseUrl = App.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/uploads/{p.TrimStart('/')}";
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

}
