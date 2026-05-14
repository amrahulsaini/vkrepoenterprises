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

    /// <summary>Allow dragging the window by the title bar.</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private async void btnLogin_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtMobileNo.Text) || txtMobileNo.Text.Length < 10)
        {
            MessageBox.Show("Please enter a valid 10-digit mobile number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(txtPassword.Password))
        {
            MessageBox.Show("Please enter your password.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            try
            {
                var logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                System.IO.Directory.CreateDirectory(logDir);
                var logFile = System.IO.Path.Combine(logDir, $"error_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                System.IO.File.WriteAllText(logFile, ex.ToString());
            }
            catch { }
            MessageBox.Show($"Login failed:\n\n{ex}\n\nFull details written to 'logs' folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                password = txtPassword.Password
            };

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
            HttpResponseMessage response;
            try
            {
                response = await App.HttpClient.PostAsync(
                    App.ApiBaseUrl + "api/AppUsers/Login",
                    JsonContent.Create(formData),
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "";
                MessageBox.Show("Cannot connect to the server. Please check that the server is running and the API URL is correct.", "Connection Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (HttpRequestException rex) when (rex.InnerException is System.Net.Sockets.SocketException)
            {
                lblStatus.Text = "";
                MessageBox.Show("Cannot reach the server. Please check the API URL in settings.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            response.EnsureSuccessStatusCode();

            App.SignedAppUser = await response.Content.ReadFromJsonAsync<SignedAppUser>();
            App.SetAuthToken(App.SignedAppUser?.Token ?? "");

            // Construct the main window first to catch any initialization/XAML errors
            MainWindow window;
            try
            {
                window = new MainWindow();
            }
            catch (Exception)
            {
                // If main window fails to construct, do not hide the login window — let outer catch handle logging
                throw;
            }

            // Only hide the login window after the main window is successfully constructed.
            Hide();
            try
            {
                window.ShowDialog();
            }
            finally
            {
                // Ensure the login window is visible again if the main window closes or if ShowDialog throws.
                Show();
            }
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                MessageBox.Show("Invalid mobile number or password.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
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
