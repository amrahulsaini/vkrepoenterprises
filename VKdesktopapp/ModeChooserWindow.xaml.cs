using System;
using System.Windows;
using System.Windows.Input;
using CRMRSDesktopApp.Billing;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public partial class ModeChooserWindow : Window
{
    public bool LoggedOut { get; private set; }

    public ModeChooserWindow()
    {
        InitializeComponent();
        var name = App.SignedAppUser?.AgencyName;
        if (!string.IsNullOrWhiteSpace(name)) lblWho.Text = "Welcome, " + name;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private async void btnSuperAdmin_Click(object sender, RoutedEventArgs e)
    {
        string configured;
        try { configured = await DesktopApiClient.GetSuperAdminPasswordAsync(); }
        catch { configured = ""; }

        if (!await PassesGate("Super Admin", configured)) return;

        Hide();
        try
        {
            var w = new MainWindow();
            w.ShowDialog();
        }
        finally { Show(); }
    }

    private async void btnBilling_Click(object sender, RoutedEventArgs e)
    {
        string configured;
        try { configured = await DesktopApiClient.GetBillingDesktopPasswordAsync(); }
        catch { configured = ""; }

        if (!await PassesGate("Billing", configured)) return;

        Hide();
        try
        {
            var w = new BillingShellWindow();
            w.ShowDialog();
            if (w.LoggedOut) { LoggedOut = true; Close(); return; }
        }
        finally { if (!LoggedOut) Show(); }
    }

    private System.Threading.Tasks.Task<bool> PassesGate(string title, string configured)
    {
        var required = string.IsNullOrEmpty(configured) ? App.LoginPassword : configured;

        if (string.IsNullOrEmpty(required))
            return System.Threading.Tasks.Task.FromResult(true);

        var prompt = new PasswordPromptWindow(title) { Owner = this };
        if (prompt.ShowDialog() != true)
            return System.Threading.Tasks.Task.FromResult(false);

        if (!string.Equals(prompt.EnteredPassword, required, StringComparison.Ordinal))
        {
            MessageBox.Show("Wrong password.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return System.Threading.Tasks.Task.FromResult(false);
        }
        return System.Threading.Tasks.Task.FromResult(true);
    }

    private void btnLogout_Click(object sender, RoutedEventArgs e)
    {
        LoggedOut = true;
        Close();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
