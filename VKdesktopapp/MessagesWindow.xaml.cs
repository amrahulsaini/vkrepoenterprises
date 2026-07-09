using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public partial class MessagesWindow : Window
{
    public MessagesWindow()
    {
        InitializeComponent();
        Loaded += async (_, __) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var d = await DesktopApiClient.GetIntegrationMessagesAsync();
            listMessages.ItemsSource = d.Messages;
            emptyLabel.Visibility = (d.Messages == null || d.Messages.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;
            try { await DesktopApiClient.MarkIntegrationMessagesReadAsync(); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Statements", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void btnRefresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
