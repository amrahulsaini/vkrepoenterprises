using System.Windows;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Finances;

public partial class BranchEditorWindow : Window
{
    private readonly int _financeId;
    private readonly BranchRepository _branchRepo = new();

    public BranchEditorWindow(int financeId, string financeName)
    {
        InitializeComponent();
        _financeId = financeId;
        txtFinanceName.Text = financeName;
    }

    private async void btnSave_Click(object sender, RoutedEventArgs e)
    {
        var name = txtBranchName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Branch name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        btnSave.IsEnabled = false;
        try
        {
            var contact1 = txtContact1.Text?.Trim();
            var contact2 = txtContact2.Text?.Trim();
            var contact3 = txtContact3.Text?.Trim();
            var address = txtAddress.Text?.Trim();
            var branchCode = txtBranchCode?.Text?.Trim();
            await _branchRepo.CreateBranchAsync(_financeId, name, contact1, contact2, contact3, address, branchCode, null, null, null, null);
            DialogResult = true;
            Close();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Failed to save branch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnSave.IsEnabled = true;
        }
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
