using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.AppUsers;

public partial class IdCardsManagerPage : Page
{
    private readonly Dictionary<long, int> _days = new();

    public IdCardsManagerPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();

    private string CurrentFilter()
    {
        if (cmbFilter.SelectedItem is ComboBoxItem it && it.Tag is string s) return s;
        return "pending";
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        txtLoading.Visibility = Visibility.Visible;
        txtEmpty.Visibility   = Visibility.Collapsed;
        listCards.ItemsSource  = null;
        try
        {
            var items = await DesktopApiClient.GetIdCardsAsync(CurrentFilter());
            listCards.ItemsSource = items;
            txtEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Failed to load ID card requests:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { txtLoading.Visibility = Visibility.Collapsed; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) await LoadAsync();
    }

    private void Days_Changed(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is long uid && int.TryParse(tb.Text, out var d))
            _days[uid] = d;
    }

    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long uid) return;
        int days = _days.TryGetValue(uid, out var d) && d > 0 ? d : 1;
        if (MessageBox.Show($"Approve this ID card for {days} day(s)?", "Approve ID Card",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await DesktopApiClient.ApproveIdCardAsync(uid, days);
            await LoadAsync();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Approve failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Decline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long uid) return;
        var reason = PromptReason();
        if (reason == null) return;   // cancelled
        try
        {
            await DesktopApiClient.DeclineIdCardAsync(uid, reason);
            await LoadAsync();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Decline failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ViewImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string url && !string.IsNullOrWhiteSpace(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Minimal modal text prompt. Returns null if cancelled.</summary>
    private string? PromptReason()
    {
        var win = new Window
        {
            Title = "Decline reason",
            Width = 420, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Why is this ID card being declined? The agent will see this and must re-upload.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });
        var box = new TextBox { Height = 60, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(box);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        string? result = null;
        var ok = new Button { Content = "Decline", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 6, 14, 6), IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                MessageBox.Show("Please enter a reason.", "Reason required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            result = box.Text.Trim();
            win.DialogResult = true;
        };
        cancel.Click += (_, _) => win.DialogResult = false;
        btns.Children.Add(ok);
        btns.Children.Add(cancel);
        panel.Children.Add(btns);
        win.Content = panel;
        return win.ShowDialog() == true ? result : null;
    }
}
