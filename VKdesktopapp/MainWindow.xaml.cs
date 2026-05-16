using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VRASDesktopApp.AppUsers;
using VRASDesktopApp.Confirmations;
using VRASDesktopApp.Finances;
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
