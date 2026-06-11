using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CRMRSDesktopApp.Data;
using CRMRSDesktopApp.Models;

namespace CRMRSDesktopApp.Records;

public partial class FindVehiclePage : Page
{
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _detailCts;
    private readonly DispatcherTimer _debounceTimer;
    private List<VehicleSearchItem> _fullResults = new();
    private readonly VehicleSearchRepository _searchRepo = new();
    private readonly Dictionary<string, VehicleSearchItem> _fullRecordCache = new();

    public FindVehiclePage()
    {
        InitializeComponent();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => {
            txtSearch.Focus();
            Keyboard.Focus(txtSearch);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

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
            btnSearch.IsEnabled       = false;
            prgSearching.Visibility   = Visibility.Visible;

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
            txtSearch.Text = string.Empty;


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

            RebindResultsGrid();
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

    private void RebindResultsGrid()
    {
        var chassis = IsChassisMode();
        var grouped = _fullResults
            .GroupBy(x => chassis ? x.ChassisNo : x.VehicleNo)
            .Select(g => g.First())
            .OrderBy(x => chassis ? x.ChassisNo : x.VehicleNo,
                     StringComparer.OrdinalIgnoreCase)
            .ToList();
        dgResults.ItemsSource = grouped;
        lblResults.Text = grouped.Count.ToString("N0");
        lblBranchCount.Text = "0";
    }

    private void dgResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgResults.SelectedItem is VehicleSearchItem vehicle)
        {
            var chassis = IsChassisMode();
            var key     = chassis ? vehicle.ChassisNo : vehicle.VehicleNo;
            var branches = _fullResults
                .Where(x => (chassis ? x.ChassisNo : x.VehicleNo) == key)
                .OrderBy(x => x.Financer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.BranchName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lstBranches.ItemsSource = branches;
            lblBranchCount.Text     = branches.Count.ToString("N0");
            brdBranches.Visibility  = Visibility.Visible;
            brdDetails.Visibility   = Visibility.Collapsed;

            if (branches.Count > 0)
                lstBranches.SelectedIndex = 0;
        }
        else
        {
            lblBranchCount.Text    = "0";
            brdBranches.Visibility = Visibility.Collapsed;
            brdDetails.Visibility  = Visibility.Collapsed;
        }
    }

    private async void lstBranches_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstBranches.SelectedItem is not VehicleSearchItem skinny)
        {
            brdDetails.Visibility = Visibility.Collapsed;
            return;
        }

        brdDetails.Visibility  = Visibility.Visible;
        brdDetails.DataContext = skinny;

        if (_fullRecordCache.TryGetValue(skinny.Id, out var cached))
        {
            brdDetails.DataContext = cached;
            return;
        }

        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _detailCts = new CancellationTokenSource();
        var ct = _detailCts.Token;
        try
        {
            var full = await _searchRepo.GetRecordByIdAsync(long.Parse(skinny.Id), ct);
            if (ct.IsCancellationRequested || full == null) return;
            _fullRecordCache[skinny.Id] = full;
            if (lstBranches.SelectedItem is VehicleSearchItem stillSel && stillSel.Id == skinny.Id)
                brdDetails.DataContext = full;
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void BranchItem_Copy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is VehicleSearchItem r)
        {
            Clipboard.SetText($"{r.Financer}\n{r.BranchName}\n{r.UpdatedOn}".Trim());
        }
    }
    private void BranchItem_CopyFinance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string s && !string.IsNullOrWhiteSpace(s))
            Clipboard.SetText(s);
    }
    private void BranchItem_CopyBranch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string s && !string.IsNullOrWhiteSpace(s))
            Clipboard.SetText(s);
    }

    private void btnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (brdDetails.DataContext is not VehicleSearchItem r) return;

        var sb = new System.Text.StringBuilder();
        void L(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"{label} : {value}");
        }
        L("Vehicle No",    r.VehicleNo);
        L("Chassis No",    r.ChassisNo);
        L("Model/Make",    r.Model);
        L("Engine No",     r.EngineNo);
        L("Agreement No",  r.AgreementNo);
        L("Cust. Name",    r.CustomerName);
        L("Cust. Address", r.CustomerAddress);
        L("Cust. Contact", r.CustomerContactNos);
        L("Bucket",        r.Bucket);
        L("OD",            r.OD);
        L("Branch (xlsx)", r.BranchFromExcel);
        L("Area",          r.Area);
        L("Region",        r.Region);
        L("Level 1",       r.Level1);
        L("Level 1 Contact", r.Level1ContactNos);
        L("Level 2",       r.Level2);
        L("Level 2 Contact", r.Level2ContactNos);
        L("Level 3",       r.Level3);
        L("Level 3 Contact", r.Level3ContactNos);
        L("Level 4",       r.Level4);
        L("Level 4 Contact", r.Level4ContactNos);
        L("Branch",        r.BranchName);
        L("Finance",       r.Financer);
        L("Contact 1",     r.FirstContactDetails);
        L("Contact 2",     r.SecondContactDetails);
        L("Contact 3",     r.ThirdContactDetails);
        L("Address",       r.Address);
        L("Executive",     r.ExecutiveName);

        Clipboard.SetText(sb.ToString().TrimEnd());
        MessageBox.Show("All vehicle details copied to clipboard.\nPaste with Ctrl+V.",
            "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
    }


    private void btnConfirmNow_Click(object sender, RoutedEventArgs e)
    {
        if (brdDetails.DataContext is VehicleSearchItem record)
        {
            var window = new CRMRSDesktopApp.Confirmations.ManageConfirmationWindow(record);
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
        if (brdDetails.DataContext is not VehicleSearchItem record) return;

        var chassis = IsChassisMode();
        var key     = chassis ? record.ChassisNo : record.VehicleNo;

        var copies = _fullResults
            .Where(x => (chassis ? x.ChassisNo : x.VehicleNo) == key)
            .ToList();

        List<VehicleSearchItem> toDelete;
        if (copies.Count > 1)
        {
            var picker = new DeleteRecordPickerWindow(record.VehicleNo, copies)
            {
                Owner = Window.GetWindow(this)
            };
            if (picker.ShowDialog() != true || picker.SelectedRecords.Count == 0)
                return;
            toDelete = picker.SelectedRecords;
        }
        else
        {
            var financeName = string.IsNullOrWhiteSpace(record.Financer)
                ? "this finance" : record.Financer;
            var confirm = MessageBox.Show(
                $"Permanently delete {record.VehicleNo} from {financeName}?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
            toDelete = copies;
        }

        try
        {
            btnDelete.IsEnabled = false;
            foreach (var r in toDelete)
            {
                await _searchRepo.DeleteRecordAsync(long.Parse(r.Id));
                _fullResults.Remove(r);
            }

            RebindResultsGrid();

            var remaining = _fullResults
                .Where(x => (chassis ? x.ChassisNo : x.VehicleNo) == key)
                .ToList();
            if (remaining.Any())
            {
                lstBranches.ItemsSource   = remaining;
                lblBranchCount.Text       = remaining.Count.ToString("N0");
                lstBranches.SelectedIndex = 0;
            }
            else
            {
                dgResults.SelectedItem = null;
                brdBranches.Visibility = Visibility.Collapsed;
                brdDetails.Visibility  = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnDelete.IsEnabled = true;
        }
    }
}
