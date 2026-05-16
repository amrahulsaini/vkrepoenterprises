using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Records;

public partial class UploadRecordsPage : Page
{
    private RecordsEditorWindow? _recordsEditorWindow;

    public UploadRecordsPage()
    {
        InitializeComponent();
    }

    private void btnUploadUsingExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_recordsEditorWindow == null)
        {
            // The actual dashboard window hosting this page (NOT
            // Application.Current.MainWindow, which still points at the
            // closed login window).
            var dashboard = Window.GetWindow(this);

            _recordsEditorWindow = new RecordsEditorWindow();
            _recordsEditorWindow.Closed += (_, __) =>
            {
                _recordsEditorWindow = null;
                // Editor closed → bring the dashboard back.
                if (dashboard != null)
                {
                    dashboard.Show();
                    if (dashboard.WindowState == WindowState.Minimized)
                        dashboard.WindowState = WindowState.Maximized;
                    dashboard.Activate();
                }
            };
            _recordsEditorWindow.Show();
            // One window on screen at a time — hide the dashboard while the
            // editor is open. Sidesteps unreliable taskbar switching between
            // borderless maximized windows.
            dashboard?.Hide();
        }
        else
        {
            _recordsEditorWindow.Activate();
        }
    }

    private void btnAddMergeManually_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Open manual merge editor (not implemented).", "Upload Records", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            var dashboard = await App.HttpClient.GetFromJsonAsync<UploadsDashboardResponse>(
                $"{App.ApiBaseUrl}api/Uploads");

            if (dashboard == null)
            {
                return;
            }

            lblFiles.Text = dashboard.TotalFiles.ToString("N0");
            lblBanks.Text = dashboard.TotalBanks.ToString("N0");
            lblHeaders.Text = dashboard.TotalHeaders.ToString("N0");
            lblLatestUpload.Text = string.IsNullOrWhiteSpace(dashboard.LatestUpload) ? "-" : dashboard.LatestUpload;

            dgFiles.ItemsSource = dashboard.Files;
            dgBanks.ItemsSource = dashboard.Banks;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load upload dashboard: {ex.Message}", "Uploads", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
