using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VRASDesktopApp.Data;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.AppUsers;

public partial class AppUsersManagerPage : Page
{
    private readonly AppUserRepository _repo = new();
    private ObservableCollection<AppUserListItem> _allUsers = new();
    private AppUserListItem? _selectedUser;

    public AppUsersManagerPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        usersLoadingBar.Visibility = Visibility.Visible;
        try
        {
            var (total, active, admins, withSub) = await _repo.GetStatsAsync();
            lblTotal.Text   = total.ToString("N0");
            lblActive.Text  = active.ToString("N0");
            lblAdmins.Text  = admins.ToString("N0");
            lblWithSub.Text = withSub.ToString("N0");

            var users = await _repo.GetUsersAsync();
            _allUsers = new ObservableCollection<AppUserListItem>(users);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load users: {ex.Message}", "Users",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            usersLoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        var q = txtSearch.Text.Trim().ToLower();
        var filtered = string.IsNullOrEmpty(q)
            ? _allUsers
            : new ObservableCollection<AppUserListItem>(
                _allUsers.Where(u =>
                    u.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    u.Mobile.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (u.Address?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)));
        icUsers.ItemsSource = filtered;
    }

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void UserRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: AppUserListItem user })
            await SelectUserAsync(user);
    }

    private async Task SelectUserAsync(AppUserListItem user)
    {
        _selectedUser = user;
        txtRightTitle.Text    = user.Name;
        txtAvatar.Text        = user.Initials;
        txtProfileName.Text   = user.Name;
        txtProfileMobile.Text = user.Mobile;
        txtProfileId.Text     = user.Id.ToString();
        txtProfileAddress.Text = user.Address ?? "—";
        txtProfilePincode.Text = user.Pincode ?? "—";
        txtProfileBalance.Text = $"₹{user.Balance:N2}";
        txtProfileJoined.Text  = user.CreatedDisplay;
        txtDeviceId.Text       = user.DeviceId ?? "(no device registered)";

        pnlEmpty.Visibility   = Visibility.Collapsed;
        pnlProfile.Visibility = Visibility.Visible;

        await LoadSubscriptionsAsync(user.Id);
    }

    private async Task LoadSubscriptionsAsync(long userId)
    {
        try
        {
            var subs = await _repo.GetSubscriptionsAsync(userId);
            dgSubscriptions.ItemsSource = subs;
        }
        catch { /* silent — subscriptions are secondary */ }
    }

    // ── Active toggle ──────────────────────────────────────────────────
    private async void ActiveToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: AppUserListItem user })
            await SetActiveAsync(user, true);
    }

    private async void ActiveToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: AppUserListItem user })
            await SetActiveAsync(user, false);
    }

    private async Task SetActiveAsync(AppUserListItem user, bool active)
    {
        try
        {
            await _repo.SetActiveAsync(user.Id, active);
            user.IsActive = active;
            // Refresh stats
            var (total, a, admins, withSub) = await _repo.GetStatsAsync();
            lblActive.Text = a.ToString("N0");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update active state: {ex.Message}", "Users",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Admin toggle ───────────────────────────────────────────────────
    private async void AdminToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: AppUserListItem user })
            await SetAdminAsync(user, true);
    }

    private async void AdminToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: AppUserListItem user })
            await SetAdminAsync(user, false);
    }

    private async Task SetAdminAsync(AppUserListItem user, bool admin)
    {
        try
        {
            await _repo.SetAdminAsync(user.Id, admin);
            user.IsAdmin = admin;
            var (total, active, admins, withSub) = await _repo.GetStatsAsync();
            lblAdmins.Text = admins.ToString("N0");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update admin state: {ex.Message}", "Users",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Subscription ───────────────────────────────────────────────────
    private async void btnAddSubscription_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var dlg = new SubscriptionEditorWindow { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await _repo.AddSubscriptionAsync(
                _selectedUser.Id,
                dlg.StartDate, dlg.EndDate,
                dlg.Amount, dlg.Notes);
            await LoadSubscriptionsAsync(_selectedUser.Id);
            // update sub end on list item
            var subs = await _repo.GetSubscriptionsAsync(_selectedUser.Id);
            _selectedUser.SubEndDate = subs.Count > 0 ? subs.Max(s => s.EndDate) : null;

            var (total, active, admins, withSub) = await _repo.GetStatsAsync();
            lblWithSub.Text = withSub.ToString("N0");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to add subscription: {ex.Message}", "Subscription",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void dgSubscriptions_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (dgSubscriptions.SelectedItem is not SubscriptionItem)
            e.Handled = true;
    }

    private async void SubDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgSubscriptions.SelectedItem is not SubscriptionItem sub || _selectedUser == null)
            return;
        var confirm = MessageBox.Show(
            $"Delete subscription ending {sub.EndDisplay}?",
            "Delete Plan", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            await _repo.DeleteSubscriptionAsync(sub.Id);
            await LoadSubscriptionsAsync(_selectedUser.Id);
            var (total, active, admins, withSub) = await _repo.GetStatsAsync();
            lblWithSub.Text = withSub.ToString("N0");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete subscription: {ex.Message}", "Delete",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Device reset ───────────────────────────────────────────────────
    private async void btnResetDevice_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var confirm = MessageBox.Show(
            $"Reset device for {_selectedUser.Name}?\nThey will need to log in again from a new device.",
            "Reset Device", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            await _repo.ResetDeviceAsync(_selectedUser.Id);
            _selectedUser.DeviceId = null;
            txtDeviceId.Text = "(no device registered)";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to reset device: {ex.Message}", "Reset Device",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
