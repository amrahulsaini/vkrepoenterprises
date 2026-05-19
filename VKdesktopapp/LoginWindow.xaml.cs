using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using VRASDesktopApp.Models;

namespace VRASDesktopApp;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    /// <summary>Allow dragging the window by the title bar.</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private async void btnLogin_Click(object sender, RoutedEventArgs e)
    {
        var email = txtEmail.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || !email.Contains('.'))
        {
            MessageBox.Show("Please enter a valid email address.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            txtEmail.Focus();
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
        var formData = new
        {
            email    = txtEmail.Text.Trim().ToLowerInvariant(),
            password = txtPassword.Password
        };

        // Drop any stale Bearer token from a previous session before signing in,
        // so the tenant-routing middleware never rejects the login request itself.
        App.HttpClient.DefaultRequestHeaders.Authorization = null;

        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
        HttpResponseMessage response;
        try
        {
            response = await App.HttpClient.PostAsync(
                App.ApiBaseUrl + "api/agency/desktop/login",
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

        if (!response.IsSuccessStatusCode)
        {
            lblStatus.Text = "";
            string msg = "Sign in failed. Please check your email and password.";
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var m))
                    msg = m.GetString() ?? msg;
            }
            catch { }
            MessageBox.Show(msg, "Sign in failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var signed = await response.Content.ReadFromJsonAsync<SignedAppUser>();
        if (signed == null || string.IsNullOrEmpty(signed.Token))
        {
            lblStatus.Text = "";
            MessageBox.Show("Unexpected response from the server. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        App.SignedAppUser = signed;
        App.SetAuthToken(signed.Token);

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
        lblStatus.Text = "";
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
