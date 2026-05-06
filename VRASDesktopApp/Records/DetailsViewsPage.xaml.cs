using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Records;

public partial class DetailsViewsPage : Page
{
    private static readonly TimeSpan DashboardCacheTtl = TimeSpan.FromSeconds(30);
    private static DetailsDashboardResponse? _cachedDashboard;
    private static DateTime _cachedAtUtc;

    public DetailsViewsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        if (TryApplyCachedDashboard())
        {
            return;
        }

        try
        {
            var dashboard = await App.HttpClient.GetFromJsonAsync<DetailsDashboardResponse>(
                $"{App.ApiBaseUrl}api/DetailsViews");

            if (dashboard == null)
            {
                return;
            }

            _cachedDashboard = dashboard;
            _cachedAtUtc = DateTime.UtcNow;
            ApplyDashboard(dashboard);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load details dashboard: {ex.Message}", "Details Views", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsCacheFresh()
    {
        if (_cachedDashboard == null)
        {
            return false;
        }

        return (DateTime.UtcNow - _cachedAtUtc) <= DashboardCacheTtl;
    }

    private bool TryApplyCachedDashboard()
    {
        if (!IsCacheFresh())
        {
            return false;
        }

        ApplyDashboard(_cachedDashboard!);
        return true;
    }

    private void ApplyDashboard(DetailsDashboardResponse dashboard)
    {
        lblViews.Text = dashboard.TotalViews.ToString("N0");
        lblFound.Text = dashboard.FoundCount.ToString("N0");
        lblNotFound.Text = dashboard.NotFoundCount.ToString("N0");
        lblUsers.Text = dashboard.UniqueUsers.ToString("N0");

        dgItems.ItemsSource = dashboard.Items;
        dgLocations.ItemsSource = dashboard.Locations;
    }

    private void dgItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = dgItems.SelectedItem;
        if (item == null)
        {
            brDetails.Visibility = Visibility.Collapsed;
            return;
        }

        // Fill detail fields using reflection to tolerate anonymous types from JSON
        string vehicle = GetProp(item, "VehicleNo");
        string user = GetProp(item, "UserName");
        string mobile = GetProp(item, "UserMobile");
        string status = GetProp(item, "VehicleStatus");
        string location = GetProp(item, "Location");
        string viewed = GetProp(item, "CreatedOn");

        txtVehicleNo.Text = vehicle ?? "-";
        txtUserName.Text = user ?? "-";
        txtMobile.Text = mobile ?? "-";
        txtStatus.Text = status ?? "-";
        txtLocation.Text = location ?? "-";
        txtViewed.Text = viewed ?? "-";
        brDetails.Visibility = Visibility.Visible;
    }

    private void dgItems_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgItems.SelectedItem == null) return;
        // For now, double-click just ensures details pane is visible (could open full detail window)
        dgItems_SelectionChanged(sender, null);
    }

    private static string GetProp(object o, string name)
    {
        try
        {
            var prop = o.GetType().GetProperty(name);
            if (prop != null)
            {
                var v = prop.GetValue(o);
                return v?.ToString();
            }
        }
        catch { }
        return null;
    }
}
