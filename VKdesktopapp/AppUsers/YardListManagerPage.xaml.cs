using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;
using Microsoft.Win32;

namespace CRMRSDesktopApp.AppUsers;

public partial class YardListManagerPage : Page
{
    private const long MaxUploadBytes = 25L * 1024 * 1024;

    private byte[]? _fileBytes;
    private string? _fileName;

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

    public YardListManagerPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await LoadItemsAsync();

    private async System.Threading.Tasks.Task LoadItemsAsync()
    {
        try
        {
            var items = await DesktopApiClient.GetYardListAsync();
            listItems.ItemsSource = items;
            txtEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load the yard list:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Kind_Changed(object sender, RoutedEventArgs e)
    {
        if (pnlLink == null) return;
        var isLink = rbLink.IsChecked == true;
        pnlLink.Visibility   = isLink ? Visibility.Visible : Visibility.Collapsed;
        btnChoose.Visibility = isLink ? Visibility.Collapsed : Visibility.Visible;
        txtChosen.Visibility = isLink ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Choose a yard list file",
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
        var notes = txtNotes.Text?.Trim();

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
                await DesktopApiClient.AddYardListLinkAsync(title, url, notes);
                txtUrl.Text = "";
            }
            else
            {
                if (_fileBytes == null || _fileBytes.Length == 0)
                {
                    MessageBox.Show("Choose a file first.", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var name = _fileName ?? "yardlist";
                await DesktopApiClient.AddYardListFileAsync(
                    title, name, MimeFor(name), Convert.ToBase64String(_fileBytes), notes);
                _fileBytes = null; _fileName = null;
                txtChosen.Text = "No file chosen";
            }

            txtTitle.Text = ""; txtNotes.Text = "";
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
        if (MessageBox.Show("Delete this yard list entry?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await DesktopApiClient.DeleteYardListAsync(id);
            await LoadItemsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Delete failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
