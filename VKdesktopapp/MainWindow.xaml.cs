using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VRASDesktopApp.AppUsers;
using VRASDesktopApp.Blacklist;
using VRASDesktopApp.Confirmations;
using VRASDesktopApp.Data;
using VRASDesktopApp.Finances;
using VRASDesktopApp.DirectData;
using VRASDesktopApp.Records;
using VRASDesktopApp.Reports;

namespace VRASDesktopApp;

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

        // Poll the support unread-reply badge: once now + every 60s.
        _ = UpdateSupportBadgeAsync();
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        t.Tick += async (_, _) => await UpdateSupportBadgeAsync();
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
        // Opening Support = the agency has now seen the replies → clear badge.
        await MarkSupportSeenAsync();
    }

    // ── Support unread badge ─────────────────────────────────────────────────
    // Shows a red count on the 🎧 icon when CRMRS has posted admin replies the
    // agency hasn't opened yet. "Seen" = count of admin messages at the last
    // time the agency opened Support, stored in a small LocalAppData file.
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
        if (count < 0) return;                 // couldn't check — leave badge as is
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
        }
    }

    private void OpenRecordsEditor()
    {
        if (_recordsEditorWindow == null)
        {
            // RecordsEditorWindow is the most Syncfusion-heavy screen in the
            // app (SfSpreadsheet + ribbon + Windows11Light theme). On machines
            // with Smart App Control / AppLocker / WDAC, those unsigned
            // Syncfusion DLLs get blocked and constructing the window throws a
            // FileLoadException (0x800711C7). Catch it here so the agency sees
            // a clear "blocked by your IT policy" message instead of a raw
            // crash dialog — and so the rest of the app stays usable.
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
                // Editor closed → bring the dashboard back to the foreground.
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Maximized;
                Activate();
            };
            _recordsEditorWindow.Show();
            // Get the dashboard out of the way so the editor is the foreground
            // window. Minimize — do NOT Hide(): this window is shown via
            // LoginWindow.ShowDialog(), and hiding a modal dialog ends that
            // modal loop, which makes the login window pop back up.
            WindowState = WindowState.Minimized;
            return;
        }

        if (_recordsEditorWindow.WindowState == WindowState.Minimized)
        {
            _recordsEditorWindow.WindowState = WindowState.Normal;
        }

        _recordsEditorWindow.Activate();
    }

    // Shows the signed-in agency's name / contact / address in the menu header.
    // Falls back to the locally-configured firm when no agency session exists.
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

    // Downloads the signed-in agency's logo and shows it in the menu header.
    // Best-effort — on any failure the default CRMS mark stays in place.
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
        catch { /* keep the default logo */ }
    }
}
