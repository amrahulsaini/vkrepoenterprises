using System.Windows;
using System.Windows.Input;
using VRASDesktopApp.Data;
using VRASDesktopApp.Properties;

namespace VRASDesktopApp;

public partial class ServerSettingsWindow : Window
{
    /// <summary>Drag the window by its custom title bar.</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && WindowState != WindowState.Maximized)
            DragMove();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    public ServerSettingsWindow()
    {
        InitializeComponent();

        txtApiBaseUrl.Text = Settings.Default.ApiBaseUrl;
        txtApiKey.Text     = Settings.Default.ApiKey;
        txtFirmName.Text   = Settings.Default.FirmName;
        txtContactNos.Text = Settings.Default.ContactNos;
        txtAddress.Text    = Settings.Default.Address;

        Loaded += async (_, __) =>
        {
            try
            {
                var pass = await DesktopApiClient.GetSubsPasswordAsync();
                pwdSubsPass.Password = pass;
            }
            catch { /* silent — server may not have the table yet */ }
        };
    }

    private async void btnSave_Click(object sender, RoutedEventArgs e)
    {
        Settings.Default.ApiBaseUrl = txtApiBaseUrl.Text;
        Settings.Default.ApiKey     = txtApiKey.Text;
        Settings.Default.FirmName   = txtFirmName.Text;
        Settings.Default.ContactNos = txtContactNos.Text;
        Settings.Default.Address    = txtAddress.Text;
        Settings.Default.Save();

        // Save subs password to server
        var subsPass = pwdSubsPass.Password.Trim();
        if (!string.IsNullOrEmpty(subsPass))
        {
            try { await DesktopApiClient.SetSubsPasswordAsync(subsPass); }
            catch { /* non-fatal — local settings still saved */ }
        }

        MessageBox.Show("Settings saved successfully.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e) => Close();
}
