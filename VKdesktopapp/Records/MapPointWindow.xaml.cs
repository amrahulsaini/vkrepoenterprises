using System;
using System.IO;
using System.Windows;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Records;

public partial class MapPointWindow : Window
{
    private readonly double? _lat, _lng;
    private readonly string _vrn, _chassis, _model, _userName, _userMobile, _serverTime, _address;

    public MapPointWindow(
        string vrn, string chassis, string model,
        string userName, string userMobile, string serverTime,
        double? lat, double? lng, string? address = null)
    {
        InitializeComponent();
        _vrn = vrn; _chassis = chassis; _model = model;
        _userName = userName; _userMobile = userMobile;
        _serverTime = serverTime; _address = address ?? "";
        _lat = lat; _lng = lng;

        txtTitle.Text    = $"{vrn}  —  {userName}";
        var addrPart     = !string.IsNullOrWhiteSpace(address) ? $"  |  {address}" : "";
        txtSubtitle.Text = $"{model}  |  {serverTime}  |  {userMobile}{addrPart}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Shared WebView2 user data folder under LocalAppData (Program Files-safe)
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VKEnterprises", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                .CreateAsync(null, userDataFolder);
            await mapView.EnsureCoreWebView2Async(env);

            // Map HTML is server-hosted so fixes don't require an installer update.
            var baseUrl = App.ApiBaseUrl.TrimEnd('/');
            var qs = BuildQueryString();
            mapView.CoreWebView2.Navigate($"{baseUrl}/public/map_point.html{qs}");
        }
        catch { /* swallow — Window_Loaded handler */ }
    }

    private string BuildQueryString()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (_lat.HasValue) parts.Add($"lat={_lat.Value:F6}");
        if (_lng.HasValue) parts.Add($"lng={_lng.Value:F6}");
        parts.Add($"name={Uri.EscapeDataString(_userName)}");
        parts.Add($"mobile={Uri.EscapeDataString(_userMobile)}");
        parts.Add($"vrn={Uri.EscapeDataString(_vrn)}");
        parts.Add($"chassis={Uri.EscapeDataString(_chassis)}");
        parts.Add($"model={Uri.EscapeDataString(_model)}");
        parts.Add($"time={Uri.EscapeDataString(_serverTime)}");
        if (!string.IsNullOrWhiteSpace(_address))
            parts.Add($"addr={Uri.EscapeDataString(_address)}");
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }
}
