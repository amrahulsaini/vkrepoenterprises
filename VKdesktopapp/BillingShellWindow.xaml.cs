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
        var name = App.SignedAppUser?.AgencyName;
        if (!string.IsNullOrWhiteSpace(name)) lblTitle.Text = name + " Billing";
        LoadAgencyLogo();
        Loaded += (_, __) => PageContainer.Navigate(new BillingGatePage());
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

    private void btnLogout_Click(object sender, RoutedEventArgs e)
    {
        LoggedOut = true;
        Close();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();
}
