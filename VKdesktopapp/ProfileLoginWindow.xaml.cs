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

    public ProfileLoginWindow(string mode)
    {
        InitializeComponent();
        lblTitle.Text = mode;
        lblHint.Text = "Enter your mobile number and profile password to open " + mode + ".";
        Loaded += (_, __) => txtMobile.Focus();
    }

    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) btnOk_Click(sender, new RoutedEventArgs());
    }

    private async void btnOk_Click(object sender, RoutedEventArgs e)
    {
        var mobile = new string((txtMobile.Text ?? "").Where(char.IsDigit).ToArray());
        var pass = txtPass.Password ?? "";

        if (mobile.Length < 10)
        {
            Fail("Enter your 10-digit mobile number.");
            txtMobile.Focus();
            return;
        }
        if (pass.Length == 0)
        {
            Fail("Enter your profile password.");
            txtPass.Focus();
            return;
        }

        btnOk.IsEnabled = false;
        btnOk.Content = "Checking...";
        try
        {
            var r = await DesktopApiClient.ProfileLoginAsync(mobile, pass);
            if (r.Ok)
            {
                SignedInName = r.Name;
                SignedInMobile = mobile;
                DialogResult = true;
                Close();
                return;
            }
            if (r.Error != null && r.Error.Contains("fingerprint", StringComparison.OrdinalIgnoreCase))
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
            btnOk.Content = "Sign in";
        }
    }

    private void Fail(string message)
    {
        lblError.Text = message;
        lblError.Visibility = Visibility.Visible;
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
