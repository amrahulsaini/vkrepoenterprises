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
        await LoadOverviewAsync();
    }

    private async Task LoadOverviewAsync()
    {
        try
        {
            var overview = await App.HttpClient.GetFromJsonAsync<Overview>(
                App.ApiBaseUrl + "api/Overview");

            if (overview != null)
            {
                lblRecords.Text = overview.TotalRecords.ToString("N0");
                lblFinances.Text = overview.TotalFinances.ToString("N0");
                lblUsers.Text = overview.TotalAppUsers.ToString("N0");
                lblConfirmations.Text = overview.TotalConfirmations.ToString("N0");
                lblFeedbacks.Text = overview.TotalFeedbacks.ToString("N0");
            }
        }
        catch
        {
            // Silently fail - show defaults
        }
    }
}
