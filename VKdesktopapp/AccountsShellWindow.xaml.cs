using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CRMRSDesktopApp.Accounts;

namespace CRMRSDesktopApp;

public partial class AccountsShellWindow : Window
{
    public bool LoggedOut { get; private set; }

    public AccountsShellWindow()
    {
        InitializeComponent();
        lblTitle.Text = BillingShellWindow.AgencyDisplayName();
        LoadAgencyLogo();
        Loaded += (_, __) => PageContainer.Navigate(new AccountsPage());
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
}
