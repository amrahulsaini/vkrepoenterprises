using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;
using VRASDesktopApp.Models;
using VRASDesktopApp.Properties;

namespace VRASDesktopApp;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        lblFirmName.Text = App.Firm.FirmName;
        lblFirmMobile.Text = App.Firm.ContactNos;
        lblFirmAddress.Text = App.Firm.Address;
    }

    private async void btnLogin_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtMobileNo.Text) || txtMobileNo.Text.Length < 10)
        {
            MessageBox.Show("Please enter a valid 10-digit mobile number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnLogin.IsEnabled = false;
        lblStatus.Text = "Signing in...";

        try
        {
            await Login();
        }
        catch (Exception ex)
        {
            lblStatus.Text = "";
            MessageBox.Show($"Login failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnLogin.IsEnabled = true;
        }
    }

    public async Task Login()
    {
        try
        {
            var formData = new
            {
                mobileno = txtMobileNo.Text,
                password = App.ApiKey
            };

            HttpResponseMessage response = await App.HttpClient.PostAsync(
                App.ApiBaseUrl + "api/AppUsers/Login",
                JsonContent.Create(formData));

            response.EnsureSuccessStatusCode();

            App.SignedAppUser = await response.Content.ReadFromJsonAsync<SignedAppUser>();
            App.SetAuthToken(App.SignedAppUser?.Token ?? "");

            Hide();
            MainWindow window = new MainWindow();
            window.ShowDialog();
            Show();
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                MessageBox.Show("Mobile number and this device do not match.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show($"Server error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            btnLogin_Click(sender, e);
        }
    }
}
