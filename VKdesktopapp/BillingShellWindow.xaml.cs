using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CRMRSDesktopApp.Billing;

namespace CRMRSDesktopApp;

public partial class BillingShellWindow : Window
{
    public bool LoggedOut { get; private set; }

    public BillingShellWindow()
    {
        InitializeComponent();
        lblTitle.Text = AgencyDisplayName();
        LoadAgencyLogo();
        Loaded += (_, __) => PageContainer.Navigate(new BillingLoginPage());
    }

    private void LoadAgencyLogo()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CRMRS", "agency-logo.png");
            if (!File.Exists(path)) return;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            imgLogo.Source = bmp;
        }
        catch { }
    }

    /// The tenant brand where the build has one, otherwise the account's
    /// agency name, matching what the sign in screen shows.
    internal static string AgencyDisplayName()
    {
        try
        {
            var cached = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CRMRS", "agency-name.txt");
            if (File.Exists(cached))
            {
                var n = File.ReadAllText(cached).Trim();
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
        }
        catch { }

        if (Branding.IsTenantBuild && !string.IsNullOrWhiteSpace(Branding.Name)) return Branding.Name;
        return App.SignedAppUser?.AgencyName ?? "";
    }
}
