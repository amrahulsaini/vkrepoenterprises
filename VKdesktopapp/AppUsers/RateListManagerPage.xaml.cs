using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CRMRSDesktopApp.Data;
using Microsoft.Win32;

namespace CRMRSDesktopApp.AppUsers;

public partial class RateListManagerPage : Page
{
    private const long MaxUploadBytes = 25L * 1024 * 1024;

    private byte[]? _fileBytes;
    private string? _fileName;

    private class UserPick : INotifyPropertyChanged
    {
        public long   Id     { get; set; }
        public string Name   { get; set; } = "";
        public string Mobile { get; set; } = "";
        public string Display => string.IsNullOrWhiteSpace(Mobile) ? Name : $"{Name}  ·  {Mobile}";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private List<UserPick> _userPicks = new();

    private long _editingListId;

    private static readonly (string Ext, string Mime)[] KnownTypes =
    {
        (".pdf",  "application/pdf"),
        (".jpg",  "image/jpeg"),
        (".jpeg", "image/jpeg"),
        (".png",  "image/png"),
        (".webp", "image/webp"),
        (".gif",  "image/gif"),
        (".doc",  "application/msword"),
        (".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        (".xls",  "application/vnd.ms-excel"),
        (".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        (".csv",  "text/csv"),
        (".ppt",  "application/vnd.ms-powerpoint"),
        (".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        (".txt",  "text/plain"),
        (".zip",  "application/zip"),
    };

    public RateListManagerPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try { cmbFinance.ItemsSource = await DesktopApiClient.GetFinancesAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load head offices:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        try
        {
            var users = await DesktopApiClient.GetAllSimpleUsersAsync();
            _userPicks = users
                .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
                .Select(u => new UserPick { Id = u.Id, Name = u.Name, Mobile = u.Mobile })
                .ToList();
            ShowUserPicks();
            UpdateUsersButton();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load app users:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await LoadItemsAsync();
    }

    private void ShowUserPicks()
    {
        var term = (txtUserSearch.Text ?? "").Trim();
        lstUserPicks.ItemsSource = term.Length == 0
            ? _userPicks
            : _userPicks.Where(u =>
                  u.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                  u.Mobile.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
    }

    private void UpdateUsersButton()
    {
        var picked = _userPicks.Where(u => u.IsChecked).ToList();
        btnUsers.Content = picked.Count switch
        {
            0 => "No users selected",
            1 => picked[0].Name,
            _ when picked.Count == _userPicks.Count && _userPicks.Count > 0 => $"All {picked.Count} users",
            _ => $"{picked.Count} users selected",
        };
    }

    private List<long> PickedUserIds() =>
        _userPicks.Where(u => u.IsChecked).Select(u => u.Id).ToList();

    private void UserSearch_Changed(object sender, TextChangedEventArgs e) => ShowUserPicks();

    private void UserPick_Changed(object sender, RoutedEventArgs e) => UpdateUsersButton();

    private void UserPicker_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Escape) return;
        e.Handled = true;
        if (e.Key == Key.Enter) CommitUserPicks();
        else btnUsers.IsChecked = false;
    }

    private void btnSelectAllUsers_Click(object sender, RoutedEventArgs e)
    {
        foreach (var u in _userPicks) u.IsChecked = true;
        UpdateUsersButton();
    }

    private void btnClearUsers_Click(object sender, RoutedEventArgs e)
    {
        foreach (var u in _userPicks) u.IsChecked = false;
        UpdateUsersButton();
    }

    private void btnDoneUsers_Click(object sender, RoutedEventArgs e) => CommitUserPicks();

    private async void CommitUserPicks()
    {
        UpdateUsersButton();
        btnUsers.IsChecked = false;
        if (_editingListId == 0) return;

        var id = _editingListId;
        _editingListId = 0;
        try
        {
            await DesktopApiClient.SetRateListUsersAsync(id, PickedUserIds());
            foreach (var u in _userPicks) u.IsChecked = false;
            txtUserSearch.Text = "";
            UpdateUsersButton();
            await LoadItemsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the audience:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditUsers_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long id }) return;
        var item = (listItems.ItemsSource as List<DesktopApiClient.RateListItemDto>)
            ?.FirstOrDefault(x => x.Id == id);
        if (item == null) return;

        var already = new HashSet<long>(item.UserIds);
        foreach (var u in _userPicks) u.IsChecked = already.Contains(u.Id);
        txtUserSearch.Text = "";
        ShowUserPicks();
        UpdateUsersButton();
        _editingListId = id;
        btnUsers.IsChecked = true;
    }

    private async System.Threading.Tasks.Task LoadItemsAsync()
    {
        try
        {
            var items = await DesktopApiClient.GetRateListAsync();
            listItems.ItemsSource = items;
            txtEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load the rate list:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Kind_Changed(object sender, RoutedEventArgs e)
    {
        if (pnlLink == null) return;
        var isLink = rbLink.IsChecked == true;
        pnlLink.Visibility  = isLink ? Visibility.Visible : Visibility.Collapsed;
        btnChoose.Visibility = isLink ? Visibility.Collapsed : Visibility.Visible;
        txtChosen.Visibility = isLink ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Choose a rate list file",
            Filter = "Documents and images|*.pdf;*.jpg;*.jpeg;*.png;*.webp;*.gif;*.doc;*.docx;*.xls;*.xlsx;*.csv;*.ppt;*.pptx;*.txt;*.zip|All files|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var info = new FileInfo(dlg.FileName);
            if (info.Length > MaxUploadBytes)
            {
                MessageBox.Show($"That file is {info.Length / 1024 / 1024} MB. The limit is 25 MB — link to it instead.",
                    "Too big", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _fileBytes = File.ReadAllBytes(dlg.FileName);
            _fileName  = Path.GetFileName(dlg.FileName);
            txtChosen.Text = $"Selected: {_fileName} ({_fileBytes.Length / 1024:N0} KB)";
            if (string.IsNullOrWhiteSpace(txtTitle.Text))
                txtTitle.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read the file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string MimeFor(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        foreach (var (e, m) in KnownTypes) if (e == ext) return m;
        return "application/octet-stream";
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        var title = txtTitle.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show("Give it a title.", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var financeId = cmbFinance.SelectedValue as int?;
        var notes     = txtNotes.Text?.Trim();
        var audience  = PickedUserIds();
        if (audience.Count == 0 &&
            MessageBox.Show(
                "No app users are selected, so nobody will see this entry in the app.\n\nPublish anyway?",
                "No audience", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        btnPublish.IsEnabled = false;
        try
        {
            if (rbLink.IsChecked == true)
            {
                var url = txtUrl.Text?.Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    MessageBox.Show("Paste the link.", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    url = "https://" + url;
                await DesktopApiClient.AddRateListLinkAsync(title, url, financeId, notes, audience);
                txtUrl.Text = "";
            }
            else
            {
                if (_fileBytes == null || _fileBytes.Length == 0)
                {
                    MessageBox.Show("Choose a file first.", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var name = _fileName ?? "ratelist";
                await DesktopApiClient.AddRateListFileAsync(
                    title, name, MimeFor(name), Convert.ToBase64String(_fileBytes),
                    financeId, notes, audience);
                _fileBytes = null; _fileName = null;
                txtChosen.Text = "No file chosen";
            }

            txtTitle.Text = ""; txtNotes.Text = "";
            cmbFinance.SelectedIndex = -1;
            foreach (var u in _userPicks) u.IsChecked = false;
            UpdateUsersButton();
            await LoadItemsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Publish failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { btnPublish.IsEnabled = true; }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long id }) return;
        if (MessageBox.Show("Delete this rate list entry?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await DesktopApiClient.DeleteRateListAsync(id);
            await LoadItemsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Delete failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
