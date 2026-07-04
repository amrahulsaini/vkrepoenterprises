using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CRMRSDesktopApp.Data;
using CRMRSDesktopApp.Models;

namespace CRMRSDesktopApp.AppUsers;

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
        if (_allUsers == null) return;
        IEnumerable<AppUserListItem> list = _allUsers;

        switch (cmbFilter?.SelectedIndex ?? 0)
        {
            case 1: list = list.Where(u => u.IsActive); break;
            case 2: list = list.Where(u => u.IsAdmin); break;
            case 3: list = list.Where(u => !u.IsAdmin); break;
        }

        var q = txtSearch.Text.Trim();
        if (!string.IsNullOrEmpty(q))
            list = list.Where(u =>
                u.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                u.Mobile.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (u.Address?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

        icUsers.ItemsSource = new ObservableCollection<AppUserListItem>(list);
    }

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void cmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

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

        await SetAvatarAsync(user.PfpBase64);

        _suppressStopToggle = true;
        tglStopApp.IsChecked = !user.IsStopped;
        lblStopStatus.Text   = user.IsStopped ? "Stopped" : "Running";
        _suppressStopToggle  = false;

        pnlEmpty.Visibility   = Visibility.Collapsed;
        pnlProfile.Visibility = Visibility.Visible;
        btnUserActions.Visibility = Visibility.Visible;
        btnRefreshUser.Visibility = Visibility.Visible;
        RefreshActionLabels(user);

        await Task.WhenAll(
            LoadSubscriptionsAsync(user.Id),
            LoadFinanceRestrictionsAsync(user.Id),
            LoadKycAsync(user.Id));
    }

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
            if (bl) _selectedUser.IsStopped = true;
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

    private async void btnRefreshUser_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        btnRefreshUser.IsEnabled = false;
        try
        {
            await Task.WhenAll(
                LoadSubscriptionsAsync(_selectedUser.Id),
                LoadFinanceRestrictionsAsync(_selectedUser.Id),
                LoadKycAsync(_selectedUser.Id));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Refresh", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { btnRefreshUser.IsEnabled = true; }
    }

    private async void ActActive_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var active = !_selectedUser.IsActive;
        if (active && !string.Equals(_kycStatus, "success", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "This agent's KYC is not verified yet. Review their documents and click \"Mark Verified\" first.",
                "KYC required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
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

    private void pnlProfile_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

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

    private DesktopApiClient.KycDocsDto? _kycDocs;

    private async Task LoadKycAsync(long userId)
    {
        imgKycAadhaarFront.Visibility = Visibility.Collapsed;
        imgKycAadhaarBack.Visibility  = Visibility.Collapsed;
        imgKycPanFront.Visibility     = Visibility.Collapsed;
        imgKycSelfie.Visibility       = Visibility.Collapsed;
        imgKycUidaiPhoto.Visibility   = Visibility.Collapsed;
        txtKycAadhaarFrontEmpty.Visibility = Visibility.Visible;
        txtKycAadhaarBackEmpty.Visibility  = Visibility.Visible;
        txtKycPanFrontEmpty.Visibility     = Visibility.Visible;
        txtKycSelfieEmpty.Visibility       = Visibility.Visible;
        txtKycUidaiPhotoEmpty.Visibility   = Visibility.Visible;
        txtKycAadhaarFrontEmpty.Text = "Loading…";
        txtKycAadhaarBackEmpty.Text  = "Loading…";
        txtKycPanFrontEmpty.Text     = "Loading…";
        txtKycSelfieEmpty.Text       = "Loading…";
        txtKycUidaiPhotoEmpty.Text   = "Loading…";
        ClearKycDetails();
        _kycDocs = null;

        DesktopApiClient.KycDocsDto? docs = null;
        try { docs = await _repo.GetKycAsync(userId); }
        catch { }
        _kycDocs = docs;

        txtKycAadhaarFrontEmpty.Text = "Not uploaded";
        txtKycAadhaarBackEmpty.Text  = "Not uploaded";
        txtKycPanFrontEmpty.Text     = "Not uploaded";
        txtKycSelfieEmpty.Text       = "Not uploaded";
        txtKycUidaiPhotoEmpty.Text   = "Not available";

        if (docs == null) return;

        PopulateKycDetails(docs);

        var aFrontTask = LoadKycBytesAsync(docs.AadhaarFront);
        var aBackTask  = LoadKycBytesAsync(docs.AadhaarBack);
        var pFrontTask = LoadKycBytesAsync(docs.PanFront);
        var selfieTask = LoadKycBytesAsync(docs.Selfie ?? "");
        var uidaiTask  = LoadKycBytesAsync(docs.AadhaarPhoto ?? "");
        await Task.WhenAll(aFrontTask, aBackTask, pFrontTask, selfieTask, uidaiTask);

        ApplyKycImage(imgKycAadhaarFront, txtKycAadhaarFrontEmpty, await aFrontTask);
        ApplyKycImage(imgKycAadhaarBack,  txtKycAadhaarBackEmpty,  await aBackTask);
        ApplyKycImage(imgKycPanFront,     txtKycPanFrontEmpty,     await pFrontTask);
        ApplyKycImage(imgKycSelfie,       txtKycSelfieEmpty,       await selfieTask);
        ApplyKycImage(imgKycUidaiPhoto,   txtKycUidaiPhotoEmpty,   await uidaiTask);
    }

    private string _kycStatus = "success";

    private void ClearKycDetails()
    {
        txtKycAaVerified.Text   = "Aadhaar: Not verified";
        txtKycAaVerified.Foreground = (System.Windows.Media.Brush)FindResource("Gray700");
        txtKycAaNumber.Text  = "—";
        txtKycAaName.Text    = "—";
        txtKycAaDob.Text     = "—";
        txtKycAaGender.Text  = "—";
        txtKycAaAddress.Text = "—";
        txtKycLocation.Text  = "—";
        _kycStatus = "success";
        txtKycStatusBadge.Text = "KYC: —";
        txtKycRejectNote.Visibility = Visibility.Collapsed;
        SetBadge((System.Windows.Media.Brush)FindResource("Gray100"),
                 (System.Windows.Media.Brush)FindResource("Gray700"));
    }

    private void SetBadge(System.Windows.Media.Brush bg, System.Windows.Media.Brush fg)
    {
        brdKycStatusBadge.Background = bg;
        txtKycStatusBadge.Foreground = fg;
    }

    private void PopulateKycDetails(DesktopApiClient.KycDocsDto docs)
    {
        var a = docs.Aadhaar;
        bool verified = a?.Verified == true;
        txtKycAaVerified.Text = verified ? "✓ Aadhaar verified with UIDAI (OKYC)" : "Aadhaar not OTP-verified";
        txtKycAaVerified.Foreground = verified
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A))
            : (System.Windows.Media.Brush)FindResource("Gray700");

        string number = !string.IsNullOrWhiteSpace(a?.Number) ? a!.Number!
                       : !string.IsNullOrWhiteSpace(a?.Last4)  ? $"XXXX XXXX {a!.Last4}" : "—";
        txtKycAaNumber.Text  = number;
        txtKycAaName.Text    = string.IsNullOrWhiteSpace(a?.Name)   ? "—" : a!.Name!;
        txtKycAaDob.Text     = string.IsNullOrWhiteSpace(a?.Dob)    ? "—" : a!.Dob!;
        txtKycAaGender.Text  = string.IsNullOrWhiteSpace(a?.Gender) ? "—" : a!.Gender!;
        txtKycAaAddress.Text = string.IsNullOrWhiteSpace(a?.Address)? "—" : a!.Address!;

        var loc = docs.Location;
        if (loc?.Lat != null && loc.Lng != null)
        {
            var label = string.IsNullOrWhiteSpace(loc.Label) ? "" : $"{loc.Label}  ";
            txtKycLocation.Text = $"{label}({loc.Lat:F5}, {loc.Lng:F5})";
        }
        else txtKycLocation.Text = "—";

        _kycStatus = (docs.KycStatus ?? "success").Trim().ToLowerInvariant();
        switch (_kycStatus)
        {
            case "success":
                txtKycStatusBadge.Text = "KYC: VERIFIED";
                SetBadge(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0xFC, 0xE7)),
                         new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x15, 0x80, 0x3D)));
                txtKycRejectNote.Visibility = Visibility.Collapsed;
                break;
            case "failed":
                txtKycStatusBadge.Text = "KYC: REJECTED";
                SetBadge(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFE, 0xE2, 0xE2)),
                         (System.Windows.Media.Brush)FindResource("Red500"));
                txtKycRejectNote.Text = string.IsNullOrWhiteSpace(docs.RejectNote)
                    ? "Rejected (no note)." : $"Note: {docs.RejectNote}";
                txtKycRejectNote.Visibility = Visibility.Visible;
                break;
            default:
                txtKycStatusBadge.Text = "KYC: PENDING REVIEW";
                SetBadge(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFE, 0xF9, 0xC3)),
                         new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x85, 0x4D, 0x0E)));
                txtKycRejectNote.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private async void btnKycVerify_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        try
        {
            await _repo.SetKycStatusAsync(_selectedUser.Id, "success", null);
            _kycStatus = "success";
            await LoadKycAsync(_selectedUser.Id);
            MessageBox.Show("KYC marked as verified. You can now activate this agent.",
                "KYC Verified", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        { MessageBox.Show(ex.Message, "KYC", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void btnKycReject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var (ok, note) = PromptForNote(
            $"Reject {_selectedUser.Name}'s KYC",
            "Reason (optional) — the agent sees this and re-submits. Rejecting also deactivates the account.");
        if (!ok) return;
        try
        {
            await _repo.SetKycStatusAsync(_selectedUser.Id, "failed", string.IsNullOrWhiteSpace(note) ? null : note);
            _kycStatus = "failed";
            _selectedUser.IsActive = false;
            RefreshActionLabels(_selectedUser);
            await LoadKycAsync(_selectedUser.Id);
            MessageBox.Show("KYC rejected. The agent will be asked to re-submit.",
                "KYC Rejected", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        { MessageBox.Show(ex.Message, "KYC", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void btnKycDeleteUidai_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        var confirm = MessageBox.Show(
            $"Delete the UIDAI photo and verified Aadhaar details for {_selectedUser.Name}?\n\n" +
            "This removes the UIDAI photo and the name / DOB / gender / address / Aadhaar number " +
            "fetched from UIDAI. This cannot be undone.",
            "Delete UIDAI data", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var (ok, pwd) = PromptForPassword(
            "Confirm with your password",
            "Enter your login password to delete the UIDAI photo and details.");
        if (!ok) return;
        if (string.IsNullOrEmpty(pwd))
        {
            MessageBox.Show("Password is required.", "Delete UIDAI data", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var email = App.SignedAppUser?.Email ?? "";
            var valid = await _repo.VerifyLoginPasswordAsync(email, pwd);
            if (!valid)
            {
                MessageBox.Show("Incorrect password. UIDAI data was not deleted.",
                    "Delete UIDAI data", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            await _repo.DeleteKycUidaiAsync(_selectedUser.Id);
            await LoadKycAsync(_selectedUser.Id);
            MessageBox.Show("UIDAI photo and verified Aadhaar details deleted.",
                "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        { MessageBox.Show(ex.Message, "Delete UIDAI data", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private (bool ok, string pwd) PromptForPassword(string title, string prompt)
    {
        var win = new Window
        {
            Title = title, Width = 420, Height = 200, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this),
            Background = System.Windows.Media.Brushes.White
        };
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var box = new PasswordBox { Height = 32, Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(box);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var okBtn  = new Button { Content = "Confirm delete", MinWidth = 120, Height = 32, IsDefault = true };
        bool result = false;
        okBtn.Click += (_, __) => { result = true; win.DialogResult = true; };
        cancel.Click += (_, __) => { win.DialogResult = false; };
        bar.Children.Add(cancel); bar.Children.Add(okBtn);
        root.Children.Add(bar);
        win.Content = root;
        box.Loaded += (_, __) => box.Focus();
        win.ShowDialog();
        return (result, box.Password ?? "");
    }

    private (bool ok, string text) PromptForNote(string title, string prompt)
    {
        var win = new Window
        {
            Title = title, Width = 460, Height = 220, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this),
            Background = System.Windows.Media.Brushes.White
        };
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var box = new TextBox { MinHeight = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(box);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var okBtn  = new Button { Content = "Reject KYC", MinWidth = 110, Height = 32, IsDefault = true };
        bool result = false;
        okBtn.Click += (_, __) => { result = true; win.DialogResult = true; };
        cancel.Click += (_, __) => { win.DialogResult = false; };
        bar.Children.Add(cancel); bar.Children.Add(okBtn);
        root.Children.Add(bar);
        win.Content = root;
        box.Focus();
        win.ShowDialog();
        return (result, box.Text ?? "");
    }

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
        "selfie"        => _kycDocs?.Selfie,
        "aadhaar_photo" => _kycDocs?.AadhaarPhoto,
        _ => null
    };

    private async void btnKycDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string docType)
            await DownloadKycAsync(docType);
    }

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

    private async void btnRefreshUsers_Click(object sender, RoutedEventArgs e)
    {
        btnRefreshUsers.IsEnabled = false;
        try { await LoadUsersAsync(); }
        finally { btnRefreshUsers.IsEnabled = true; }
    }

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
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
        catch { }
    }

    private async Task LoadSubscriptionsAsync(long userId)
    {
        try
        {
            var subs = await _repo.GetSubscriptionsAsync(userId);
            dgSubscriptions.ItemsSource = subs;
            var total = subs.Sum(s => s.Amount);
            txtProfileBalance.Text = $"₹{total:N2}";
            if (_selectedUser != null && _selectedUser.Id == userId)
                _selectedUser.Balance = total;
        }
        catch { }
    }

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
            var (total, a, admins, withSub) = await _repo.GetStatsAsync();
            lblActive.Text = a.ToString("N0");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update active state: {ex.Message}", "Users",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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

    private async void btnAddSubscription_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;

        var existing = await _repo.GetSubscriptionsAsync(_selectedUser.Id);
        if (existing.Any(s => s.IsActive))
        {
            MessageBox.Show(
                "This user already has an active plan. Add a new plan once the current one expires, " +
                "or delete the active plan first (right-click the plan row → Delete Plan).",
                "Active plan exists",
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

    private async void StopToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressStopToggle || _selectedUser == null) return;
        await SetStoppedAsync(_selectedUser, false);
    }

    private async void StopToggle_Unchecked(object sender, RoutedEventArgs e)
    {
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
            _suppressStopToggle = true;
            tglStopApp.IsChecked = !user.IsStopped;
            _suppressStopToggle  = false;
        }
    }

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
