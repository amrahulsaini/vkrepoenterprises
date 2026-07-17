using System.Windows;

namespace CRMRSDesktopApp.Billing;

public partial class PasswordPromptWindow : Window
{
    public string EnteredPassword { get; private set; } = "";

    public PasswordPromptWindow()
    {
        InitializeComponent();
        Loaded += (_, __) => pwd.Focus();
    }

    public PasswordPromptWindow(string title) : this()
    {
        Title = title;
        lblTitle.Text = "Enter " + title + " Password";
        lblHint.Text = "This is protected. Enter the password to continue.";
    }

    private void btnOk_Click(object sender, RoutedEventArgs e)
    {
        EnteredPassword = pwd.Password;
        DialogResult = true;
        Close();
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
