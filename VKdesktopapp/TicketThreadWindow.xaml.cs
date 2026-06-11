using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public partial class TicketThreadWindow : Window
{
    private readonly int _ticketId;

    internal TicketThreadWindow(DesktopApiClient.TicketDto ticket)
    {
        InitializeComponent();
        _ticketId = ticket.Id;
        txtSubject.Text = ticket.Subject;
        txtCreated.Text = $"Reported {ticket.CreatedAt}";
        SetStatus(ticket.Status);
        RenderThread(ticket);

        Loaded += (_, __) =>
        {
            Activate();
            txtReply.Focus();
            System.Windows.Input.Keyboard.Focus(txtReply);
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && WindowState != WindowState.Maximized) DragMove();
    }
    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void SetStatus(string status)
    {
        txtStatus.Text = status switch
        {
            "resolved" => "RESOLVED", "in_progress" => "IN PROGRESS", _ => "OPEN"
        };
        statusChip.Background = new SolidColorBrush(status switch
        {
            "resolved"    => Color.FromRgb(0x2E, 0x7D, 0x32),
            "in_progress" => Color.FromRgb(0xF5, 0xA6, 0x23),
            _             => Color.FromRgb(0x64, 0x74, 0x8B)
        });
    }

    private void RenderThread(DesktopApiClient.TicketDto t)
    {
        threadPanel.Children.Clear();

        AddBubble("You", t.Message, t.CreatedAt, isAgency: true);

        if (!string.IsNullOrWhiteSpace(t.ScreenshotUrl))
        {
            try
            {
                var img = new Image
                {
                    Source = new BitmapImage(new Uri(t.ScreenshotUrl)),
                    Stretch = Stretch.Uniform, MaxHeight = 200,
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 10)
                };
                threadPanel.Children.Add(img);
            }
            catch { }
        }

        if (t.Messages != null)
            foreach (var m in t.Messages)
            {
                bool agency = m.Sender == "agency";
                AddBubble(agency ? "You" : "CRMRS Support", m.Body, m.CreatedAt, agency);
            }
    }

    private void AddBubble(string who, string body, string when, bool isAgency)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(isAgency ? Color.FromRgb(0xFF, 0xF3, 0xE0) : Color.FromRgb(0xEF, 0xF6, 0xFF)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 420,
            HorizontalAlignment = isAgency ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = who, FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(isAgency ? Color.FromRgb(0xC0, 0x5A, 0x00) : Color.FromRgb(0x15, 0x65, 0xC0))
        });
        sp.Children.Add(new TextBlock
        {
            Text = body, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), Margin = new Thickness(0, 2, 0, 0)
        });
        sp.Children.Add(new TextBlock
        {
            Text = when, FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Right
        });
        bubble.Child = sp;
        threadPanel.Children.Add(bubble);
        threadScroll.ScrollToEnd();
    }

    private async void btnSend_Click(object sender, RoutedEventArgs e)
    {
        var body = txtReply.Text.Trim();
        if (body.Length == 0) return;
        btnSend.IsEnabled = false;
        try
        {
            await DesktopApiClient.PostTicketMessageAsync(_ticketId, body);
            txtReply.Clear();
            var all = await DesktopApiClient.GetMyTicketsAsync();
            var fresh = all.Find(x => x.Id == _ticketId);
            if (fresh != null) { SetStatus(fresh.Status); RenderThread(fresh); }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not send: {ex.Message}", "Ticket",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { btnSend.IsEnabled = true; }
    }
}
