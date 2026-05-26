using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VRASDesktopApp.Models;

namespace VRASDesktopApp;

public partial class LoginWindow : Window
{
    // Local cache of the agency's branding. Populated after a successful login;
    // the logo replaces the CRMRS default on the sign-in screen and the name
    // replaces the "CRMS" title on every subsequent launch.
    private static readonly string AgencyCacheDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CRMS");
    private static readonly string AgencyLogoCachePath = System.IO.Path.Combine(AgencyCacheDir, "agency-logo.png");
    private static readonly string AgencyNameCachePath = System.IO.Path.Combine(AgencyCacheDir, "agency-name.txt");

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, __) => LoadCachedAgencyBranding();
    }

    private void LoadCachedAgencyBranding()
    {
        // Name first (cheap), then logo. Each one is independent — a missing
        // file just means we keep the XAML default.
        try
        {
            if (System.IO.File.Exists(AgencyNameCachePath))
            {
                var name = System.IO.File.ReadAllText(AgencyNameCachePath).Trim();
                if (!string.IsNullOrWhiteSpace(name)) lblAppName.Text = name;
            }
        }
        catch { }

        try
        {
            if (!System.IO.File.Exists(AgencyLogoCachePath)) return;
            // BitmapImage with CacheOption=OnLoad reads the bytes immediately
            // so the file isn't locked — important because we overwrite it on
            // every fresh login.
            var bytes = System.IO.File.ReadAllBytes(AgencyLogoCachePath);
            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            imgLogo.Source = bmp;
        }
        catch { /* fall back to the CRMRS default in XAML */ }
    }

    /// <summary>Downloads and caches the signed-in agency's logo + name so the
    /// next sign-in screen shows them instead of the CRMRS defaults.</summary>
    private static async System.Threading.Tasks.Task CacheAgencyBrandingAsync(string agencyName, string logoPath)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AgencyCacheDir);
            if (!string.IsNullOrWhiteSpace(agencyName))
                await System.IO.File.WriteAllTextAsync(AgencyNameCachePath, agencyName.Trim());
        }
        catch { }

        if (string.IsNullOrWhiteSpace(logoPath)) return;
        try
        {
            var url = logoPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? logoPath
                : App.ApiBaseUrl.TrimEnd('/') + "/" + logoPath.TrimStart('/');
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var bytes = await http.GetByteArrayAsync(url);
            await System.IO.File.WriteAllBytesAsync(AgencyLogoCachePath, bytes);
        }
        catch { /* leave the cached logo unchanged on failure */ }
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

        // The very first HTTPS call to a host after process start often takes
        // a few hundred ms longer (DNS + cold TLS handshake) and occasionally
        // bubbles up a SocketException on flaky networks. Retry once with a
        // fresh content/token so the user doesn't have to click Sign In twice.
        HttpResponseMessage response = null!;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                response = await App.HttpClient.PostAsync(
                    App.ApiBaseUrl + "api/agency/desktop/login",
                    JsonContent.Create(formData),
                    cts.Token);
                break;
            }
            catch (OperationCanceledException) when (attempt < 2)
            {
                // first attempt timed out — retry once
                continue;
            }
            catch (HttpRequestException rex) when (rex.InnerException is System.Net.Sockets.SocketException && attempt < 2)
            {
                // first attempt couldn't open a socket — retry once
                continue;
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "";
                MessageBox.Show("The server didn't respond in time. Check your internet connection and try again.",
                    "Connection Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (HttpRequestException rex) when (rex.InnerException is System.Net.Sockets.SocketException)
            {
                lblStatus.Text = "";
                MessageBox.Show("Cannot reach the server. Please check your internet connection and that the API URL in settings is correct.",
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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

        // Fire-and-forget — the cached logo + name are used on the NEXT
        // launch, so we don't block the sign-in flow waiting for them.
        _ = CacheAgencyBrandingAsync(signed.AgencyName, signed.LogoPath);

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
