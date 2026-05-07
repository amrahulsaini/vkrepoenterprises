using System.Windows;
using VRASDesktopApp.Properties;

namespace VRASDesktopApp;

public partial class ServerSettingsWindow : Window
{
    public ServerSettingsWindow()
    {
        InitializeComponent();

        // Load current settings
        txtApiBaseUrl.Text = Settings.Default.ApiBaseUrl;
        txtApiKey.Text = Settings.Default.ApiKey;
        txtFirmName.Text = Settings.Default.FirmName;
        txtContactNos.Text = Settings.Default.ContactNos;
        txtAddress.Text = Settings.Default.Address;
    }

    private void btnSave_Click(object sender, RoutedEventArgs e)
    {
        Settings.Default.ApiBaseUrl = txtApiBaseUrl.Text;
        Settings.Default.ApiKey = txtApiKey.Text;
        Settings.Default.FirmName = txtFirmName.Text;
        Settings.Default.ContactNos = txtContactNos.Text;
        Settings.Default.Address = txtAddress.Text;
        Settings.Default.Save();

        MessageBox.Show("Settings saved successfully.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
