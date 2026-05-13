using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VRASDesktopApp.Data;

namespace VRASDesktopApp.Records;

public partial class UserPickerWindow : Window
{
    public long?  SelectedUserId   { get; private set; }
    public string SelectedUserName { get; private set; } = "";

    private List<DesktopApiClient.PickerUserDto> _allUsers = new();

    public UserPickerWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _allUsers = await DesktopApiClient.GetPickerUsersAsync();
            RenderUsers(_allUsers);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load users: {ex.Message}");
            Close();
        }
    }

    private void RenderUsers(List<DesktopApiClient.PickerUserDto> users)
    {
        spUsers.Children.Clear();
        if (users.Count == 0)
        {
            spUsers.Children.Add(new TextBlock
            {
                Text = "No users found.",
                Foreground = (Brush)FindResource("Gray500"),
                Margin = new Thickness(0, 20, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var u in users)
            spUsers.Children.Add(BuildUserRow(u));
    }

    private Border BuildUserRow(DesktopApiClient.PickerUserDto u)
    {
        var avatar = BuildInitialsAvatar(u.Name);

        var nameBlock = new TextBlock
        {
            Text       = u.Name,
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Gray900")
        };
        var mobileBlock = new TextBlock
        {
            Text       = u.Mobile,
            FontSize   = 11,
            Foreground = (Brush)FindResource("Gray500"),
            Margin     = new Thickness(0, 2, 0, 0)
        };

        var textStack = new StackPanel();
        textStack.Children.Add(nameBlock);
        textStack.Children.Add(mobileBlock);

        if (!string.IsNullOrWhiteSpace(u.Address))
            textStack.Children.Add(new TextBlock
            {
                Text         = u.Address,
                FontSize     = 10,
                Foreground   = (Brush)FindResource("Gray400"),
                Margin       = new Thickness(0, 1, 0, 0),
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
            });

        var badge = new Border
        {
            Background        = u.IsActive ? (Brush)FindResource("Green500") : (Brush)FindResource("Gray300"),
            CornerRadius      = new CornerRadius(10),
            Padding           = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text       = u.IsActive ? "Active" : "Inactive",
            FontSize   = 10,
            Foreground = u.IsActive ? Brushes.White : (Brush)FindResource("Gray600")
        };

        var inner = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(avatar, Dock.Left);
        DockPanel.SetDock(badge, Dock.Right);
        inner.Children.Add(avatar);
        inner.Children.Add(badge);
        inner.Children.Add(textStack);

        var row = new Border
        {
            Background   = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding      = new Thickness(12, 10, 12, 10),
            Margin       = new Thickness(0, 0, 0, 6),
            Cursor       = Cursors.Hand,
            Child        = inner
        };

        row.MouseEnter       += (_, _) => row.Background = (Brush)FindResource("Primary50");
        row.MouseLeave       += (_, _) => row.Background = Brushes.White;
        row.MouseLeftButtonUp += (_, _) =>
        {
            SelectedUserId   = u.Id;
            SelectedUserName = u.Name;
            DialogResult     = true;
            Close();
        };

        return row;
    }

    private static Border BuildInitialsAvatar(string name)
    {
        const double size = 40;
        var initial = name.Length > 0 ? name[0].ToString().ToUpper() : "?";
        var circle = new Border
        {
            Width        = size, Height       = size,
            CornerRadius = new CornerRadius(size / 2),
            Background   = InitialColor(name),
            Margin       = new Thickness(0, 0, 12, 0)
        };
        circle.Child = new TextBlock
        {
            Text                = initial,
            FontSize            = 16,
            FontWeight          = FontWeights.Bold,
            Foreground          = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        return circle;
    }

    private static readonly Brush[] AvatarColors =
    {
        new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
        new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),
        new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47)),
        new SolidColorBrush(Color.FromRgb(0xFB, 0x8C, 0x00)),
        new SolidColorBrush(Color.FromRgb(0x8E, 0x24, 0xAA)),
        new SolidColorBrush(Color.FromRgb(0x00, 0x89, 0x7B)),
    };

    private static Brush InitialColor(string name)
        => AvatarColors[Math.Abs(name.GetHashCode()) % AvatarColors.Length];

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = txtSearch.Text.Trim();
        btnClearSearch.Visibility = q.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var filtered = string.IsNullOrWhiteSpace(q)
            ? _allUsers
            : _allUsers.Where(u =>
                u.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                u.Mobile.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        RenderUsers(filtered);
    }

    private void btnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        txtSearch.Clear();
        txtSearch.Focus();
    }

    private void btnAllUsers_Click(object sender, RoutedEventArgs e)
    {
        SelectedUserId   = null;
        SelectedUserName = "All Users";
        DialogResult     = true;
        Close();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
