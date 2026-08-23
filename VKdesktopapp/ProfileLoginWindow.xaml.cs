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
    public string[] Modules { get; private set; } = System.Array.Empty<string>();
    public string Role { get; private set; } = "";

    private string _mobile = "";

    public ProfileLoginWindow(string mode)
    {
        InitializeComponent();
        lblTitle.Text = mode;
        Loaded += (_, __) => txtMobile.Focus();
    }

    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) btnOk_Click(sender, new RoutedEventArgs());
    }

    private void btnOk_Click(object sender, RoutedEventArgs e)
    {
        if (pnlMethod.Visibility != Visibility.Visible) ContinueFromMobile();
        else _ = SignInWithPasswordAsync();
    }

    private void ContinueFromMobile()
    {
        var mobile = new string((txtMobile.Text ?? "").Where(char.IsDigit).ToArray());
        if (mobile.Length < 10)
        {
            Fail("Enter your 10-digit mobile number.");
            txtMobile.Focus();
            return;
        }

        _mobile = mobile;
        ClearError();
        lblMobileEcho.Text = mobile;
        lblHint.Text = "Enter your profile password, or confirm with your fingerprint instead.";
        pnlMobile.Visibility = Visibility.Collapsed;
        pnlMethod.Visibility = Visibility.Visible;
        btnOk.Content = "Sign in";
        txtPass.Focus();
    }

    private void btnEditMobile_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        txtPass.Clear();
        lblHint.Text = "Enter your mobile number to continue.";
        pnlMethod.Visibility = Visibility.Collapsed;
        pnlMobile.Visibility = Visibility.Visible;
        btnOk.Content = "Continue";
        txtMobile.Focus();
        txtMobile.SelectAll();
    }

    /// The phone decides who signs in, so the fingerprint route needs no
    /// password at all: hand straight over to the QR window.
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
