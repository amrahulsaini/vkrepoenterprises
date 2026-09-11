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
    public LoginWindow()
    {
        InitializeComponent();
        if (Branding.IsTenantBuild)
            lblAppName.Text = Branding.Name;
        Loaded += async (_, __) =>
        {
            LoadCachedAgencyBranding();
            SavedSession.PurgeLegacy();
            await RevokeDeviceAsync();
        };
    }

    private void ShowAgencyCard()
    {
        pnlAgencyForm.Visibility = Visibility.Collapsed;
        pnlAgencySaved.Visibility = Visibility.Visible;
    }

    private void ShowAgencyForm()
    {
        pnlAgencySaved.Visibility = Visibility.Collapsed;
        pnlAgencyForm.Visibility = Visibility.Visible;
    }

    private async void btnChangeAgency_Click(object sender, RoutedEventArgs e)
    {
        var ask = MessageBox.Show(
            "Sign out of " + lblAppName.Text + " on this computer and sign in to a different agency?",
            "Change agency", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;

        btnChangeAgency.IsEnabled = false;
        try { await ChangeAgencyAsync(); }
        finally { btnChangeAgency.IsEnabled = true; }
    }

    private async Task ChangeAgencyAsync()
    {
        lblStatus.Text = "Signing out...";
        await RevokeDeviceAsync();
        ClearCachedAgencyBranding();
        App.SignedAppUser = null;
        App.HttpClient.DefaultRequestHeaders.Authorization = null;
        txtEmail.Clear();
        txtPassword.Clear();
        ShowAgencyForm();
        lblStatus.Text = "";
        Show();
        Activate();
        txtEmail.Focus();
    }

    private void ClearCachedAgencyBranding()
    {
        AgencyBranding.Clear();
        lblAppName.Text = Branding.IsTenantBuild ? Branding.Name : "CRMRS";
        var def = AgencyBranding.DefaultLogo();
        if (def != null) imgLogo.Source = def;
    }

    private void LoadCachedAgencyBranding()
    {
        lblAppName.Text = AgencyBranding.Name;
        var logo = AgencyBranding.LoadLogo();
        if (logo != null) imgLogo.Source = logo;
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
            rememberDevice = "false",
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

        EnterApp(signed);
    }

    private void EnterApp(SignedAppUser signed)
    {
        App.SignedAppUser = signed;
        App.LoginEmail = signed.Email;
        App.SetAuthToken(signed.Token);

        _ = AgencyBranding.SaveAsync(signed.AgencyName, signed.LogoPath);

        ShowAgencyCard();
        txtPassword.Clear();
        lblStatus.Text = "";

        var main = new MainWindow();
        main.Closed += (_, __) => Application.Current.Shutdown();
        Hide();
        main.Show();
        main.Activate();
    }

    private static async Task RevokeDeviceAsync()
    {
        var token = SavedSession.Load();
        SavedSession.Clear();
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
