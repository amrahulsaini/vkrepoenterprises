using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CRMRSDesktopApp.AppUsers;
using CRMRSDesktopApp.Billing;
using CRMRSDesktopApp.Blacklist;
using CRMRSDesktopApp.Confirmations;
using CRMRSDesktopApp.Data;
using CRMRSDesktopApp.Finances;
using CRMRSDesktopApp.DirectData;
using CRMRSDesktopApp.Records;
using CRMRSDesktopApp.Reports;

namespace CRMRSDesktopApp;

public partial class MainWindow : Window
{
    private readonly Page _homePage;
    private readonly Page _findVehiclePage;
    private readonly Page _financesManagerPage;
    private readonly Page _appUsersManagerPage;
    private readonly Page _detailsViewsPage;
    private readonly Page _uploadRecordsPage;
    private readonly Page _confirmationsPage;
    private readonly Page _reportsPage;
    private readonly Page _blacklistPage;
    private readonly Page _directDataPage;
    private RecordsEditorWindow? _recordsEditorWindow;

    private static readonly SolidColorBrush ActiveBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFF9800"));
    private static readonly SolidColorBrush InactiveBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF888888"));

    public MainWindow()
    {
        InitializeComponent();

        _homePage = new HomePage();
        _findVehiclePage = new FindVehiclePage();
        _financesManagerPage = new FinancesManagerPage();
        _appUsersManagerPage = new AppUsersManagerPage();
        _detailsViewsPage = new DetailsViewsPage();
        _uploadRecordsPage = new UploadRecordsPage();
        _confirmationsPage = new ConfirmationsManagerPage();
        _reportsPage = new ReportsPage();
        _blacklistPage = new BlacklistPage();
        _directDataPage = new DirectDataPage();

        RefreshFirmLabels();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadPage(_homePage);
        MenuContainer.Visibility = Visibility.Visible;
        if (clmMenuContainer.Width.GridUnitType != GridUnitType.Star && clmMenuContainer.Width.Value < 220)
        {
            clmMenuContainer.Width = new GridLength(320);
        }
        LoadAgencyLogo();

        _ = UpdateSupportBadgeAsync();
        _ = UpdateMessagesBadgeAsync();
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        t.Tick += async (_, _) => { await UpdateSupportBadgeAsync(); await UpdateMessagesBadgeAsync(); };
        t.Start();
    }

    private void LoadPage(Page page)
    {
        PageContainer.Navigate(page);
    }

    private void btnNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        SetAllNavButtonsForeground(InactiveBrush);
        btn.Foreground = ActiveBrush;

        switch (btn.Tag?.ToString())
        {
            case "Home": LoadPage(_homePage); break;
            case "Search": LoadPage(_findVehiclePage); break;
            case "Finances": LoadPage(_financesManagerPage); break;
            case "Users": LoadPage(_appUsersManagerPage); break;
            case "Confirmations": LoadPage(_confirmationsPage); break;
            case "Reports": LoadPage(_reportsPage); break;
            case "DirectData": LoadPage(_directDataPage); break;
        }
    }

    private void SetAllNavButtonsForeground(Brush brush)
    {
        btnHome.Foreground = brush;
        btnSearch.Foreground = brush;
        btnFinances.Foreground = brush;
        btnUsers.Foreground = brush;
        btnConfirmations.Foreground = brush;
        btnReports.Foreground = brush;
        btnDirectData.Foreground = brush;
    }

    private void btnSettings_Click(object sender, RoutedEventArgs e)
    {
        var w = new ServerSettingsWindow { Owner = this };
        w.ShowDialog();
        RefreshFirmLabels();
    }

    private async void btnSupport_Click(object sender, RoutedEventArgs e)
    {
        var w = new SupportWindow { Owner = this };
        w.ShowDialog();
        await MarkSupportSeenAsync();
    }

    private async void btnMessages_Click(object sender, RoutedEventArgs e)
    {
        var w = new MessagesWindow { Owner = this };
        w.ShowDialog();
        await UpdateMessagesBadgeAsync();
    }

    private async Task UpdateMessagesBadgeAsync()
    {
        try
        {
            var d = await DesktopApiClient.GetIntegrationMessagesAsync();
            if (d.Unread > 0)
            {
                messagesBadgeText.Text  = d.Unread > 99 ? "99+" : d.Unread.ToString();
                messagesBadge.Visibility = Visibility.Visible;
            }
            else messagesBadge.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private static string SupportSeenFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VKEnterprises", "tickets_seen.txt");

    private static int ReadSupportSeen()
    {
        try { return int.TryParse(System.IO.File.ReadAllText(SupportSeenFile), out var n) ? n : 0; }
        catch { return 0; }
    }
    private static void WriteSupportSeen(int n)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SupportSeenFile)!);
            System.IO.File.WriteAllText(SupportSeenFile, n.ToString());
        }
        catch { }
    }

    private async Task UpdateSupportBadgeAsync()
    {
        var count = await DesktopApiClient.GetAdminMessageCountAsync();
        if (count < 0) return;
        int unread = count - ReadSupportSeen();
        if (unread > 0)
        {
            supportBadgeText.Text   = unread > 99 ? "99+" : unread.ToString();
            supportBadge.Visibility = Visibility.Visible;
        }
        else supportBadge.Visibility = Visibility.Collapsed;
    }

    private async Task MarkSupportSeenAsync()
    {
        var count = await DesktopApiClient.GetAdminMessageCountAsync();
        if (count >= 0) WriteSupportSeen(count);
        supportBadge.Visibility = Visibility.Collapsed;
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void btnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void btnRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && WindowState != WindowState.Maximized)
            DragMove();
    }

    private void TileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = (btn.Tag ?? string.Empty).ToString();
        switch (tag)
        {
            case "Home": LoadPage(_homePage); break;
            case "Search": LoadPage(_findVehiclePage); break;
            case "Finances": LoadPage(_financesManagerPage); break;
            case "Users": LoadPage(_appUsersManagerPage); break;
            case "UploadRecords": OpenRecordsEditor(); break;
            case "DetailsViews": LoadPage(_detailsViewsPage); break;
            case "Confirmations": LoadPage(_confirmationsPage); break;
            case "Reports": LoadPage(_reportsPage); break;
            case "Blacklist": LoadPage(_blacklistPage); break;
            case "DirectData": LoadPage(_directDataPage); break;
            case "Billing": LoadPage(new Billing.BillingLoginPage()); break;
            case "Allocations": _ = OpenAllocationsAsync(); break;
            case "Couriers": LoadPage(new Couriers.CouriersPage()); break;
        }
    }

    private async Task OpenAllocationsAsync()
    {
        string stamp;
        try { stamp = await DesktopApiClient.GetGateStampAsync("allocation"); }
        catch
        {
            MessageBox.Show("Cannot reach the server to check the password. Try again.",
                "Allocations", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrEmpty(stamp))
        {
            var prompt = new Billing.PasswordPromptWindow("Allocations") { Owner = this };
            if (prompt.ShowDialog() != true) return;

            DesktopApiClient.GateVerifyResult result;
            try { result = await DesktopApiClient.VerifyGateAsync("allocation", prompt.EnteredPassword); }
            catch
            {
                MessageBox.Show("Cannot reach the server to check the password. Try again.",
                    "Allocations", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!result.Ok)
            {
                MessageBox.Show("Wrong password.", "Allocations", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        LoadPage(new Billing.AllocationsPage());
    }

    private void OpenRecordsEditor()
    {
        if (_recordsEditorWindow == null)
        {
            try
            {
                _recordsEditorWindow = new RecordsEditorWindow();
            }
            catch (Exception ex) when (App.IsBlockedByPolicy(ex))
            {
                _recordsEditorWindow = null;
                App.ShowPolicyBlockMessage(ex);
                return;
            }
            _recordsEditorWindow.Closed += (_, __) =>
            {
                _recordsEditorWindow = null;
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Maximized;
                Activate();
            };
            _recordsEditorWindow.Show();
            WindowState = WindowState.Minimized;
            return;
        }

        if (_recordsEditorWindow.WindowState == WindowState.Minimized)
        {
            _recordsEditorWindow.WindowState = WindowState.Normal;
        }

        _recordsEditorWindow.Activate();
    }

    private void RefreshFirmLabels()
    {
        var u = App.SignedAppUser;
        bool agency = u != null && u.IsAgency;

        string name   = agency && !string.IsNullOrWhiteSpace(u!.AgencyName) ? u.AgencyName : App.Firm.FirmName;
        string mobile = agency && !string.IsNullOrWhiteSpace(u!.Mobile1)    ? u.Mobile1    : App.Firm.ContactNos;
        string addr   = agency && !string.IsNullOrWhiteSpace(u!.Address)    ? u.Address    : App.Firm.Address;

        if (FindName("lblFirmName")    is TextBlock nameTb)   nameTb.Text   = name;
        if (FindName("lblFirmMobile")  is TextBlock mobileTb) mobileTb.Text = mobile;
        if (FindName("lblFirmAddress") is TextBlock addrTb)   addrTb.Text   = addr;
    }

    private async void LoadAgencyLogo()
    {
        try
        {
            var u = App.SignedAppUser;
            if (u == null || !u.IsAgency || string.IsNullOrWhiteSpace(u.LogoPath)) return;
            if (FindName("imgAgencyLogo") is not Image img) return;

            var url = App.ApiBaseUrl.TrimEnd('/') + "/" + u.LogoPath.TrimStart('/');
            var bytes = await App.HttpClient.GetByteArrayAsync(url);

            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;
        }
        catch { }
    }
}
