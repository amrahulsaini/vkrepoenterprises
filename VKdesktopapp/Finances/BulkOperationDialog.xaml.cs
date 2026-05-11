using System.Windows;

namespace VRASDesktopApp.Finances;

public partial class BulkOperationDialog : Window
{
    private bool _backgrounded;

    public BulkOperationDialog(string statusText)
    {
        InitializeComponent();
        txtStatus.Text = statusText;
    }

    public void SignalSuccess(string completionMessage)
    {
        if (_backgrounded)
            MessageBox.Show(completionMessage, "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        else if (IsVisible)
            Close();
    }

    public void SignalError(string errorMessage)
    {
        if (_backgrounded)
        {
            MessageBox.Show($"Operation failed:\n{errorMessage}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            if (IsVisible) Close();
            MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void btnBackground_Click(object sender, RoutedEventArgs e)
    {
        _backgrounded = true;
        Close();
    }
}
