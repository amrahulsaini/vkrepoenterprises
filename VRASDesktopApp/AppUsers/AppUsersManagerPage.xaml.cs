using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.AppUsers;

public partial class AppUsersManagerPage : Page
{
    public AppUsersManagerPage()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await App.HttpClient.GetFromJsonAsync<List<AppUserListItem>>(
                $"{App.ApiBaseUrl}api/AppUsers");
            dgUsers.ItemsSource = users;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load users: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
