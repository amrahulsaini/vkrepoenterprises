using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;
using Microsoft.Win32;

namespace CRMRSDesktopApp.AppUsers;

public partial class RepoKitsManagerPage : Page
{
    private byte[]? _pdfBytes;
    private string? _pdfName;

    public RepoKitsManagerPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var finances = await DesktopApiClient.GetFinancesAsync();
            cmbFinance.ItemsSource = finances;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load head offices:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await LoadKitsAsync();
    }

    private async System.Threading.Tasks.Task LoadKitsAsync()
    {
        try
        {
            var kits = await DesktopApiClient.GetRepoKitsAsync();
            listKits.ItemsSource = kits;
            txtEmpty.Visibility = kits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load repo kits:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ChoosePdf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Choose a repo kit PDF" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _pdfBytes = File.ReadAllBytes(dlg.FileName);
            _pdfName  = Path.GetFileName(dlg.FileName);
            txtChosen.Text = $"Selected: {_pdfName} ({_pdfBytes.Length / 1024} KB)";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read the file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (cmbFinance.SelectedValue is not int financeId || financeId <= 0)
        {
            MessageBox.Show("Please select a head office.", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_pdfBytes == null || _pdfBytes.Length == 0)
        {
            MessageBox.Show("Please choose a PDF file.", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        btnUpload.IsEnabled = false;
        try
        {
            var b64 = Convert.ToBase64String(_pdfBytes);
            await DesktopApiClient.UploadRepoKitAsync(financeId, txtTitle.Text?.Trim(), _pdfName ?? "kit.pdf", b64);
            _pdfBytes = null; _pdfName = null;
            txtChosen.Text = "No file chosen";
            txtTitle.Text = "";
            await LoadKitsAsync();
            MessageBox.Show("Repo kit uploaded.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Upload failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { btnUpload.IsEnabled = true; }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string url && !string.IsNullOrWhiteSpace(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not long id) return;
        if (MessageBox.Show("Delete this repo kit?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await DesktopApiClient.DeleteRepoKitAsync(id);
            await LoadKitsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Delete failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
