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

    private void btnSuperAdmin_Click(object sender, RoutedEventArgs e)
    {
        var w = new MainWindow { Owner = this };
        WindowState = WindowState.Minimized;
        try { w.ShowDialog(); }
        finally { WindowState = WindowState.Normal; Activate(); }
    }

    private void btnBilling_Click(object sender, RoutedEventArgs e)
    {
        var w = new BillingShellWindow { Owner = this };
        WindowState = WindowState.Minimized;
        try
        {
            w.ShowDialog();
            if (w.LoggedOut) { LoggedOut = true; Close(); return; }
        }
        finally { if (!LoggedOut) { WindowState = WindowState.Normal; Activate(); } }
    }

    private void btnCourier_Click(object sender, RoutedEventArgs e)
    {
        var w = new CourierShellWindow { Owner = this };
        WindowState = WindowState.Minimized;
        try
        {
            w.ShowDialog();
            if (w.LoggedOut) { LoggedOut = true; Close(); return; }
        }
        finally { if (!LoggedOut) { WindowState = WindowState.Normal; Activate(); } }
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();
}
