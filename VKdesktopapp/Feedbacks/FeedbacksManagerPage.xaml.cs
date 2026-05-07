using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Feedbacks;

public partial class FeedbacksManagerPage : Page
{
    public FeedbacksManagerPage()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadFeedbacksAsync();
    }

    private async Task LoadFeedbacksAsync()
    {
        try
        {
            var feedbacks = await App.HttpClient.GetFromJsonAsync<List<Feedback>>(
                $"{App.ApiBaseUrl}api/Feedbacks");
            dgFeedbacks.ItemsSource = feedbacks;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load feedbacks: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
