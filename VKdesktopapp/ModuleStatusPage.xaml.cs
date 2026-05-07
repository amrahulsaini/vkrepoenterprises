using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp;

public partial class ModuleStatusPage : Page
{
    private readonly string _moduleKey;
    private readonly string _fallbackTitle;

    public ModuleStatusPage() : this("module", "MODULE")
    {
    }

    public ModuleStatusPage(string moduleKey, string fallbackTitle)
    {
        _moduleKey = moduleKey;
        _fallbackTitle = fallbackTitle;
        InitializeComponent();
        txtTitle.Text = fallbackTitle;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            var dashboard = await App.HttpClient.GetFromJsonAsync<ModuleStatusResponse>(
                $"{App.ApiBaseUrl}api/Modules/{Uri.EscapeDataString(_moduleKey)}");

            if (dashboard == null)
            {
                return;
            }

            txtTitle.Text = string.IsNullOrWhiteSpace(dashboard.Title) ? _fallbackTitle : dashboard.Title.ToUpperInvariant();
            txtSubtitle.Text = string.IsNullOrWhiteSpace(dashboard.Subtitle) ? "Desktop showcase module" : dashboard.Subtitle;
            txtPrimaryLabel.Text = dashboard.Primary.Label.ToUpperInvariant();
            txtPrimaryValue.Text = dashboard.Primary.Value;
            txtPrimaryDescription.Text = dashboard.Primary.Description;
            txtSecondaryLabel.Text = dashboard.Secondary.Label.ToUpperInvariant();
            txtSecondaryValue.Text = dashboard.Secondary.Value;
            txtSecondaryDescription.Text = dashboard.Secondary.Description;
            txtBanner.Text = dashboard.Banner;

            dgHighlights.ItemsSource = dashboard.Highlights;
            dgCollections.ItemsSource = dashboard.Collections;
            dgTimeline.ItemsSource = dashboard.RecentItems;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load module view: {ex.Message}", _fallbackTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
