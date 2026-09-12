using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CRMRSDesktopApp.Billing;

public partial class ViewAllDetailsWindow
{
    static ViewAllDetailsWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(Grid_PreviewKeyDown));
    }

    private static async void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not TextBox box)
            return;

        if (box.DataContext is not Row row)
            return;

        var grid = FindParent<DataGrid>(box);
        if (grid == null)
            return;

        var window = Window.GetWindow(grid) as ViewAllDetailsWindow;
        if (window == null)
            return;

        // Enter saves the remark immediately instead of waiting for LostFocus.
        // UpdateSourceTrigger=LostFocus in the existing XAML means the row model
        // may not have received the new text yet, so read directly from the TextBox.
        var text = (box.Text ?? "").Trim();
        if (text == (window._lastSavedRemark.TryGetValue(row.Id, out var prev) ? prev : row.BillingRemark))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        await window.SaveBillingRemarkAsync(row, text);
    }

    private async Task SaveBillingRemarkAsync(Row row, string text)
    {
        try
        {
            await DesktopApiClient.UpdateSubmissionFieldsAsync(row.Id, new { BillingRemark = text });
            _lastSavedRemark[row.Id] = text;
            row.BillingRemark = text;
            txtStatus.Text = "Billing remark saved.";
        }
        catch (Exception ex)
        {
            txtStatus.Text = "Save failed: " + ex.Message;
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
                return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
