using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VRASDesktopApp.AppUsers;
using VRASDesktopApp.Confirmations;
using VRASDesktopApp.Feedbacks;
using VRASDesktopApp.Finances;
using VRASDesktopApp.Records;
using VRASDesktopApp.Reports;

namespace VRASDesktopApp;

public partial class MainWindow : Window
{
    private HomePage? _homePage;
    private FindVehiclePage? _findVehiclePage;
    private FinancesManagerPage? _financesManagerPage;
    private AppUsersManagerPage? _appUsersManagerPage;
    private ConfirmationsManagerPage? _confirmationsManagerPage;
    private ReportsPage? _reportsPage;
    private FeedbacksManagerPage? _feedbacksPage;
    private AcceptPaymentsPage? _paymentsPage;

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
        _confirmationsManagerPage = new ConfirmationsManagerPage();
        _reportsPage = new ReportsPage();
        _feedbacksPage = new FeedbacksManagerPage();
        _paymentsPage = new AcceptPaymentsPage();

        lblFirmName.Text = App.Firm.FirmName;
        lblFirmMobile.Text = App.Firm.ContactNos;
        lblFirmAddress.Text = App.Firm.Address;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadPage(_homePage!);
    }

    private void LoadPage(Page page)
    {
        PageContainer.Child = page;
    }

    private void btnNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        SetAllNavButtonsForeground(InactiveBrush);
        btn.Foreground = ActiveBrush;

        switch (btn.Tag?.ToString())
        {
            case "Home": LoadPage(_homePage!); break;
            case "Search": LoadPage(_findVehiclePage!); break;
            case "Finances": LoadPage(_financesManagerPage!); break;
            case "Users": LoadPage(_appUsersManagerPage!); break;
            case "Confirmations": LoadPage(_confirmationsManagerPage!); break;
            case "Reports": LoadPage(_reportsPage!); break;
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
}
