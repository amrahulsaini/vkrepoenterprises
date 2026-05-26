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
    // Local cache of the agency's logo. Populated after a successful login;
    // shown on every subsequent launch's sign-in screen in place of the
    // default CRMRS logo.
    private static readonly string AgencyLogoCachePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CRMS", "agency-logo.png");

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, __) => LoadCachedAgencyLogo();
    }

    private void LoadCachedAgencyLogo()
    {
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

    /// <summary>Downloads and caches the signed-in agency's logo so the next
    /// sign-in screen shows it instead of the CRMRS default.</summary>
    private static async System.Threading.Tasks.Task CacheAgencyLogoAsync(string logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath)) return;
        try
        {
            // logoPath is the server-relative path stored on the agency record,
            // e.g. "/agency-uploads/rk_enterprises.jpg". Prepend the API host.
            var url = logoPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? logoPath
                : App.ApiBaseUrl.TrimEnd('/') + "/" + logoPath.TrimStart('/');
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var bytes = await http.GetByteArrayAsync(url);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AgencyLogoCachePath)!);
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

        // Fire-and-forget — the cached logo is used on the NEXT launch,
        // so we don't block the sign-in flow waiting for it.
        _ = CacheAgencyLogoAsync(signed.LogoPath);

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
