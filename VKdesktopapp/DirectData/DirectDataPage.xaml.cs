using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VRASDesktopApp.DirectData;

public partial class DirectDataPage : Page
{
    private List<WebhookFileItem>  _allFiles  = new();
    private List<WebhookCredItem>  _allCreds  = new();
    private bool _showingFiles = true;

    public DirectDataPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadFilesAsync();
    }

    // ── View toggle ────────────────────────────────────────────────────────

    private async void btnShowFiles_Click(object sender, RoutedEventArgs e)
    {
        _showingFiles = true;
        panelFiles.Visibility  = Visibility.Visible;
        panelCreds.Visibility  = Visibility.Collapsed;
        btnAddCred.Visibility  = Visibility.Collapsed;
        btnShowFiles.Style     = (Style)FindResource("PrimaryButton");
        btnShowCreds.Style     = (Style)FindResource("SecondaryButton");
        lblTab.Text            = "  —  Files";
        await LoadFilesAsync();
    }

    private async void btnShowCreds_Click(object sender, RoutedEventArgs e)
    {
        _showingFiles = false;
        panelFiles.Visibility  = Visibility.Collapsed;
        panelCreds.Visibility  = Visibility.Visible;
        btnAddCred.Visibility  = Visibility.Visible;
        btnShowFiles.Style     = (Style)FindResource("SecondaryButton");
        btnShowCreds.Style     = (Style)FindResource("PrimaryButton");
        lblTab.Text            = "  —  Credentials";
        await LoadCredsAsync();
    }

    private async void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_showingFiles) await LoadFilesAsync();
        else               await LoadCredsAsync();
    }

    // ── Load files ─────────────────────────────────────────────────────────

    private async Task LoadFilesAsync()
    {
        lblStatus.Text = "Loading files…";
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}api/webhooks/files");
            req.Headers.Add("X-Api-Key", App.ApiKey);
            var resp = await App.HttpClient.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            _allFiles = await resp.Content.ReadFromJsonAsync<List<WebhookFileItem>>()
                        ?? new List<WebhookFileItem>();

            RebuildBankFilter();
            ApplyFilesFilter();
            lblStatus.Text = $"{_allFiles.Count:N0} file(s) received";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void RebuildBankFilter()
    {
        var banks = new List<string> { "All Banks" };
        banks.AddRange(_allFiles.Select(f => f.BankName).Distinct().OrderBy(b => b));
        var prev = cmbBank.SelectedItem?.ToString();
        cmbBank.ItemsSource   = banks;
        cmbBank.SelectedIndex = banks.IndexOf(prev ?? "All Banks") is >= 0 and var i ? i : 0;
    }

    private void ApplyFilesFilter()
    {
        var bank    = cmbBank.SelectedItem?.ToString();
        var search  = txtSearch.Text.Trim();
        var result  = _allFiles.AsEnumerable();
        if (!string.IsNullOrEmpty(bank) && bank != "All Banks")
            result = result.Where(f => f.BankName == bank);
        if (!string.IsNullOrEmpty(search))
            result = result.Where(f =>
                f.FileName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                f.UploadedBy.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                f.VehicleType.Contains(search, StringComparison.OrdinalIgnoreCase));
        dgFiles.ItemsSource = result.ToList();
    }

    // ── Load credentials ───────────────────────────────────────────────────

    private async Task LoadCredsAsync()
    {
        lblStatus.Text = "Loading credentials…";
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}api/webhooks/users");
            req.Headers.Add("X-Api-Key", App.ApiKey);
            var resp = await App.HttpClient.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            _allCreds     = await resp.Content.ReadFromJsonAsync<List<WebhookCredItem>>()
                            ?? new List<WebhookCredItem>();
            dgCreds.ItemsSource = _allCreds;
            lblStatus.Text = $"{_allCreds.Count:N0} credential(s)";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
    }

    // ── Filter events ──────────────────────────────────────────────────────

    private void cmbBank_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ApplyFilesFilter();

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilesFilter();

    // ── Download CSV ───────────────────────────────────────────────────────

    private async void btnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (!int.TryParse(btn.Tag?.ToString(), out var id)) return;

        var dlg = new SaveFileDialog
        {
            Title      = "Save Webhook CSV",
            Filter     = "CSV Files (*.csv)|*.csv",
            FileName   = $"webhook_{id}.csv",
            DefaultExt = "csv"
        };
        if (dlg.ShowDialog() != true) return;

        lblStatus.Text = $"Downloading file #{id}…";
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{App.ApiBaseUrl}api/webhooks/files/{id}/download");
            req.Headers.Add("X-Api-Key", App.ApiKey);
            var resp = await App.HttpClient.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            lblStatus.Text = $"Saved → {dlg.FileName}";
            MessageBox.Show($"Saved to:\n{dlg.FileName}", "Downloaded", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Download failed: {ex.Message}";
            MessageBox.Show($"Download failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Add credential ─────────────────────────────────────────────────────

    private async void btnAddCred_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddCredentialDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        lblStatus.Text = "Creating credential…";
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{App.ApiBaseUrl}api/webhooks/users");
            req.Headers.Add("X-Api-Key", App.ApiKey);
            req.Content = JsonContent.Create(new { username = dlg.Username, password = dlg.Password });
            var resp = await App.HttpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
                MessageBox.Show(err?.Message ?? "Failed to create user", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            lblStatus.Text = "Credential created";
            MessageBox.Show($"Credential created!\n\nUsername: {dlg.Username}\nPassword: {dlg.Password}\n\nShare these with the bank so they can send data.",
                "Created", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadCredsAsync();
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
    }

    // ── Delete credential ──────────────────────────────────────────────────

    private async void btnDeleteCred_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (!int.TryParse(btn.Tag?.ToString(), out var id)) return;

        if (MessageBox.Show("Delete this credential? The bank will no longer be able to send data.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        lblStatus.Text = "Deleting…";
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, $"{App.ApiBaseUrl}api/webhooks/users/{id}");
            req.Headers.Add("X-Api-Key", App.ApiKey);
            var resp = await App.HttpClient.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            lblStatus.Text = "Deleted";
            await LoadCredsAsync();
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
    }

    // ── DTOs ───────────────────────────────────────────────────────────────

    private record WebhookFileItem(
        int Id, string BankName, string FileName, string VehicleType,
        string UploadedBy, string UploadedDate, int TotalRecords,
        string ReceivedAt, string FileGuid);

    private record WebhookCredItem(int Id, string Username, string CreatedAt);
    private record ErrorResponse(string Message);
}
