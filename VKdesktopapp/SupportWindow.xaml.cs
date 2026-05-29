using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VRASDesktopApp.Data;

namespace VRASDesktopApp;

public partial class SupportWindow : Window
{
    private string? _shotBase64;   // attached screenshot, base64 (no data: prefix)

    public SupportWindow()
    {
        InitializeComponent();
        Loaded += async (_, __) => await LoadTicketsAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && WindowState != WindowState.Maximized) DragMove();
    }
    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── Attach screenshot ───────────────────────────────────────────────────
    private void btnAttach_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Choose a screenshot",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes.Length > 8 * 1024 * 1024)
            {
                MessageBox.Show("Please choose an image under 8 MB.", "Screenshot",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _shotBase64 = Convert.ToBase64String(bytes);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit(); bmp.Freeze();
            imgShot.Source = bmp;
            shotPreviewBox.Visibility = Visibility.Visible;
            btnRemoveShot.Visibility  = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load image: {ex.Message}", "Screenshot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void btnRemoveShot_Click(object sender, RoutedEventArgs e)
    {
        _shotBase64 = null;
        imgShot.Source = null;
        shotPreviewBox.Visibility = Visibility.Collapsed;
        btnRemoveShot.Visibility  = Visibility.Collapsed;
    }

    // ── Submit ──────────────────────────────────────────────────────────────
    private async void btnSubmit_Click(object sender, RoutedEventArgs e)
    {
        var subject = txtSubject.Text.Trim();
        var message = txtMessage.Text.Trim();
        if (subject.Length < 2 || message.Length < 2)
        {
            MessageBox.Show("Please enter a subject and a description.", "Report an Issue",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        btnSubmit.IsEnabled = false;
        try
        {
            await DesktopApiClient.CreateTicketAsync(subject, message, _shotBase64);
            txtSubject.Clear(); txtMessage.Clear();
            btnRemoveShot_Click(sender, e);
            await LoadTicketsAsync();
            MessageBox.Show("Ticket submitted. Our team will reply here.", "Thank you",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not submit: {ex.Message}", "Report an Issue",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { btnSubmit.IsEnabled = true; }
    }

    // ── Load my tickets ─────────────────────────────────────────────────────
    private async void btnRefresh_Click(object sender, RoutedEventArgs e) => await LoadTicketsAsync();

    private async System.Threading.Tasks.Task LoadTicketsAsync()
    {
        try
        {
            var tickets = await DesktopApiClient.GetMyTicketsAsync();
            var vms = new List<TicketVm>();
            foreach (var t in tickets) vms.Add(new TicketVm(t));
            icTickets.ItemsSource = vms;
            txtEmpty.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load tickets: {ex.Message}", "Support",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Card click → open the full conversation thread for that ticket.
    private void TicketCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is TicketVm vm)
        {
            var w = new TicketThreadWindow(vm.Dto) { Owner = this };
            w.ShowDialog();
            _ = LoadTicketsAsync();   // refresh previews/status after viewing
        }
    }

    // View-model with display helpers for the ticket list template.
    // internal — wraps the internal TicketDto. WPF binding still works on
    // internal types within the same assembly.
    internal class TicketVm
    {
        private readonly DesktopApiClient.TicketDto _t;
        public TicketVm(DesktopApiClient.TicketDto t) { _t = t; }
        public DesktopApiClient.TicketDto Dto => _t;

        public string Subject    => _t.Subject;
        public string CreatedAt  => _t.CreatedAt;

        // Preview = the latest message in the thread, else the opening message.
        public string Preview
        {
            get
            {
                if (_t.Messages != null && _t.Messages.Count > 0)
                {
                    var last = _t.Messages[^1];
                    var who  = last.Sender == "admin" ? "CRMRS: " : "You: ";
                    return who + last.Body;
                }
                return _t.Message;
            }
        }

        public string MsgCountText
        {
            get
            {
                int n = _t.Messages?.Count ?? 0;
                return n == 0 ? "No replies yet" : (n == 1 ? "1 reply" : $"{n} replies");
            }
        }

        public string StatusText => _t.Status switch
        {
            "resolved"    => "RESOLVED",
            "in_progress" => "IN PROGRESS",
            _             => "OPEN"
        };
        public Brush StatusBrush => _t.Status switch
        {
            "resolved"    => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
            "in_progress" => new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23)),
            _             => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))
        };
    }
}
