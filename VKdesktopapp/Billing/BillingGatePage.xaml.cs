using System;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Billing;

public partial class BillingGatePage : Page
{
    public BillingGatePage()
    {
        InitializeComponent();
    }

    private async void btnAllocations_Click(object sender, RoutedEventArgs e)
    {
        string stored;
        try { stored = await DesktopApiClient.GetAllocationPasswordAsync(); }
        catch { stored = ""; }

        if (!string.IsNullOrEmpty(stored))
        {
            var prompt = new PasswordPromptWindow { Owner = Window.GetWindow(this) };
            if (prompt.ShowDialog() != true) return;
            if (!string.Equals(prompt.EnteredPassword, stored, StringComparison.Ordinal))
            {
                MessageBox.Show("Incorrect allocation password.", "Allocations",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        NavigationService?.Navigate(new AllocationsPage());
    }

    private void btnBilling_Click(object sender, RoutedEventArgs e)
        => NavigationService?.Navigate(new BillingLoginPage());
}
