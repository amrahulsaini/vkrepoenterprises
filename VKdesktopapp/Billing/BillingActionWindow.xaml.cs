using System;
using System.Windows;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Billing;

public partial class BillingActionWindow : Window
{
    private readonly long _submissionId;

    public string? SavedAction { get; private set; }

    public BillingActionWindow(long submissionId, string currentAction, string what)
    {
        InitializeComponent();
        _submissionId = submissionId;
        lblWhich.Text = what;

        switch ((currentAction ?? "").ToLowerInvariant())
        {
            case "hold":   rbHold.IsChecked = true; break;
            case "cancel": rbCancel.IsChecked = true; break;
            default:       rbImmediate.IsChecked = true; break;
        }
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void btnSave_Click(object sender, RoutedEventArgs e)
    {
        var action = rbHold.IsChecked == true ? "hold"
                   : rbCancel.IsChecked == true ? "cancel"
                   : "immediate";
        try
        {
            btnSave.IsEnabled = false;
            txtErr.Foreground = System.Windows.Media.Brushes.Gray;
            txtErr.Text = "Saving…";

            await DesktopApiClient.UpdateBillingActionAsync(_submissionId, action);

            SavedAction = action;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            txtErr.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtErr.Text = "Could not save: " + ex.Message;
            btnSave.IsEnabled = true;
        }
    }
}
