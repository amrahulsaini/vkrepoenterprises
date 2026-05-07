using System.Net.Http.Json;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Records;

public partial class FindVehiclePage : Page
{
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _debounceTimer;
    private int _debounceMs = 120;
    private List<VehicleSearchItem> _fullResults = new List<VehicleSearchItem>();

    public FindVehiclePage()
    {
        InitializeComponent();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_debounceMs) };
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Removed default search so page opens empty
        // await SearchAsync();
    }

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        // restart debounce timer for aggressive instant search
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        _ = SearchIfNeededAsync();
    }

    private async Task SearchIfNeededAsync()
    {
        try
        {
            var mode = (cmbMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "rc";
            var text = txtSearch.Text ?? string.Empty;
            
            // Strip non-digits
            var digits = Regex.Replace(text, "[^0-9]", "");
            if (txtSearch.Text != digits)
            {
                var cursor = txtSearch.SelectionStart;
                txtSearch.Text = digits;
                txtSearch.SelectionStart = Math.Min(cursor, txtSearch.Text.Length);
            }

            if (string.Equals(mode, "rc", StringComparison.OrdinalIgnoreCase))
            {
                txtSearch.MaxLength = 4;
                if (digits.Length == 4) await SearchAsync();
            }
            else // chassis
            {
                txtSearch.MaxLength = 4;
                if (digits.Length == 4) await SearchAsync();
            }
        }
        catch { }
    }

    private void cmbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (txtSearch != null)
        {
            txtSearch.Text = string.Empty;
        }
        _debounceTimer?.Stop();
        _ = SearchIfNeededAsync();
    }

    private async void btnSearch_Click(object sender, RoutedEventArgs e)
    {
        var text = txtSearch.Text ?? string.Empty;
        var digits = Regex.Replace(text, "[^0-9]", "");
        if (digits.Length == 4)
        {
            await SearchAsync();
        }
        else
        {
            MessageBox.Show("Please enter exactly 4 digits to search.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SearchAsync()
    {
        try
        {
            btnSearch.IsEnabled = false;
            prgSearching.Visibility = Visibility.Visible;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            var mode = (cmbMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "rc";
            var query = txtSearch.Text.Trim();
            var url = $"{App.ApiBaseUrl}api/Records/Search?q={Uri.EscapeDataString(query)}&mode={Uri.EscapeDataString(mode)}";

            var httpResponse = await App.HttpClient.GetAsync(url, ct);
            httpResponse.EnsureSuccessStatusCode();

            var result = await httpResponse.Content.ReadFromJsonAsync<VehicleSearchResponse>(cancellationToken: ct);
            if (result == null)
            {
                return;
            }

            lblResults.Text = result.ResultCount.ToString("N0");
            lblBranchCount.Text = result.UniqueBranches.ToString("N0");
            lblMode.Text = string.Equals(result.Mode, "chassis", StringComparison.OrdinalIgnoreCase) ? "CHASSIS" : "RC";
            lblSearchHint.Text = string.IsNullOrWhiteSpace(result.Query)
                ? "Recent records are shown because the search box is empty."
                : $"Showing live matches for '{result.Query}'.";

            if (string.Equals(result.Mode, "chassis", StringComparison.OrdinalIgnoreCase))
            {
                colRc.Visibility = Visibility.Collapsed;
                colChassis.Visibility = Visibility.Visible;
            }
            else
            {
                colRc.Visibility = Visibility.Visible;
                colChassis.Visibility = Visibility.Collapsed;
            }

            _fullResults = result.Results ?? new List<VehicleSearchItem>();

            // Show unique vehicles grouping by RC/Chassis
            var isChassis = string.Equals(result.Mode, "chassis", StringComparison.OrdinalIgnoreCase);
            var groupedResults = _fullResults
                .GroupBy(x => isChassis ? x.ChassisNo : x.VehicleNo)
                .Select(g => g.First())
                .ToList();

            dgResults.ItemsSource = groupedResults;
            
            // Automatically erase placeholder text after search completes successfully
            txtSearch.Text = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // search cancelled by new input - ignore
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Vehicle search failed: {ex.Message}", "Search", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            prgSearching.Visibility = Visibility.Collapsed;
            btnSearch.IsEnabled = true;
        }
    }

    private void dgResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgResults.SelectedItem is VehicleSearchItem vehicle)
        {
            var isChassis = string.Equals((cmbMode.SelectedItem as ComboBoxItem)?.Tag?.ToString(), "chassis", StringComparison.OrdinalIgnoreCase);
            var key = isChassis ? vehicle.ChassisNo : vehicle.VehicleNo;
            
            // Get all records belonging to this vehicle to display all available Branches and Financers
            var branches = _fullResults
                .Where(x => (isChassis ? x.ChassisNo : x.VehicleNo) == key)
                .ToList();

            lstBranches.ItemsSource = branches;
            brdBranches.Visibility = Visibility.Visible;
            brdDetails.Visibility = Visibility.Collapsed; // Hide details until a branch is clicked
            
            if (branches.Count > 0)
            {
                lstBranches.SelectedIndex = 0; // Automatically select the first branch
            }
        }
        else
        {
            brdBranches.Visibility = Visibility.Collapsed;
            brdDetails.Visibility = Visibility.Collapsed;
        }
    }

    private void lstBranches_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstBranches.SelectedItem is VehicleSearchItem specificRecord)
        {
            brdDetails.Visibility = Visibility.Visible;
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
            var textToCopy = 
$@"Finance : {record.Financer}
Branch : {record.BranchName}
Vehicle No : {record.VehicleNo}
Model : {record.Model}
Chasis No : {record.ChassisNo}
Engine No : {record.EngineNo}
Agency Name : V K Enterprises
Agency Contact : 0";
            Clipboard.SetText(textToCopy);
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
            var confirm = MessageBox.Show($"Are you sure you want to {actionName} {record.VehicleNo}?", "Confirm Release", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    var url = $"{App.ApiBaseUrl}api/Records/MarkReleased/{record.Id}";
                    var response = await App.HttpClient.PostAsync(url, null);
                    response.EnsureSuccessStatusCode();

                    var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    var newStatus = result.GetProperty("status").GetString();

                    record.ReleaseStatus = newStatus ?? (currentlyReleased ? "NO" : "YES");
                    MessageBox.Show($"Record {actionName}d successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Refresh view
                    brdDetails.DataContext = null;
                    brdDetails.DataContext = record;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to toggle release status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async void btnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (brdDetails.DataContext is VehicleSearchItem record)
        {
            var confirm = MessageBox.Show($"Are you sure you want to absolutely delete {record.VehicleNo}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    var url = $"{App.ApiBaseUrl}api/Records/Delete/{record.Id}";
                    var response = await App.HttpClient.DeleteAsync(url);
                    response.EnsureSuccessStatusCode();

                    MessageBox.Show("Record deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    _fullResults.Remove(record);
                    
                    // Refresh the selected vehicle branches
                    var isChassis = string.Equals((cmbMode.SelectedItem as ComboBoxItem)?.Tag?.ToString(), "chassis", StringComparison.OrdinalIgnoreCase);
                    var key = isChassis ? record.ChassisNo : record.VehicleNo;
                    var remainingBranches = _fullResults.Where(x => (isChassis ? x.ChassisNo : x.VehicleNo) == key).ToList();
                    
                    if (remainingBranches.Any())
                    {
                        lstBranches.ItemsSource = remainingBranches;
                        lstBranches.SelectedIndex = 0;
                    }
                    else
                    {
                        await SearchAsync();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
