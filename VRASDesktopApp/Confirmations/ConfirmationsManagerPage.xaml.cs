using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Confirmations;

public partial class ConfirmationsManagerPage : Page
{
    public ConfirmationsManagerPage()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadConfirmationsAsync();
    }

    private async Task LoadConfirmationsAsync()
    {
        try
        {
            var confirmations = await App.HttpClient.GetFromJsonAsync<List<Confirmation>>(
                $"{App.ApiBaseUrl}api/Confirmations");
            dgConfirmations.ItemsSource = confirmations;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load confirmations: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
