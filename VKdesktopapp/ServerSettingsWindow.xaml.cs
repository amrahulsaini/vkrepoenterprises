using System.Windows;
using VRASDesktopApp.Data;
using VRASDesktopApp.Properties;

namespace VRASDesktopApp;

public partial class ServerSettingsWindow : Window
{
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
