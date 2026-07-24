using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.AppUsers;

/// <summary>One ID-card row. Text binds immediately; the three document images
/// load in the background and notify when ready (so the list shows instantly).
/// The validity calendar is only shown when it's needed — pending / expired,
/// or when the admin clicks "Change validity" on an active card.</summary>
public sealed class IdCardVm : INotifyPropertyChanged
{
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? BloodGroup { get; set; }
    public string? Dob { get; set; }
    public string? ValidFrom { get; set; }
    public string? ValidUntil { get; set; }
    public bool Expired { get; set; }
    public string? DeclineReason { get; set; }

    public bool IsPending  => Status == "pending";
    public bool IsActive   => Status == "approved" && !Expired;
    public bool IsExpired  => Status == "approved" && Expired;

    public string StatusLabel => Status switch
    {
        "approved" => Expired ? "EXPIRED" : "ACTIVE",
        "declined" => "DECLINED",
        _          => "PENDING"
    };

    public Brush StatusBrush => Status switch
    {
        "approved" => Expired ? Brushes.Red : (Brush)new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
        "declined" => Brushes.Red,
        _          => (Brush)new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09))
    };

    public string ValidRange =>
        string.IsNullOrEmpty(ValidUntil)
            ? "—"
            : $"{Fmt(ValidFrom)} → {Fmt(ValidUntil)}";

    private static string Fmt(string? iso) =>
        System.DateTime.TryParse(iso, out var d) ? d.ToString("dd-MM-yyyy") : (iso ?? "—");

    // ── Calendar visibility ──────────────────────────────────────────────
    private bool _showPickers;
    public bool ShowPickers
    {
        get => _showPickers;
        set { _showPickers = value; Raise(nameof(ShowPickers)); Raise(nameof(ShowChangeButton)); }
    }
    // Active cards hide the calendar until the admin asks to change validity.
    public bool ShowChangeButton => IsActive && !ShowPickers;

    // ── Lazy images ──────────────────────────────────────────────────────
    private ImageSource? _photo, _pcc, _dra;
    public ImageSource? PhotoImage { get => _photo; set { _photo = value; Raise(nameof(PhotoImage)); } }
    public ImageSource? PccImage   { get => _pcc;   set { _pcc = value;   Raise(nameof(PccImage)); } }
    public ImageSource? DraImage   { get => _dra;   set { _dra = value;   Raise(nameof(DraImage)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
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
        return "all";
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
                var vm = new IdCardVm
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
                };
                // Pending / expired / declined -> show the calendar right away.
                // Active -> hide it (a "Change validity" button reveals it).
                vm.ShowPickers = !vm.IsActive;
                vms.Add(vm);
            }
            // Bind text immediately; images stream in afterwards.
            listCards.ItemsSource = vms;
            txtEmpty.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtLoading.Visibility = Visibility.Collapsed;

            foreach (var (vm, it) in Zip(vms, items))
            {
                _ = LoadImageAsync(it.PhotoUrl).ContinueWith(t => vm.PhotoImage = t.Result,
                    System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
                _ = LoadImageAsync(it.PccUrl).ContinueWith(t => vm.PccImage = t.Result,
                    System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
                _ = LoadImageAsync(it.DraUrl).ContinueWith(t => vm.DraImage = t.Result,
                    System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
        }
        catch (System.Exception ex)
        {
            txtLoading.Visibility = Visibility.Collapsed;
            MessageBox.Show($"Failed to load ID card requests:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static IEnumerable<(IdCardVm, DesktopApiClient.IdCardReviewDto)> Zip(
        List<IdCardVm> a, List<DesktopApiClient.IdCardReviewDto> b)
    {
        for (int i = 0; i < a.Count && i < b.Count; i++) yield return (a[i], b[i]);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) await LoadAsync();
    }

    private void ChangeValidity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is IdCardVm vm) vm.ShowPickers = true;
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
        if (MessageBox.Show($"Set this ID card valid {from:dd-MM-yyyy} to {to:dd-MM-yyyy}?",
                "ID Card Validity", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
        if (reason == null) return;
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
