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

    private async Task TryAutoLoginAsync()
    {
        if (_autoLoginTried) return;
        _autoLoginTried = true;

        SavedSession.PurgeLegacy();

        var deviceToken = SavedSession.Load();
        if (string.IsNullOrEmpty(deviceToken)) return;

        ShowAgencyCard();
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
                // Only forget the sign in when the server actually rejects it:
                // expired, revoked, or the password changed. A server hiccup
                // must not cost the user their saved sign in.
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    SavedSession.Clear();
                    ShowAgencyForm();
                }
                else
                {
                    lblSavedState.Text = "Cannot reach the server right now. Change agency to sign in again.";
                }
                lblStatus.Text = "";
                return;
            }

            var signed = await resp.Content.ReadFromJsonAsync<SignedAppUser>();
            if (signed == null || string.IsNullOrEmpty(signed.Token))
            {
                SavedSession.Clear();
                ShowAgencyForm();
                lblStatus.Text = "";
                return;
            }

            await EnterAppAsync(signed, deviceToken);
        }
        catch
        {
            lblSavedState.Text = "Cannot reach the server right now. Change agency to sign in again.";
            lblStatus.Text = "";
        }
        finally
        {
            btnLogin.IsEnabled = true;
        }
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
        App.ProfileUser = null;
        App.HttpClient.DefaultRequestHeaders.Authorization = null;
        txtEmail.Clear();
        txtPassword.Clear();
        ShowAgencyForm();
        lblStatus.Text = "";
        _autoLoginTried = true;
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

        _ = AgencyBranding.SaveAsync(signed.AgencyName, signed.LogoPath);

        // Agencies that use HRMS sign in as a person, not by picking a mode:
        // the profile decides which modules exist, and MainWindow already
        // reaches Billing, Couriers, Accounts and Allocations from its own
        // tiles, so the chooser has nothing left to choose. Agencies without
        // HRMS have no profiles to sign in with and keep the chooser.
        bool profileGated = false;
        try { profileGated = await CRMRSDesktopApp.Data.DesktopApiClient.ProfileLoginRequiredAsync(); } catch { }

        if (profileGated)
        {
            ShowAgencyCard();
            lblStatus.Text = "";
            Hide();

            if (!await ProfileGate.EnsureAsync(this, "CRMRS", standalone: true))
            {
                txtPassword.Clear();
                if (ProfileGate.ChangeAgencyRequested) await ChangeAgencyAsync();
                else Application.Current.Shutdown();
                return;
            }

            var main = new MainWindow();
            main.Closed += (_, __) => Application.Current.Shutdown();
            Hide();
            main.Show();
            main.Activate();
            return;
        }

        // The chooser is shown non-modally so it can be hidden while a mode is
        // on screen and brought straight back when that mode closes. Hiding a
        // window that is itself running a modal loop ends the loop, which is
        // what previously made the app quit when a mode was closed.
        var chooser = new ModeChooserWindow();
        chooser.Closed += async (_, __) =>
        {
            txtPassword.Clear();
            if (chooser.ChangeAgencyRequested || chooser.LoggedOut)
            {
                await RevokeDeviceAsync();
                ClearCachedAgencyBranding();
                ShowAgencyForm();
                txtEmail.Clear();
                lblStatus.Text = "";
                Show();
                Activate();
                txtEmail.Focus();
            }
            else
            {
                Application.Current.Shutdown();
            }
        };

        lblStatus.Text = "";
        Hide();
        chooser.Show();
        chooser.Activate();
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
