using System;
using System.Windows;
using System.Windows.Input;
using CRMRSDesktopApp.Billing;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public partial class ModeChooserWindow : Window
{
    public bool LoggedOut { get; private set; }
    public bool ChangeAgencyRequested { get; private set; }

    public ModeChooserWindow()
    {
        InitializeComponent();
        LoadAgencyHeader();
    }

    private static string LogoCachePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CRMRS", "agency-logo.png");

    private async void LoadAgencyHeader()
    {
        var u = App.SignedAppUser;
        var name = u?.AgencyName;
        lblAgencyName.Text = string.IsNullOrWhiteSpace(name) ? App.Firm.FirmName : name;
        lblAgencyAddress.Text = u?.Address ?? "";
        lblSignedIn.Text = string.IsNullOrWhiteSpace(App.LoginEmail)
            ? "" : "Signed in as " + App.LoginEmail;

        // Fast: show the cached logo immediately.
        try
        {
            if (System.IO.File.Exists(LogoCachePath))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(LogoCachePath);
                bmp.EndInit();
                bmp.Freeze();
                imgAgencyLogo.Source = bmp;
            }
        }
        catch { }

        // Dynamic: refresh name/address/logo from the server so a change made
        // in the portal or Server Settings shows without re-logging in. Also
        // refreshes the local cache the billing/courier shells read.
        try
        {
            var p = await DesktopApiClient.GetAgencyProfileAsync();
            if (p == null) return;

            if (!string.IsNullOrWhiteSpace(p.Name))
            {
                lblAgencyName.Text = p.Name;
                if (u != null) u.AgencyName = p.Name;
            }
            lblAgencyAddress.Text = p.Address ?? lblAgencyAddress.Text;
            if (u != null && p.Address != null) u.Address = p.Address;

            if (!string.IsNullOrWhiteSpace(p.LogoPath))
            {
                if (u != null) u.LogoPath = p.LogoPath;
                var url = App.ApiBaseUrl.TrimEnd('/') + "/" + p.LogoPath.TrimStart('/');
                var bytes = await App.HttpClient.GetByteArrayAsync(url);

                using var ms = new System.IO.MemoryStream(bytes);
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                imgAgencyLogo.Source = bmp;

                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogoCachePath)!);
                    await System.IO.File.WriteAllBytesAsync(LogoCachePath, bytes);
                }
                catch { }
            }
        }
        catch { }
    }

    private void btnChangeAgency_Click(object sender, RoutedEventArgs e)
    {
        SavedSession.Clear();
        ChangeAgencyRequested = true;
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private async void btnSuperAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (!await AskPasswordAsync("Super Admin", "superadmin")) return;

        var w = new MainWindow();
        Hide();
        try { w.ShowDialog(); }
        finally { Show(); Activate(); }
    }

    private void btnBilling_Click(object sender, RoutedEventArgs e)
    {
        var w = new BillingShellWindow();
        Hide();
        try
        {
            w.ShowDialog();
            if (w.LoggedOut) { LoggedOut = true; Close(); return; }
        }
        finally { if (!LoggedOut) { Show(); Activate(); } }
    }

    private async void btnCourier_Click(object sender, RoutedEventArgs e)
    {
        if (!await AskPasswordAsync("Couriers", "courier")) return;

        var w = new CourierShellWindow();
        Hide();
        try
        {
            w.ShowDialog();
            if (w.LoggedOut) { LoggedOut = true; Close(); return; }
        }
        finally { if (!LoggedOut) { Show(); Activate(); } }
    }

    /// Asks for the account password every time a mode is opened. The typed
    /// password is checked on the server; it is never held in the app.
    private async System.Threading.Tasks.Task<bool> AskPasswordAsync(string title, string gate)
    {
        var prompt = new PasswordPromptWindow(title) { Owner = this };
        if (prompt.ShowDialog() != true) return false;

        try
        {
            var result = await DesktopApiClient.VerifyGateAsync(gate, prompt.EnteredPassword);
            if (result.Ok) return true;
        }
        catch
        {
            MessageBox.Show("Cannot reach the server to check the password. Try again.",
                title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        MessageBox.Show("Wrong password.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();
}
