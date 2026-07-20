using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CRMRSDesktopApp.Couriers;

namespace CRMRSDesktopApp;

public partial class CourierShellWindow : Window
{
    public bool LoggedOut { get; private set; }

    public CourierShellWindow()
    {
        InitializeComponent();
        var name = App.SignedAppUser?.AgencyName;
        if (!string.IsNullOrWhiteSpace(name)) lblTitle.Text = name + " Couriers";
        LoadAgencyLogo();
        Loaded += (_, __) => PageContainer.Navigate(new CouriersPage());
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
        var confirm = MessageBox.Show(
            "Log out and forget this sign in on this computer?\n\n" +
            "You will have to type your email and password next time. " +
            "To just close Couriers, use the X instead.",
            "Log out", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        LoggedOut = true;
        Close();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();
}
