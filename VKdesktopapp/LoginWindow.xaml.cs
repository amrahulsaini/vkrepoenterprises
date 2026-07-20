using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CRMRSDesktopApp.Models;

namespace CRMRSDesktopApp;

public partial class LoginWindow : Window
{
    private static readonly string AgencyCacheDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CRMRS");
    private static readonly string AgencyLogoCachePath = System.IO.Path.Combine(AgencyCacheDir, "agency-logo.png");
    private static readonly string AgencyNameCachePath = System.IO.Path.Combine(AgencyCacheDir, "agency-name.txt");

    private bool _autoLoginTried;

    public LoginWindow()
    {
        InitializeComponent();
        if (Branding.IsTenantBuild)
            lblAppName.Text = Branding.Name;
        Loaded += async (_, __) =>
        {
            LoadCachedAgencyBranding();
            await TryAutoLoginAsync();
        };
    }

    private async Task TryAutoLoginAsync()
    {
        if (_autoLoginTried) return;
        _autoLoginTried = true;

        SavedSession.PurgeLegacy();

        var deviceToken = SavedSession.Load();
        if (string.IsNullOrEmpty(deviceToken)) return;

        btnLogin.IsEnabled = false;
        lblStatus.Text = "Signing in...";
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            var resp = await App.HttpClient.PostAsync(
                App.ApiBaseUrl + "api/agency/desktop/session/resume",
                JsonContent.Create(new { deviceToken }), cts.Token);

            if (!resp.IsSuccessStatusCode)
            {
                // Expired, revoked, or the account password changed.
                SavedSession.Clear();
                GateAccess.ClearAll();
                lblStatus.Text = "";
                return;
            }

            var signed = await resp.Content.ReadFromJsonAsync<SignedAppUser>();
            if (signed == null || string.IsNullOrEmpty(signed.Token))
            {
                SavedSession.Clear();
                lblStatus.Text = "";
                return;
            }

            await EnterAppAsync(signed, deviceToken);
        }
        catch
        {
            lblStatus.Text = "";
        }
        finally
        {
            btnLogin.IsEnabled = true;
        }
    }

    private void LoadCachedAgencyBranding()
    {
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
        catch { }
    }

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
        catch { }
    }

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

    public Task Login() => Login(txtEmail.Text.Trim(), txtPassword.Password, silent: false);

    public async Task Login(string emailIn, string passwordIn, bool silent)
    {
        var formData = new
        {
            email          = emailIn.Trim().ToLowerInvariant(),
            password       = passwordIn,
            rememberDevice = "true",
            deviceLabel    = Environment.MachineName
        };

        App.HttpClient.DefaultRequestHeaders.Authorization = null;

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
                continue;
            }
            catch (HttpRequestException rex) when (rex.InnerException is System.Net.Sockets.SocketException && attempt < 2)
            {
                continue;
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "";
                if (!silent)
                    MessageBox.Show("The server didn't respond in time. Check your internet connection and try again.",
                        "Connection Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (HttpRequestException rex) when (rex.InnerException is System.Net.Sockets.SocketException)
            {
                lblStatus.Text = "";
                if (!silent)
                    MessageBox.Show("Cannot reach the server. Please check your internet connection and that the API URL in settings is correct.",
                        "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            lblStatus.Text = "";
            if (silent)
            {
                SavedSession.Clear();
                txtEmail.Text = emailIn;
                return;
            }
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
            if (!silent)
                MessageBox.Show("Unexpected response from the server. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await EnterAppAsync(signed, signed.DeviceToken);
    }

    private async Task EnterAppAsync(SignedAppUser signed, string? deviceToken)
    {
        App.SignedAppUser = signed;
        App.LoginEmail = signed.Email;
        App.SetAuthToken(signed.Token);

        if (!string.IsNullOrEmpty(deviceToken)) SavedSession.Save(deviceToken!);

        _ = CacheAgencyBrandingAsync(signed.AgencyName, signed.LogoPath);

        var chooser = new ModeChooserWindow();

        lblStatus.Text = "";
        Hide();
        chooser.ShowDialog();

        txtPassword.Clear();
        if (chooser.ChangeAgencyRequested || chooser.LoggedOut)
        {
            await RevokeDeviceAsync();
            txtEmail.Clear();
            lblStatus.Text = "";
            Show();
            txtEmail.Focus();
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    private static async Task RevokeDeviceAsync()
    {
        var token = SavedSession.Load();
        SavedSession.Clear();
        GateAccess.ClearAll();
        if (string.IsNullOrEmpty(token)) return;
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            await App.HttpClient.PostAsync(
                App.ApiBaseUrl + "api/agency/desktop/session/revoke",
                JsonContent.Create(new { deviceToken = token }), cts.Token);
        }
        catch { }
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
