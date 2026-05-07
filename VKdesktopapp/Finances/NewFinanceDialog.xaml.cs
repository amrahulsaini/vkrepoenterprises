using System.Windows;
using System.Windows.Input;

namespace VRASDesktopApp.Finances;

public partial class NewFinanceDialog : Window
{
    public string FinanceName { get; private set; } = string.Empty;

    public NewFinanceDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => txtName.Focus();
    }

    private void btnSave_Click(object sender, RoutedEventArgs e) => TrySave();

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void txtName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TrySave();
        else if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }

    private void TrySave()
    {
        var name = txtName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            txtName.Focus();
            return;
        }
        FinanceName = name;
        DialogResult = true;
        Close();
    }
}
