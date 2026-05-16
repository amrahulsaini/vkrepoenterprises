using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VRASDesktopApp.Data;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.AppUsers;

public class FinanceRestrictionItem
{
    public int    Id           { get; set; }
    public string Name         { get; set; } = string.Empty;
    public bool   IsRestricted { get; set; }
}

public partial class AppUsersManagerPage : Page
{
    private readonly AppUserRepository _repo = new();
    private ObservableCollection<AppUserListItem> _allUsers = new();
    private AppUserListItem? _selectedUser;
    private bool _suppressStopToggle;

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
            var (users, total, active, admins, withSub) = await _repo.GetUsersWithStatsAsync();
            lblTotal.Text   = total.ToString("N0");
            lblActive.Text  = active.ToString("N0");
            lblAdmins.Text  = admins.ToString("N0");
            lblWithSub.Text = withSub.ToString("N0");
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

        // Load PFP image if available
        if (!string.IsNullOrWhiteSpace(user.PfpBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(user.PfpBase64);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                imgPfp.Source = bmp;
                imgPfp.Visibility = Visibility.Visible;
                txtAvatar.Visibility = Visibility.Collapsed;
            }
            catch
            {
                imgPfp.Visibility = Visibility.Collapsed;
                txtAvatar.Visibility = Visibility.Visible;
            }
        }
        else
        {
            imgPfp.Visibility = Visibility.Collapsed;
            txtAvatar.Visibility = Visibility.Visible;
        }

        // App Active toggle — ON = app running, OFF = stopped (inverted semantics)
        _suppressStopToggle = true;
        tglStopApp.IsChecked = !user.IsStopped;
        lblStopStatus.Text   = user.IsStopped ? "Stopped" : "Running";
        _suppressStopToggle  = false;

        pnlEmpty.Visibility   = Visibility.Collapsed;
        pnlProfile.Visibility = Visibility.Visible;

        await LoadSubscriptionsAsync(user.Id);
        await LoadFinanceRestrictionsAsync(user.Id);
    }

    private async Task LoadFinanceRestrictionsAsync(long userId)
    {
        try
        {
            var finances     = await DesktopApiClient.GetFinancesAsync();
            var restricted   = await _repo.GetFinanceRestrictionsAsync(userId);
            var restrictedSet = new HashSet<int>(restricted);
            var items = finances.Select(f => new FinanceRestrictionItem
            {
                Id           = f.Id,
                Name         = f.Name,
                IsRestricted = restrictedSet.Contains(f.Id)
            }).ToList();
            icFinanceRestrictions.ItemsSource = items;
        }
        catch { /* silent */ }
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

        // Only one active plan allowed per user — delete the existing one first.
        var existing = await _repo.GetSubscriptionsAsync(_selectedUser.Id);
        if (existing.Count > 0)
        {
            MessageBox.Show(
                "This user already has a plan. Delete the existing plan first " +
                "(right-click the plan row → Delete Plan), then add a new one.",
                "Plan already exists",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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

    // ── App Active toggle (ON = running, OFF = stopped) ────────────────────
    private async void StopToggle_Checked(object sender, RoutedEventArgs e)
    {
        // Toggle ON = mark app as running (IsStopped = false)
        if (_suppressStopToggle || _selectedUser == null) return;
        await SetStoppedAsync(_selectedUser, false);
    }

    private async void StopToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        // Toggle OFF = stop the app (IsStopped = true)
        if (_suppressStopToggle || _selectedUser == null) return;
        await SetStoppedAsync(_selectedUser, true);
    }

    private async Task SetStoppedAsync(AppUserListItem user, bool stopped)
    {
        try
        {
            await _repo.SetStoppedAsync(user.Id, stopped);
            user.IsStopped       = stopped;
            lblStopStatus.Text   = stopped ? "Stopped" : "Running";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update state: {ex.Message}", "App Active",
                MessageBoxButton.OK, MessageBoxImage.Error);
            // Revert toggle to reflect actual server state (inverted semantics)
            _suppressStopToggle = true;
            tglStopApp.IsChecked = !user.IsStopped;
            _suppressStopToggle  = false;
        }
    }

    // ── Finance Restrictions ────────────────────────────────────────────
    private async void btnSaveRestrictions_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        try
        {
            var items = icFinanceRestrictions.ItemsSource as IEnumerable<FinanceRestrictionItem>;
            if (items == null) return;
            var restricted = items.Where(f => f.IsRestricted).Select(f => f.Id).ToList();
            await _repo.SetFinanceRestrictionsAsync(_selectedUser.Id, restricted);
            MessageBox.Show("Finance restrictions saved.", "Restrictions",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save restrictions: {ex.Message}", "Restrictions",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
