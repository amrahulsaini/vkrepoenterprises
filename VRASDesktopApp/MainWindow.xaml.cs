using System.Windows;
using System.Windows.Controls;
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

    private bool _menuExpanded = true;

    private static readonly SolidColorBrush ActiveBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2196F3"));
    private static readonly SolidColorBrush InactiveBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF757575"));

    public MainWindow()
    {
        InitializeComponent();

        // Pre-load pages
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
        PageContainerWide.Child = null;
        PageContainer.Visibility = Visibility.Visible;
        PageContainerWide.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Single handler for all nav buttons. Uses Tag to identify which page to load.
    /// </summary>
    private void btnNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        // Deselect all nav buttons
        SetAllNavButtonsForeground(InactiveBrush);

        // Highlight current
        btn.Foreground = ActiveBrush;

        string? tag = btn.Tag?.ToString();
        switch (tag)
        {
            case "Home":
                LoadPage(_homePage!);
                break;
            case "Search":
                LoadPage(_findVehiclePage!);
                break;
            case "Finances":
                LoadPage(_financesManagerPage!);
                break;
            case "Users":
                LoadPage(_appUsersManagerPage!);
                break;
            case "Confirmations":
                LoadPage(_confirmationsManagerPage!);
                break;
            case "Reports":
                LoadPage(_reportsPage!);
                break;
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
        var settingsWindow = new ServerSettingsWindow();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }

    #region Window Chrome Buttons

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void btnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void btnRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    #endregion

    #region Menu Toggle

    private void btnMenuToggle_Click(object sender, RoutedEventArgs e)
    {
        _menuExpanded = !_menuExpanded;
        MenuContainer.Visibility = _menuExpanded ? Visibility.Visible : Visibility.Collapsed;

        if (_menuExpanded)
        {
            PageContainer.Visibility = Visibility.Visible;
            PageContainerWide.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Move content to wide area
            PageContainerWide.Child = PageContainer.Child;
            PageContainer.Child = null;
            PageContainer.Visibility = Visibility.Collapsed;
            PageContainerWide.Visibility = Visibility.Visible;
        }
    }

    #endregion
}
