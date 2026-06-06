using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using VRASDesktopApp.Data;
using VRASDesktopApp.Properties;

namespace VRASDesktopApp;

public partial class ServerSettingsWindow : Window
{
    // One row in the "Additional Contact Numbers" list. Two-way bound so the
    // user's edits flow straight back into the collection.
    public class ExtraNumber : INotifyPropertyChanged
    {
        private string _number = "";
        public string Number
        {
            get => _number;
            set { _number = value; OnPropertyChanged(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private const int MaxExtras = 20;
    private readonly ObservableCollection<ExtraNumber> _extras = new();

    public ServerSettingsWindow()
    {
        InitializeComponent();
        icExtras.ItemsSource = _extras;

        // Show the running app's version (Major.Minor.Build) so it always
        // reflects the actual build — driven by <Version> in the csproj.
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (v != null) txtVersion.Text = $"Version {v.Major}.{v.Minor}.{v.Build}";

        Loaded += async (_, __) =>
        {
            // Agency profile (name / address / mobiles) from crm_master.
            try
            {
                var p = await DesktopApiClient.GetAgencyProfileAsync();
                if (p != null)
                {
                    txtAgencyName.Text = p.Name;
                    txtAddress.Text    = p.Address;
                    txtMobile1.Text    = p.Mobile1;
                    txtMobile2.Text    = p.Mobile2;
                    _extras.Clear();
                    foreach (var n in p.Extras) _extras.Add(new ExtraNumber { Number = n });
                    UpdateAddButtonState();
                }
            }
            catch
            {
                // Fall back to whatever the login response gave us.
                var u = App.SignedAppUser;
                if (u != null)
                {
                    txtAgencyName.Text = u.AgencyName;
                    txtAddress.Text    = u.Address;
                    txtMobile1.Text    = u.Mobile1;
                }
            }

            // Common Control Panel password (agency-wide).
            try { pwdControlPass.Password = await DesktopApiClient.GetControlPasswordAsync(); }
            catch { /* silent */ }

            // Subscription-management password.
            try { pwdSubsPass.Password = await DesktopApiClient.GetSubsPasswordAsync(); }
            catch { /* silent — server may not have the table yet */ }
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && WindowState != WindowState.Maximized)
            DragMove();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)  => Close();
    private void btnCancel_Click(object sender, RoutedEventArgs e) => Close();

    // Opens the footer's "Developed by CRMRS" website link + the purchase-contact
    // mailto:/tel: links via the OS default handler (browser, mail client, dialer).
    private void Link_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
        e.Handled = true;
    }

    // ── Extra-number rows ──────────────────────────────────────────────────
    private void btnAddExtra_Click(object sender, RoutedEventArgs e)
    {
        if (_extras.Count >= MaxExtras)
        {
            MessageBox.Show($"You can add up to {MaxExtras} extra numbers.",
                "Limit reached", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _extras.Add(new ExtraNumber());
        UpdateAddButtonState();
    }

    private void btnRemoveExtra_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ExtraNumber row)
        {
            _extras.Remove(row);
            UpdateAddButtonState();
        }
    }

    private void UpdateAddButtonState()
    {
        btnAddExtra.IsEnabled = _extras.Count < MaxExtras;
        txtExtraHint.Text = $"{_extras.Count}/{MaxExtras} extra numbers. All appear on the mobile app's Agency panel.";
    }

    // ── Save ───────────────────────────────────────────────────────────────
    private async void btnSave_Click(object sender, RoutedEventArgs e)
    {
        var name    = txtAgencyName.Text.Trim();
        var mobile1 = txtMobile1.Text.Trim();
        if (name.Length < 2)
        {
            MessageBox.Show("Agency name is required.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(mobile1))
        {
            MessageBox.Show("Primary mobile number is required.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var extras = _extras
            .Select(x => x.Number.Trim())
            .Where(s => s.Length > 0)
            .Distinct()
            .Take(MaxExtras)
            .ToList();

        btnSave.IsEnabled = false;
        try
        {
            await DesktopApiClient.SaveAgencyProfileAsync(
                name, txtAddress.Text.Trim(), mobile1, txtMobile2.Text.Trim(), extras);

            // Keep the in-memory session label in sync so the dashboard
            // top-bar updates without a re-login.
            if (App.SignedAppUser != null)
            {
                App.SignedAppUser.AgencyName = name;
                App.SignedAppUser.Address    = txtAddress.Text.Trim();
                App.SignedAppUser.Mobile1    = mobile1;
            }

            // Common Control Panel password (agency-wide, stored server-side).
            var ctrlPass = pwdControlPass.Password.Trim();
            if (!string.IsNullOrEmpty(ctrlPass))
            {
                try { await DesktopApiClient.SetControlPasswordAsync(ctrlPass); }
                catch { /* non-fatal */ }
            }

            // Subscription-management password (stored server-side, per-tenant).
            var subsPass = pwdSubsPass.Password.Trim();
            if (!string.IsNullOrEmpty(subsPass))
            {
                try { await DesktopApiClient.SetSubsPasswordAsync(subsPass); }
                catch { /* non-fatal */ }
            }

            MessageBox.Show(
                "Saved. These details now show on the mobile app's Agency panel too.",
                "Agency Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Agency Settings",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { btnSave.IsEnabled = true; }
    }

    // ── Session actions ────────────────────────────────────────────────────
    private void btnLogout_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Log out of this agency and return to the sign-in screen?",
            "Log Out", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        // Clear the session and relaunch the app fresh at the login screen.
        // Restarting the process is the clean way back to LoginWindow given
        // the app launches MainWindow via LoginWindow.ShowDialog().
        App.SignedAppUser = null;
        App.HttpClient.DefaultRequestHeaders.Authorization = null;
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe)) Process.Start(exe);
        }
        catch { }
        Application.Current.Shutdown();
    }

    private void btnExitApp_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Exit the application?", "Exit",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
            Application.Current.Shutdown();
    }
}
