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

    private void LoadAgencyHeader()
    {
        var u = App.SignedAppUser;
        var name = u?.AgencyName;
        lblAgencyName.Text = string.IsNullOrWhiteSpace(name) ? App.Firm.FirmName : name;
        lblAgencyAddress.Text = u?.Address ?? "";
        lblSignedIn.Text = string.IsNullOrWhiteSpace(App.LoginEmail)
            ? "" : "Signed in as " + App.LoginEmail;

        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CRMRS", "agency-logo.png");
            if (!System.IO.File.Exists(path)) return;
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            imgAgencyLogo.Source = bmp;
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
        string configured;
        try { configured = await DesktopApiClient.GetSuperAdminPasswordAsync(); }
        catch { configured = ""; }

        if (!await PassesGate("Super Admin", configured)) return;

        Hide();
        try
        {
            var w = new MainWindow();
            w.ShowDialog();
        }
        finally { Show(); }
    }

    private void btnBilling_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        try
        {
            var w = new BillingShellWindow();
            w.ShowDialog();
            if (w.LoggedOut) { SavedSession.Clear(); LoggedOut = true; Close(); return; }
        }
        finally { if (!LoggedOut) Show(); }
    }

    private async void btnCourier_Click(object sender, RoutedEventArgs e)
    {
        string configured;
        try { configured = await DesktopApiClient.GetCourierPasswordAsync(); }
        catch { configured = ""; }

        if (!await PassesGate("Couriers", configured)) return;

        Hide();
        try
        {
            var w = new CourierShellWindow();
            w.ShowDialog();
            if (w.LoggedOut) { SavedSession.Clear(); LoggedOut = true; Close(); return; }
        }
        finally { if (!LoggedOut) Show(); }
    }

    private System.Threading.Tasks.Task<bool> PassesGate(string title, string configured)
    {
        var required = string.IsNullOrEmpty(configured) ? App.LoginPassword : configured;

        if (string.IsNullOrEmpty(required))
            return System.Threading.Tasks.Task.FromResult(true);

        var prompt = new PasswordPromptWindow(title) { Owner = this };
        if (prompt.ShowDialog() != true)
            return System.Threading.Tasks.Task.FromResult(false);

        if (!string.Equals(prompt.EnteredPassword, required, StringComparison.Ordinal))
        {
            MessageBox.Show("Wrong password.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return System.Threading.Tasks.Task.FromResult(false);
        }
        return System.Threading.Tasks.Task.FromResult(true);
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
