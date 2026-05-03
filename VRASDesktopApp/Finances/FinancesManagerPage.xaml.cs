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
        Loaded += async (s, e) => await LoadFinancesAsync();
    }

    private async Task LoadFinancesAsync()
    {
        try
        {
            var finances = await App.HttpClient.GetFromJsonAsync<List<Finance>>(
                $"{App.ApiBaseUrl}api/Finances");
            dgFinances.ItemsSource = finances;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load finances: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
