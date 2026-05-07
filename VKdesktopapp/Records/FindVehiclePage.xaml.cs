using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VRASDesktopApp.Data;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Records;

public partial class FindVehiclePage : Page
{
    private CancellationTokenSource? _searchCts;
    private readonly DispatcherTimer _debounceTimer;
    private List<VehicleSearchItem> _fullResults = new();
    private readonly VehicleSearchRepository _searchRepo = new();

    public FindVehiclePage()
    {
        InitializeComponent();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) { }

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _ = SearchIfNeededAsync();
    }

    private bool IsChassisMode()
        => string.Equals((cmbMode.SelectedItem as ComboBoxItem)?.Tag?.ToString(), "chassis", StringComparison.OrdinalIgnoreCase);

    private async Task SearchIfNeededAsync()
    {
        try
        {
            var text    = txtSearch.Text ?? string.Empty;
            var chassis = IsChassisMode();

            if (chassis)
            {
                // Chassis: keep alphanumeric only, uppercase, max 5 chars
                var cleaned = Regex.Replace(text, "[^A-Za-z0-9]", "").ToUpper();
                if (txtSearch.Text != cleaned)
                {
                    var cur = txtSearch.SelectionStart;
                    txtSearch.Text = cleaned;
                    txtSearch.SelectionStart = Math.Min(cur, cleaned.Length);
                }
                txtSearch.MaxLength = 5;
                if (cleaned.Length == 5) await SearchAsync(cleaned);
            }
            else
            {
                // RC: digits only, max 4
                var digits = Regex.Replace(text, "[^0-9]", "");
                if (txtSearch.Text != digits)
                {
                    var cur = txtSearch.SelectionStart;
                    txtSearch.Text = digits;
                    txtSearch.SelectionStart = Math.Min(cur, digits.Length);
                }
                txtSearch.MaxLength = 4;
                if (digits.Length == 4) await SearchAsync(digits);
            }
        }
        catch { }
    }

    private void cmbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (txtSearch != null) txtSearch.Text = string.Empty;
        _debounceTimer.Stop();
    }

    private async void btnSearch_Click(object sender, RoutedEventArgs e)
    {
        var text    = txtSearch.Text?.Trim() ?? string.Empty;
        var chassis = IsChassisMode();
        var required = chassis ? 5 : 4;
        var cleaned  = chassis
            ? Regex.Replace(text, "[^A-Za-z0-9]", "").ToUpper()
            : Regex.Replace(text, "[^0-9]", "");

        if (cleaned.Length == required)
            await SearchAsync(cleaned);
        else
            MessageBox.Show($"Please enter exactly {required} {(chassis ? "characters" : "digits")} to search.",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task SearchAsync(string query)
    {
        try
        {
            btnSearch.IsEnabled     = false;
            prgSearching.Visibility = Visibility.Visible;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            var chassis = IsChassisMode();
            List<VehicleSearchItem> results;

            if (chassis)
                results = await _searchRepo.SearchByChassisLast5Async(query, ct);
            else
                results = await _searchRepo.SearchByRcLast4Async(query, ct);

            if (ct.IsCancellationRequested) return;

            _fullResults = results;

            lblResults.Text     = results.Count.ToString("N0");
            lblBranchCount.Text = results.Select(r => r.BranchName).Distinct().Count().ToString("N0");
            lblMode.Text        = chassis ? "CHASSIS" : "RC";
            lblSearchHint.Text  = results.Count == 0
                ? $"No records found for '{query}'."
                : $"Showing {results.Count} match(es) for '{query}'.";

            if (chassis)
            {
                colRc.Visibility      = Visibility.Collapsed;
                colChassis.Visibility = Visibility.Visible;
            }
            else
            {
                colRc.Visibility      = Visibility.Visible;
                colChassis.Visibility = Visibility.Collapsed;
            }

            var grouped = _fullResults
                .GroupBy(x => chassis ? x.ChassisNo : x.VehicleNo)
                .Select(g => g.First())
                .ToList();

            dgResults.ItemsSource = grouped;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show($"Search failed: {ex.Message}", "Search", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            prgSearching.Visibility = Visibility.Collapsed;
            btnSearch.IsEnabled     = true;
        }
    }

    private void dgResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgResults.SelectedItem is VehicleSearchItem vehicle)
        {
            var chassis = IsChassisMode();
            var key     = chassis ? vehicle.ChassisNo : vehicle.VehicleNo;
            var branches = _fullResults
                .Where(x => (chassis ? x.ChassisNo : x.VehicleNo) == key)
                .ToList();

            lstBranches.ItemsSource = branches;
            brdBranches.Visibility  = Visibility.Visible;
            brdDetails.Visibility   = Visibility.Collapsed;

            if (branches.Count > 0)
                lstBranches.SelectedIndex = 0;
        }
        else
        {
            brdBranches.Visibility = Visibility.Collapsed;
            brdDetails.Visibility  = Visibility.Collapsed;
        }
    }

    private void lstBranches_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstBranches.SelectedItem is VehicleSearchItem specificRecord)
        {
            brdDetails.Visibility  = Visibility.Visible;
            brdDetails.DataContext = specificRecord;
        }
        else
        {
            brdDetails.Visibility = Visibility.Collapsed;
        }
    }

    private void btnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (brdDetails.DataContext is VehicleSearchItem record)
        {
            var text =
$@"Finance : {record.Financer}
Branch : {record.BranchName}
Vehicle No : {record.VehicleNo}
Model : {record.Model}
Chasis No : {record.ChassisNo}
Engine No : {record.EngineNo}
Agency Name : V K Enterprises
Agency Contact : 0";
            Clipboard.SetText(text);
            MessageBox.Show("Details copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void btnConfirmNow_Click(object sender, RoutedEventArgs e)
    {
        if (brdDetails.DataContext is VehicleSearchItem record)
        {
            var window = new VRASDesktopApp.Confirmations.ManageConfirmationWindow(record);
            window.Owner = Window.GetWindow(this);
            window.ShowDialog();
        }
    }

    private async void btnRelease_Click(object sender, RoutedEventArgs e)
    {
        if (brdDetails.DataContext is VehicleSearchItem record)
        {
            var currentlyReleased = record.ReleaseStatus?.ToUpperInvariant() == "YES";
            var actionName = currentlyReleased ? "un-release" : "release";
            var confirm = MessageBox.Show($"Are you sure you want to {actionName} {record.VehicleNo}?",
                "Confirm Release", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    var url = $"{App.ApiBaseUrl}api/Records/MarkReleased/{record.Id}";
                    var response = await App.HttpClient.PostAsync(url, null);
                    response.EnsureSuccessStatusCode();
                    var result     = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    var newStatus  = result.GetProperty("status").GetString();
                    record.ReleaseStatus = newStatus ?? (currentlyReleased ? "NO" : "YES");
                    MessageBox.Show($"Record {actionName}d successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    brdDetails.DataContext = null;
                    brdDetails.DataContext = record;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to toggle release status: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async void btnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (brdDetails.DataContext is VehicleSearchItem record)
        {
            var confirm = MessageBox.Show(
                $"Are you sure you want to absolutely delete {record.VehicleNo}?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    var url = $"{App.ApiBaseUrl}api/Records/Delete/{record.Id}";
                    var response = await App.HttpClient.DeleteAsync(url);
                    response.EnsureSuccessStatusCode();
                    MessageBox.Show("Record deleted successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    _fullResults.Remove(record);
                    var chassis  = IsChassisMode();
                    var key      = chassis ? record.ChassisNo : record.VehicleNo;
                    var remaining = _fullResults.Where(x => (chassis ? x.ChassisNo : x.VehicleNo) == key).ToList();
                    if (remaining.Any())
                    {
                        lstBranches.ItemsSource = remaining;
                        lstBranches.SelectedIndex = 0;
                    }
                    else
                    {
                        dgResults.ItemsSource  = null;
                        brdBranches.Visibility = Visibility.Collapsed;
                        brdDetails.Visibility  = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
