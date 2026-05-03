using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Records;

public partial class FindVehiclePage : Page
{
    public FindVehiclePage()
    {
        InitializeComponent();
    }

    private async void btnSearch_Click(object sender, RoutedEventArgs e)
    {
        string query = txtSearch.Text.Trim();
        if (string.IsNullOrWhiteSpace(query) || query == "Enter vehicle number...")
        {
            MessageBox.Show("Please enter a vehicle number to search.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            btnSearch.IsEnabled = false;
            var results = await App.HttpClient.GetFromJsonAsync<List<RecordListItem>>(
                $"{App.ApiBaseUrl}api/Records/Search?q={Uri.EscapeDataString(query)}");
            dgResults.ItemsSource = results;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Search failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnSearch.IsEnabled = true;
        }
    }
}
