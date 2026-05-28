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

        // Load PFP — server may return a full URL (file-stored PFPs) or legacy
        // base64. Detect and load appropriately.
        await SetAvatarAsync(user.PfpBase64);

        // App Active toggle — ON = app running, OFF = stopped (inverted semantics)
        _suppressStopToggle = true;
        tglStopApp.IsChecked = !user.IsStopped;
        lblStopStatus.Text   = user.IsStopped ? "Stopped" : "Running";
        _suppressStopToggle  = false;

        pnlEmpty.Visibility   = Visibility.Collapsed;
        pnlProfile.Visibility = Visibility.Visible;
        btnUserActions.Visibility = Visibility.Visible;
        RefreshActionLabels(user);

        // Kick off all four secondary loads in parallel — previously these ran
        // sequentially which meant the user waited 4× the longest call before
        // KYC images even started loading.
        // Control Panel password is now agency-wide (Agency Settings), no
        // longer per-user — so it's not loaded here any more.
        await Task.WhenAll(
            LoadSubscriptionsAsync(user.Id),
            LoadFinanceRestrictionsAsync(user.Id),
            LoadKycAsync(user.Id));
    }

    // ── All-in-one Actions menu ─────────────────────────────────────────────
    // Labels flip to reflect the user's current state so one menu both sets
    // and clears each flag.
    private void RefreshActionLabels(AppUserListItem u)
    {
        miStopApp.Header   = u.IsStopped     ? "Start App"        : "Stop App";
        miBlacklist.Header = u.IsBlacklisted ? "Remove Blacklist" : "Blacklist User";
        miAdmin.Header     = u.IsAdmin        ? "Remove Admin"     : "Make Admin";
        miActive.Header    = u.IsActive       ? "Deactivate User"  : "Activate User";
    }

    private void btnUserActions_Click(object sender, RoutedEventArgs e)
    {
        if (btnUserActions.ContextMenu is { } cm)
        {
            cm.PlacementTarget = btnUserActions;
            cm.IsOpen = true;
        }
    }

    private async void ActReset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var c = MessageBox.Show($"Reset device for {_selectedUser.Name}?\nThey can log in from a new device.",
            "Reset Device", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (c != MessageBoxResult.Yes) return;
        try { await _repo.ResetDeviceAsync(_selectedUser.Id); _selectedUser.DeviceId = null; txtDeviceId.Text = "(no device registered)"; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Reset Device", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ActStop_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var stop = !_selectedUser.IsStopped;
        try
        {
            await _repo.SetStoppedAsync(_selectedUser.Id, stop);
            _selectedUser.IsStopped = stop;
            _suppressStopToggle = true; tglStopApp.IsChecked = !stop; lblStopStatus.Text = stop ? "Stopped" : "Running"; _suppressStopToggle = false;
            RefreshActionLabels(_selectedUser);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Stop App", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ActBlacklist_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var bl = !_selectedUser.IsBlacklisted;
        try
        {
            await _repo.SetBlacklistedAsync(_selectedUser.Id, bl);
            _selectedUser.IsBlacklisted = bl;
            if (bl) _selectedUser.IsStopped = true;   // blacklisting also stops
            RefreshActionLabels(_selectedUser);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Blacklist", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ActAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var admin = !_selectedUser.IsAdmin;
        try { await _repo.SetAdminAsync(_selectedUser.Id, admin); _selectedUser.IsAdmin = admin; RefreshActionLabels(_selectedUser); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Admin", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ActActive_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var active = !_selectedUser.IsActive;
        try { await _repo.SetActiveAsync(_selectedUser.Id, active); _selectedUser.IsActive = active; RefreshActionLabels(_selectedUser); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Active", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ActDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var c = MessageBox.Show(
            $"Permanently delete {_selectedUser.Name} ({_selectedUser.Mobile})?\n\n" +
            "This wipes their profile, subscriptions, KYC and device record, and frees the mobile/device to register elsewhere.\n\nThis cannot be undone.",
            "Delete User", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (c != MessageBoxResult.Yes) return;
        try { await _repo.DeleteUserAsync(_selectedUser.Id); _selectedUser = null; pnlProfile.Visibility = Visibility.Collapsed; btnUserActions.Visibility = Visibility.Collapsed; pnlEmpty.Visibility = Visibility.Visible; txtRightTitle.Text = "Select a user"; await LoadUsersAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Delete User", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    // Faster, smoother wheel scrolling on the profile panel — the default WPF
    // step felt sluggish on the long detail panel.
    private void pnlProfile_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);   // 1:1 wheel delta
            e.Handled = true;
        }
    }

    // Loads PFP into imgPfp from either a URL or legacy base64. Falls back to initials.
    private async Task SetAvatarAsync(string? pfpRaw)
    {
        if (string.IsNullOrWhiteSpace(pfpRaw))
        {
            imgPfp.Visibility = Visibility.Collapsed;
            txtAvatar.Visibility = Visibility.Visible;
            return;
        }
        try
        {
            byte[] bytes;
            if (pfpRaw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                bytes = await App.HttpClient.GetByteArrayAsync(pfpRaw);
            else
                bytes = Convert.FromBase64String(pfpRaw);

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

    // ── KYC documents ───────────────────────────────────────────────────
    private DesktopApiClient.KycDocsDto? _kycDocs;

    private async Task LoadKycAsync(long userId)
    {
        imgKycAadhaarFront.Visibility = Visibility.Collapsed;
        imgKycAadhaarBack.Visibility  = Visibility.Collapsed;
        imgKycPanFront.Visibility     = Visibility.Collapsed;
        txtKycAadhaarFrontEmpty.Visibility = Visibility.Visible;
        txtKycAadhaarBackEmpty.Visibility  = Visibility.Visible;
        txtKycPanFrontEmpty.Visibility     = Visibility.Visible;
        txtKycAadhaarFrontEmpty.Text = "Loading…";
        txtKycAadhaarBackEmpty.Text  = "Loading…";
        txtKycPanFrontEmpty.Text     = "Loading…";
        _kycDocs = null;

        DesktopApiClient.KycDocsDto? docs = null;
        try { docs = await _repo.GetKycAsync(userId); }
        catch { /* user may have no KYC yet */ }
        _kycDocs = docs;

        // Restore the "Not uploaded" placeholder text so any tile that ends up
        // empty doesn't sit on "Loading…".
        txtKycAadhaarFrontEmpty.Text = "Not uploaded";
        txtKycAadhaarBackEmpty.Text  = "Not uploaded";
        txtKycPanFrontEmpty.Text     = "Not uploaded";

        if (docs == null) return;

        // Download all three image bytes in parallel — previously sequential
        // awaits made a single-doc load take 3× longer than necessary, and
        // the UI showed "Not uploaded" for the entire duration.
        var aFrontTask = LoadKycBytesAsync(docs.AadhaarFront);
        var aBackTask  = LoadKycBytesAsync(docs.AadhaarBack);
        var pFrontTask = LoadKycBytesAsync(docs.PanFront);
        await Task.WhenAll(aFrontTask, aBackTask, pFrontTask);

        ApplyKycImage(imgKycAadhaarFront, txtKycAadhaarFrontEmpty, await aFrontTask);
        ApplyKycImage(imgKycAadhaarBack,  txtKycAadhaarBackEmpty,  await aBackTask);
        ApplyKycImage(imgKycPanFront,     txtKycPanFrontEmpty,     await pFrontTask);
    }

    // Downloads the bytes for one KYC image — runs off the UI thread. Returns
    // null on any failure (missing URL, network error) so the caller can show
    // the empty state.
    private static async Task<byte[]?> LoadKycBytesAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try { return await App.HttpClient.GetByteArrayAsync(url); }
        catch { return null; }
    }

    private static void ApplyKycImage(Image img, TextBlock emptyLabel, byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            img.Visibility = Visibility.Collapsed;
            emptyLabel.Visibility = Visibility.Visible;
            return;
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;
            img.Visibility = Visibility.Visible;
            emptyLabel.Visibility = Visibility.Collapsed;
        }
        catch
        {
            img.Visibility = Visibility.Collapsed;
            emptyLabel.Visibility = Visibility.Visible;
        }
    }

    private string? GetKycUrl(string docType) => docType switch
    {
        "aadhaar_front" => _kycDocs?.AadhaarFront,
        "aadhaar_back"  => _kycDocs?.AadhaarBack,
        "pan_front"     => _kycDocs?.PanFront,
        _ => null
    };

    private async void btnKycDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string docType)
            await DownloadKycAsync(docType);
    }

    // Shared download — used by the per-tile Download button AND the preview
    // window's Download button.
    private async Task DownloadKycAsync(string docType)
    {
        if (_selectedUser == null) return;
        var url = GetKycUrl(docType);
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("This document is not uploaded.", "KYC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title    = $"Save {docType.Replace('_', ' ')}",
            FileName = $"{_selectedUser.Mobile}_{docType}.jpg",
            Filter   = "JPEG image (*.jpg)|*.jpg|All files (*.*)|*.*",
            DefaultExt = "jpg"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = await App.HttpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            MessageBox.Show($"Saved to {dlg.FileName}", "Download Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Download failed: {ex.Message}", "Download",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void btnKycDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string docType)
            await DeleteKycAsync(docType);
    }

    // Returns true if the doc was deleted (so the preview window can close).
    private async Task<bool> DeleteKycAsync(string docType)
    {
        if (_selectedUser == null) return false;
        var confirm = MessageBox.Show(
            $"Delete {docType.Replace('_', ' ')} for {_selectedUser.Name}?\nThis cannot be undone.",
            "Delete KYC Document",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return false;
        try
        {
            await _repo.DeleteKycAsync(_selectedUser.Id, docType);
            await LoadKycAsync(_selectedUser.Id);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete: {ex.Message}", "Delete KYC",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    // Refresh the user list on demand.
    private async void btnRefreshUsers_Click(object sender, RoutedEventArgs e)
    {
        btnRefreshUsers.IsEnabled = false;
        try { await LoadUsersAsync(); }
        finally { btnRefreshUsers.IsEnabled = true; }
    }

    // Click a KYC thumbnail → open a large preview with Download / Delete.
    private void KycImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Image img || img.Source == null) return;
        if (img.Tag is not string docType) return;

        var win = new Window
        {
            Title  = docType.Replace('_', ' ').ToUpperInvariant() +
                     (_selectedUser != null ? $"  —  {_selectedUser.Name}" : ""),
            Width  = 860,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner  = Window.GetWindow(this),
            Background = System.Windows.Media.Brushes.White
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());                                  // image fills
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });       // button bar

        // Zoomable-ish: Uniform stretch in a scroll viewer so big scans are readable.
        var preview = new Image
        {
            Source  = img.Source,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Margin  = new Thickness(16)
        };
        var sv = new ScrollViewer
        {
            Content = preview,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto
        };
        Grid.SetRow(sv, 0);
        grid.Children.Add(sv);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 16)
        };
        var dl = new Button
        {
            Content = "↓  Download", Height = 36, MinWidth = 120,
            Margin = new Thickness(0, 0, 10, 0), Cursor = System.Windows.Input.Cursors.Hand,
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = (System.Windows.Media.Brush)FindResource("Gray300"),
            BorderThickness = new Thickness(1), FontSize = 14, Padding = new Thickness(14, 0, 14, 0)
        };
        dl.Click += async (_, __) => await DownloadKycAsync(docType);
        var del = new Button
        {
            Content = "✕  Delete", Height = 36, MinWidth = 120,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = (System.Windows.Media.Brush)FindResource("Red500"),
            Foreground  = (System.Windows.Media.Brush)FindResource("Red500"),
            BorderThickness = new Thickness(1), FontSize = 14, Padding = new Thickness(14, 0, 14, 0)
        };
        del.Click += async (_, __) =>
        {
            if (await DeleteKycAsync(docType)) win.Close();
        };
        bar.Children.Add(dl);
        bar.Children.Add(del);
        Grid.SetRow(bar, 1);
        grid.Children.Add(bar);

        win.Content = grid;
        win.ShowDialog();
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

    // ── Delete user ───────────────────────────────────────────────────
    // Calls the server-side delete that cascades the tenant row AND releases
    // the cross-agency claim in crm_master.app_user_registry. Without this,
    // an admin who removed the row directly from app_users in MySQL would
    // permanently block this mobile / device from registering anywhere else.
    private async void btnDeleteUser_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var confirm = MessageBox.Show(
            $"Permanently delete {_selectedUser.Name} ({_selectedUser.Mobile})?\n\n" +
            "This wipes their profile, subscriptions, KYC documents and device record. " +
            "Their mobile and device will be released so they can register with any " +
            "agency afterwards.\n\n" +
            "This cannot be undone.",
            "Delete User", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            await _repo.DeleteUserAsync(_selectedUser.Id);
            _selectedUser = null;
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete user: {ex.Message}", "Delete User",
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
