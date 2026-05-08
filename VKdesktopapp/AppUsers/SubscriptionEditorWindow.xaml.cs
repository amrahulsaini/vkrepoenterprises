using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace VRASDesktopApp.AppUsers;

public partial class SubscriptionEditorWindow : Window
{
    public string  StartDate { get; private set; } = string.Empty;
    public string  EndDate   { get; private set; } = string.Empty;
    public decimal Amount    { get; private set; }
    public string? Notes     { get; private set; }

    public SubscriptionEditorWindow()
    {
        InitializeComponent();
        dpStart.SelectedDate = DateTime.Today;
        dpEnd.SelectedDate   = DateTime.Today.AddMonths(1);
    }

    private void dpStart_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (dpStart.SelectedDate.HasValue && dpEnd.SelectedDate.HasValue
            && dpEnd.SelectedDate < dpStart.SelectedDate)
        {
            dpEnd.SelectedDate = dpStart.SelectedDate.Value.AddMonths(1);
        }
    }

    private void NumericOnly(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, @"^[0-9.]$");
    }

    private void btnSave_Click(object sender, RoutedEventArgs e)
    {
        brdError.Visibility = Visibility.Collapsed;

        if (!dpStart.SelectedDate.HasValue)
        { ShowError("Please select a start date."); return; }
        if (!dpEnd.SelectedDate.HasValue)
        { ShowError("Please select an end date."); return; }
        if (dpEnd.SelectedDate < dpStart.SelectedDate)
        { ShowError("End date must be after start date."); return; }

        decimal amount = 0;
        if (!string.IsNullOrWhiteSpace(txtAmount.Text) &&
            !decimal.TryParse(txtAmount.Text, out amount))
        { ShowError("Enter a valid amount."); return; }

        StartDate = dpStart.SelectedDate.Value.ToString("yyyy-MM-dd");
        EndDate   = dpEnd.SelectedDate.Value.ToString("yyyy-MM-dd");
        Amount    = amount;
        Notes     = txtNotes.Text.Trim().Length > 0 ? txtNotes.Text.Trim() : null;

        DialogResult = true;
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string msg)
    {
        txtError.Text = msg;
        brdError.Visibility = Visibility.Visible;
    }
}
