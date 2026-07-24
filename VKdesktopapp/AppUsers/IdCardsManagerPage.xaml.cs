using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.AppUsers;

/// <summary>Row shown in the ID-cards list. Holds the raw DTO fields plus the
/// three document images decoded into WPF ImageSources (bound directly to
/// Image.Source so they render inline, like the App Users KYC previews).</summary>
public sealed class IdCardVm
{
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? BloodGroup { get; set; }
    public string? Dob { get; set; }
    public string? ValidUntil { get; set; }
    public string? ValidFrom { get; set; }
    public bool Expired { get; set; }
    public string? DeclineReason { get; set; }
    public ImageSource? PhotoImage { get; set; }
    public ImageSource? PccImage { get; set; }
    public ImageSource? DraImage { get; set; }

    public bool   IsPending  => Status == "pending";
    public bool   IsApproved => Status == "approved";
    public string StatusLabel => Status switch
    {
        "approved" => Expired ? "EXPIRED" : "ACTIVE",
        "declined" => "DECLINED",
        _          => "PENDING"
    };
    // "22-07-2026 → 24-07-2026" or a dash when not approved yet.
    public string ValidRange =>
        string.IsNullOrEmpty(ValidUntil)
            ? "—"
            : $"{Fmt(ValidFrom)} → {Fmt(ValidUntil)}" + (Expired ? "  (expired)" : "");

    private static string Fmt(string? iso) =>
        System.DateTime.TryParse(iso, out var d) ? d.ToString("dd-MM-yyyy") : (iso ?? "—");
}

public partial class IdCardsManagerPage : Page
{
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

    private static async System.Threading.Tasks.Task<ImageSource?> LoadImageAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var bytes = await App.HttpClient.GetByteArrayAsync(url);
            if (bytes.Length == 0) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        txtLoading.Visibility = Visibility.Visible;
        txtEmpty.Visibility   = Visibility.Collapsed;
        listCards.ItemsSource  = null;
        try
        {
            var items = await DesktopApiClient.GetIdCardsAsync(CurrentFilter());
            var vms = new List<IdCardVm>();
            foreach (var it in items)
            {
                vms.Add(new IdCardVm
                {
                    UserId        = it.UserId,
                    Name          = it.Name,
                    Mobile        = it.Mobile,
                    Status        = it.Status,
                    BloodGroup    = it.BloodGroup,
                    Dob           = it.Dob,
                    ValidUntil    = it.ValidUntil,
                    ValidFrom     = it.ValidFrom,
                    Expired       = it.Expired,
                    DeclineReason = it.DeclineReason,
                    PhotoImage    = await LoadImageAsync(it.PhotoUrl),
                    PccImage      = await LoadImageAsync(it.PccUrl),
                    DraImage      = await LoadImageAsync(it.DraUrl),
                });
            }
            listCards.ItemsSource = vms;
            txtEmpty.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private readonly Dictionary<long, System.DateTime> _from = new();
    private readonly Dictionary<long, System.DateTime> _to = new();

    private void FromDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker dp && dp.Tag is long uid && dp.SelectedDate is System.DateTime d)
            _from[uid] = d;
    }

    private void ToDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DatePicker dp && dp.Tag is long uid && dp.SelectedDate is System.DateTime d)
            _to[uid] = d;
    }

    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long uid) return;

        var from = _from.TryGetValue(uid, out var f) ? f : System.DateTime.Today;
        var to   = _to.TryGetValue(uid, out var t) ? t : System.DateTime.Today.AddDays(1);
        if (to < from)
        {
            MessageBox.Show("‘Valid To’ cannot be before ‘Valid From’.", "Invalid dates",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"Approve this ID card, valid {from:dd-MM-yyyy} to {to:dd-MM-yyyy}?",
                "Approve ID Card", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await DesktopApiClient.ApproveIdCardAsync(uid, from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));
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

    /// <summary>Enlarge a document image in a modal preview window (no browser).</summary>
    private void ViewImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ImageSource src) return;
        var win = new Window
        {
            Title = "Document preview",
            Width = 720, Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = Brushes.Black
        };
        win.Content = new Image { Source = src, Stretch = Stretch.Uniform, Margin = new Thickness(8) };
        win.ShowDialog();
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
