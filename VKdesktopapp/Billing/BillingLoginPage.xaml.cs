using System;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Billing;

public partial class BillingLoginPage : Page
{
    public BillingLoginPage()
    {
        InitializeComponent();
        Loaded += (_, __) => txtUsername.Focus();
    }

    private async void btnLogin_Click(object sender, RoutedEventArgs e)
    {
        var username = txtUsername.Text.Trim();
        var password = txtPassword.Password;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            txtStatus.Text = "Enter your email and password."; return;
        }
        try
        {
            btnLogin.IsEnabled = false;
            txtStatus.Text = "Signing in…";
            var result = await DesktopApiClient.BillingMemberLoginAsync(username, password);
            if (result == null) { txtStatus.Text = "Invalid username or password."; return; }
            var session = new BillingSession
            {
                MemberId = result.Id,
                MemberName = result.Name,
                FinanceIds = result.FinanceIds ?? new System.Collections.Generic.List<int>()
            };
            NavigationService?.Navigate(new BillingPage(session));
        }
        catch (Exception ex)
        {
            txtStatus.Text = ExtractMessage(ex);
        }
        finally { btnLogin.IsEnabled = true; }
    }

    private static string ExtractMessage(Exception ex)
    {
        var m = ex.Message;
        var i = m.IndexOf("message", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            var start = m.IndexOf(':', i);
            if (start >= 0)
            {
                var seg = m.Substring(start + 1).Trim().Trim('"', '}', ' ');
                if (seg.Length is > 0 and < 200) return seg;
            }
        }
        return "Invalid username or password.";
    }

    private void btnBack_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
    }
}
