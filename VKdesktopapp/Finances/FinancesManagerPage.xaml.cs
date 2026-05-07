using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Finances;

public partial class FinancesManagerPage : Page
{
    public FinancesManagerPage()
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
            var dashboard = await App.HttpClient.GetFromJsonAsync<FinanceDashboardResponse>(
                $"{App.ApiBaseUrl}api/Finances");

            if (dashboard == null)
            {
                return;
            }

            lblHeadOffices.Text = dashboard.TotalHeadOffices.ToString("N0");
            lblBranches.Text = dashboard.TotalBranches.ToString("N0");
            lblRecords.Text = dashboard.TotalRecords.ToString("N0");
            lblUploads.Text = dashboard.TotalUploads.ToString("N0");

            dgBranches.ItemsSource = dashboard.TopBranches;
            dgUploads.ItemsSource = dashboard.RecentUploads;
            dgBanks.ItemsSource = dashboard.Banks;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load finance dashboard: {ex.Message}", "Finances", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
