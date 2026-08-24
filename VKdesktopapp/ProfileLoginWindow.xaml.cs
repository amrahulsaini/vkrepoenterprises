using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public partial class ProfileLoginWindow : Window
{
    public string SignedInName { get; private set; } = "";
    public string SignedInMobile { get; private set; } = "";
    public bool NeedsFingerprint { get; private set; }
    public bool ChoseFingerprint { get; private set; }
    public bool ChangeAgencyRequested { get; private set; }
    public string EnteredMobile { get { return _mobile; } }
    public string[] Modules { get; private set; } = System.Array.Empty<string>();
    public string Role { get; private set; } = "";

    private string _mobile = "";
    private bool _canUsePassword = true;

    public ProfileLoginWindow(string mode, bool offerChangeAgency = false)
    {
        InitializeComponent();
        lblAgency.Text = AgencyBranding.Name;
        imgAgency.Source = AgencyBranding.LoadLogo() ?? AgencyBranding.DefaultLogo();
        lblMode.Text = string.IsNullOrWhiteSpace(mode) || mode == "CRMRS"
            ? "PROFILE SIGN IN"
            : mode.ToUpperInvariant();
        if (offerChangeAgency) pnlChangeAgency.Visibility = Visibility.Visible;
        Loaded += (_, __) => txtMobile.Focus();
    }

    private void btnChangeAgency_Click(object sender, RoutedEventArgs e)
    {
        var ask = MessageBox.Show(
            "Sign out of " + lblAgency.Text + " on this computer and sign in to a different agency?",
            "Change agency", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;

        ChangeAgencyRequested = true;
        DialogResult = false;
        Close();
    }

    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (pnlMethod.Visibility == Visibility.Visible && !_canUsePassword)
        {
            btnFingerprint_Click(sender, new RoutedEventArgs());
            return;
        }
        btnOk_Click(sender, new RoutedEventArgs());
    }

    private void btnOk_Click(object sender, RoutedEventArgs e)
    {
        if (pnlMethod.Visibility != Visibility.Visible) _ = ContinueFromMobileAsync();
        else _ = SignInWithPasswordAsync();
    }

    private async System.Threading.Tasks.Task ContinueFromMobileAsync()
    {
        var mobile = new string((txtMobile.Text ?? "").Where(char.IsDigit).ToArray());
        if (mobile.Length < 10)
        {
            Fail("Enter your 10-digit mobile number.");
            txtMobile.Focus();
            return;
        }

        ClearError();
        btnOk.IsEnabled = false;
        btnOk.Content = "Checking...";
        DesktopApiClient.ProfileMethods? m;
        try { m = await DesktopApiClient.ProfileMethodsAsync(mobile); }
        catch { m = null; }
        btnOk.IsEnabled = true;
        btnOk.Content = "Continue";

        if (m is { Found: false })
        {
            Fail("No profile has that mobile number. Check the number, or ask your administrator to add you in HRMS.");
            txtMobile.Focus();
            txtMobile.SelectAll();
            return;
        }
        if (m is { Allowed: false })
        {
            Fail(m.BlockReason.Length > 0 ? m.BlockReason : "This profile is not allowed to sign in.");
            return;
        }
        if (m is { HasPassword: false, FingerprintEnrolled: false })
        {
            Fail("This profile has no way to sign in yet. Ask your administrator to set a "
                 + "profile password in HRMS, or to turn on fingerprint so you can set it up on your phone.");
            return;
        }

        _mobile = mobile;
        ShowMethodStep(m);
    }

    private void ShowMethodStep(DesktopApiClient.ProfileMethods? m)
    {
        bool knowMethods = m is not null;
        bool hasPassword = !knowMethods || m!.HasPassword;
        bool enrolled    = !knowMethods || m!.FingerprintEnrolled;
        bool fpRequired  = knowMethods && m!.FingerprintRequired;

        _canUsePassword = hasPassword && !fpRequired;

        if (knowMethods && m!.Name.Length > 0)
        {
            lblWhoLabel.Text = "SIGNING IN AS";
            lblMobileEcho.Text = m.Name + "  ·  " + _mobile;
        }
        else
        {
            lblWhoLabel.Text = "MOBILE NUMBER";
            lblMobileEcho.Text = _mobile;
        }

        pnlPassword.Visibility = _canUsePassword ? Visibility.Visible : Visibility.Collapsed;
        pnlOr.Visibility       = _canUsePassword ? Visibility.Visible : Visibility.Collapsed;
        btnOk.Visibility       = _canUsePassword ? Visibility.Visible : Visibility.Collapsed;

        btnFingerprint.IsEnabled = enrolled;
        btnFingerprint.Content = enrolled ? "Sign in with fingerprint" : "Fingerprint not set up";
        btnFingerprint.Foreground = new System.Windows.Media.SolidColorBrush(
            enrolled ? System.Windows.Media.Color.FromRgb(0xCC, 0x3C, 0x00)
                     : System.Windows.Media.Color.FromRgb(0x9A, 0x9A, 0xA3));

        lblFpHint.Text =
            !enrolled  ? "No fingerprint has been set up for this profile. Ask your administrator to "
                         + "turn it on in HRMS, then set it up in the CRMRS app on your phone."
          : fpRequired ? "This profile signs in by fingerprint only. Scan the code with the CRMRS app "
                         + "on your phone and confirm."
                       : "Scan a code with the CRMRS app on your phone and confirm with your fingerprint. "
                         + "No password needed.";

        lblHint.Text =
            !_canUsePassword && fpRequired ? "Your administrator requires a fingerprint for this profile."
          : !_canUsePassword               ? "No profile password is set, so use your fingerprint."
                                           : "Enter your profile password, or confirm with your fingerprint instead.";

        pnlMobile.Visibility = Visibility.Collapsed;
        pnlMethod.Visibility = Visibility.Visible;
        btnOk.Content = "Sign in";
        if (_canUsePassword) txtPass.Focus(); else btnFingerprint.Focus();
    }

    private void btnEditMobile_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        txtPass.Clear();
        lblHint.Text = "Enter your mobile number to continue.";
        pnlMethod.Visibility = Visibility.Collapsed;
        pnlMobile.Visibility = Visibility.Visible;
        btnOk.Visibility = Visibility.Visible;
        btnOk.Content = "Continue";
        txtMobile.Focus();
        txtMobile.SelectAll();
    }

    private void btnFingerprint_Click(object sender, RoutedEventArgs e)
    {
        ChoseFingerprint = true;
        DialogResult = false;
        Close();
    }

    private async System.Threading.Tasks.Task SignInWithPasswordAsync()
    {
        var pass = txtPass.Password ?? "";
        if (pass.Length == 0)
        {
            Fail("Enter your profile password.");
            txtPass.Focus();
            return;
        }

        btnOk.IsEnabled = false;
        btnFingerprint.IsEnabled = false;
        btnOk.Content = "Checking...";
        try
        {
            var r = await DesktopApiClient.ProfileLoginAsync(_mobile, pass);
            if (r.Ok)
            {
                SignedInName = r.Name;
                SignedInMobile = _mobile;
                Modules = r.Modules;
                Role = r.Role;
                DialogResult = true;
                Close();
                return;
            }
            if (r.NeedsFingerprint)
            {
                NeedsFingerprint = true;
                DialogResult = false;
                Close();
                return;
            }
            Fail(r.Error);
            txtPass.Clear();
            txtPass.Focus();
        }
        catch
        {
            Fail("Cannot reach the server. Check your connection and try again.");
        }
        finally
        {
            btnOk.IsEnabled = true;
            btnFingerprint.IsEnabled = true;
            btnOk.Content = "Sign in";
        }
    }

    private void Fail(string message)
    {
        lblError.Text = message;
        lblError.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        lblError.Text = "";
        lblError.Visibility = Visibility.Collapsed;
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
