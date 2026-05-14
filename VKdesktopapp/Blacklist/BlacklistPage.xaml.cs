using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Blacklist;

public class BlacklistUserItem
{
    public long     Id            { get; set; }
    public string   Name          { get; set; } = string.Empty;
    public string   Mobile        { get; set; } = string.Empty;
    public string   Address       { get; set; } = string.Empty;
    public bool     IsBlacklisted { get; set; }
    public bool     IsActive      { get; set; }
    public bool     IsStopped     { get; set; }

    public string Initials => Name.Length > 0
        ? string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(w => char.ToUpper(w[0])))
        : "?";
}

public partial class BlacklistPage : Page
{
    private ObservableCollection<BlacklistUserItem> _allUsers = new();
    private BlacklistUserItem? _selectedUser;

    private static readonly SolidColorBrush BlockedBg  = new(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly SolidColorBrush RestoreBg  = new(Color.FromRgb(0x2E, 0x7D, 0x32));

    public BlacklistPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        loadingBar.Visibility = Visibility.Visible;
        try
        {
            var users = await DesktopApiClient.GetAllSimpleUsersAsync();
            _allUsers = new ObservableCollection<BlacklistUserItem>(
                users.Select(u => new BlacklistUserItem
                {
                    Id            = u.Id,
                    Name          = u.Name,
                    Mobile        = u.Mobile,
                    Address       = u.Address,
                    IsBlacklisted = u.IsBlacklisted,
                    IsActive      = u.IsActive,
                    IsStopped     = u.IsStopped,
                }));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load users: {ex.Message}", "Blacklist",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            loadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        var q = txtSearch.Text.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _allUsers
            : new ObservableCollection<BlacklistUserItem>(
                _allUsers.Where(u =>
                    u.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    u.Mobile.Contains(q, StringComparison.OrdinalIgnoreCase)));
        icUsers.ItemsSource = filtered;
    }

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void UserRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: BlacklistUserItem user })
            SelectUser(user);
    }

    private void SelectUser(BlacklistUserItem user)
    {
        _selectedUser    = user;
        txtRightTitle.Text = user.Name;
        txtUserName.Text   = user.Name;
        txtUserMobile.Text = user.Mobile;
        txtUserAddress.Text = user.Address.Length > 0 ? user.Address : "—";

        UpdateActionPanel();

        pnlEmpty.Visibility  = Visibility.Collapsed;
        pnlAction.Visibility = Visibility.Visible;
    }

    private void UpdateActionPanel()
    {
        if (_selectedUser == null) return;

        if (_selectedUser.IsBlacklisted)
        {
            statusBadge.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xDE, 0xDE));
            txtStatus.Text         = "BLOCKED — permanently blacklisted";
            txtStatus.Foreground   = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
            btnBlacklist.Content   = "Remove from Blacklist";
            btnBlacklist.Background = RestoreBg;
            btnBlacklist.Foreground = Brushes.White;
        }
        else
        {
            statusBadge.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
            txtStatus.Text         = "Active user";
            txtStatus.Foreground   = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            btnBlacklist.Content   = "Blacklist — Block Permanently";
            btnBlacklist.Background = BlockedBg;
            btnBlacklist.Foreground = Brushes.White;
        }
    }

    private async void btnBlacklist_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;

        var blacklist = !_selectedUser.IsBlacklisted;
        var action    = blacklist ? "blacklist" : "remove from blacklist";
        var confirm   = MessageBox.Show(
            $"Are you sure you want to {action} {_selectedUser.Name} ({_selectedUser.Mobile})?" +
            (blacklist ? "\n\nThis will also stop their app immediately." : ""),
            "Confirm", MessageBoxButton.YesNo,
            blacklist ? MessageBoxImage.Warning : MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await DesktopApiClient.SetUserBlacklistedAsync(_selectedUser.Id, blacklist);
            _selectedUser.IsBlacklisted = blacklist;
            _selectedUser.IsStopped     = blacklist;
            UpdateActionPanel();
            // Refresh list item display
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed: {ex.Message}", "Blacklist",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
