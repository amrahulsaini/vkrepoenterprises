using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using VRASDesktopApp.Models;

namespace VRASDesktopApp;

public partial class AcceptPaymentsPage : Page
{
    public AcceptPaymentsPage()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadPaymentMethodsAsync();
    }

    private async Task LoadPaymentMethodsAsync()
    {
        try
        {
            var methods = await App.HttpClient.GetFromJsonAsync<List<PaymentMethods>>(
                $"{App.ApiBaseUrl}api/PaymentMethods");
            cmbPaymentMethod.ItemsSource = methods;
            cmbPaymentMethod.DisplayMemberPath = "MethodName";
            cmbPaymentMethod.SelectedValuePath = "PaymentMethodId";
        }
        catch
        {
            // Silently fail
        }
    }
}
