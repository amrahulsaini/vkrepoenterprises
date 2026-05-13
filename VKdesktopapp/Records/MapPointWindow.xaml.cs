using System;
using System.IO;
using System.Windows;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Records;

public partial class MapPointWindow : Window
{
    private readonly DesktopApiClient.SearchLogRow _row;

    public MapPointWindow(DesktopApiClient.SearchLogRow row)
    {
        InitializeComponent();
        _row = row;
        txtTitle.Text    = $"{row.VehicleNo}  —  {row.UserName}";
        txtSubtitle.Text = $"{row.Model}  |  {row.ServerTime}  |  {row.UserMobile}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
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

                var qs = BuildQueryString();
                mapView.CoreWebView2.Navigate($"http://vkapp.local/map_point.html{qs}");
            }
        }
        catch { /* WebView2 not installed */ }
    }

    private string BuildQueryString()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (_row.Lat.HasValue) parts.Add($"lat={_row.Lat.Value:F6}");
        if (_row.Lng.HasValue) parts.Add($"lng={_row.Lng.Value:F6}");
        parts.Add($"name={Uri.EscapeDataString(_row.UserName)}");
        parts.Add($"mobile={Uri.EscapeDataString(_row.UserMobile)}");
        parts.Add($"vrn={Uri.EscapeDataString(_row.VehicleNo)}");
        parts.Add($"chassis={Uri.EscapeDataString(_row.ChassisNo)}");
        parts.Add($"model={Uri.EscapeDataString(_row.Model)}");
        parts.Add($"time={Uri.EscapeDataString(_row.ServerTime)}");
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }
}
