using System.Windows;
using System.Windows.Controls;

namespace CRMRSDesktopApp.Billing;

public partial class BillingGatePage : Page
{
    public BillingGatePage()
    {
        InitializeComponent();
    }

    private void btnAllocations_Click(object sender, RoutedEventArgs e)
        => NavigationService?.Navigate(new AllocationsPage());

    private void btnBilling_Click(object sender, RoutedEventArgs e)
        => NavigationService?.Navigate(new BillingLoginPage());
}
