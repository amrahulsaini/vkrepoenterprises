using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VRASDesktopApp.AppUsers;
using VRASDesktopApp.Blacklist;
using VRASDesktopApp.Confirmations;
using VRASDesktopApp.Feedbacks;
using VRASDesktopApp.Finances;
using VRASDesktopApp.Records;
using VRASDesktopApp.Reports;
using VRASDesktopApp.Utilities;

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
    private readonly Page _feedbacksPage;
    private readonly Page _paymentsPage;
    private readonly Page _otpPage;
    private readonly Page _blacklistPage;
    private readonly Page _cleanFilePage;
    private readonly Page _billingPage;
    private readonly Page _mobileControlPinPage;
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
        _feedbacksPage = new ModuleStatusPage("feedbacks", "FEEDBACKS");
        _paymentsPage = new AcceptPaymentsPage();
        _otpPage = new OtpManagerPage();
        _blacklistPage = new BlacklistPage();
        _cleanFilePage = new ModuleStatusPage("cleanfile", "CLEAN FILE");
        _billingPage = new ModuleStatusPage("billing", "PAY BILL");
        _mobileControlPinPage = new ModuleStatusPage("controlpin", "MOBILE CONTROL PIN");

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
    }

    private void btnSettings_Click(object sender, RoutedEventArgs e)
    {
        var w = new ServerSettingsWindow { Owner = this };
        w.ShowDialog();
        RefreshFirmLabels();
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
            case "CleanFile": LoadPage(_cleanFilePage); break;
            case "OTPs": LoadPage(_otpPage); break;
            case "Blacklist": LoadPage(_blacklistPage); break;
            case "Feedbacks": LoadPage(_feedbacksPage); break;
            case "Payments": LoadPage(_paymentsPage); break;
            case "PayBill": LoadPage(_billingPage); break;
            case "MobileControlPin": LoadPage(_mobileControlPinPage); break;
            case "FirmSettings":
                var w = new ServerSettingsWindow { Owner = this };
                w.ShowDialog();
                RefreshFirmLabels();
                break;
            default:
                MessageBox.Show($"Not implemented: {tag}", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    }

    private void OpenRecordsEditor()
    {
        if (_recordsEditorWindow == null)
        {
            _recordsEditorWindow = new RecordsEditorWindow { Owner = this };
            _recordsEditorWindow.Closed += (_, __) => _recordsEditorWindow = null;
            _recordsEditorWindow.Show();
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
        var nameTb = FindName("lblFirmName") as TextBlock;
        if (nameTb != null) nameTb.Text = App.Firm.FirmName;

        if (FindName("lblFirmMobile") is TextBlock mobileTb)
        {
            mobileTb.Text = App.Firm.ContactNos;
        }

        if (FindName("lblFirmAddress") is TextBlock addrTb)
        {
            addrTb.Text = App.Firm.Address;
        }
    }
}
