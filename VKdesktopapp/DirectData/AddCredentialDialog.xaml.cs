using System.Windows;

namespace CRMRSDesktopApp.DirectData;

public partial class AddCredentialDialog : Window
{
    public string Username { get; private set; } = "";
    public string Password { get; private set; } = "";

    public AddCredentialDialog() => InitializeComponent();

    private void btnCreate_Click(object sender, RoutedEventArgs e)
    {
        var u = txtUsername.Text.Trim();
        var p = txtPassword.Text.Trim();
        if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
        {
            lblError.Text       = "Both username and password are required.";
            lblError.Visibility = Visibility.Visible;
            return;
        }
        Username     = u;
        Password     = p;
        DialogResult = true;
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
