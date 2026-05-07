using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Syncfusion.XlsIO;
using VRASDesktopApp.Data;
using VRASDesktopApp.Finances;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Records;

public partial class RecordValidatorAndUploaderWindow : Window
{
    public List<Branch> Branches = new();
    private readonly List<UploadRecord> _records = new();
    private readonly ObservableCollection<UploadRecord> _filteredRecords = new();

    private IWorksheet ActiveSheet { get; }
    private MappedColumns MappedColumns { get; }
    private Branch? SelectedBranch { get; set; }

    private readonly RecordsRepository _recordsRepo = new();

    public bool IsRecordsLoading
    {
        set
        {
            if (value)
            {
                ldrRecords.Visibility = Visibility.Visible;
                ldrRecords.Spin = true;
            }
            else
            {
                ldrRecords.Visibility = Visibility.Collapsed;
                ldrRecords.Spin = false;
            }
        }
    }

    public RecordValidatorAndUploaderWindow(IWorksheet activeSheet, MappedColumns mappedColumns)
    {
        InitializeComponent();
        ActiveSheet    = activeSheet;
        MappedColumns  = mappedColumns;
        txtPBR.Visibility = Visibility.Collapsed;
        pbr.Visibility    = Visibility.Collapsed;
        pbr.IsIndeterminate = true;
        dgList.ItemsSource  = _filteredRecords;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        IsRecordsLoading = true;
        ReadRecords();
        foreach (var record in FilterRecords(RecordFilters.Invalid))
            _filteredRecords.Add(record);
        mnFilterHeader.Text = $"Invalid: {_filteredRecords.Count}/{_records.Count}";
        IsRecordsLoading = false;
        await LoadBranchesAsync();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void btnMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    // ──────────────────────────────────────────────────────────────
    //  Read Excel rows into _records
    // ──────────────────────────────────────────────────────────────

    private void ReadRecords()
    {
        _records.Clear();
        var ranges  = ActiveSheet.MigrantRange;
        var lastRow = ActiveSheet.UsedRange.LastRow;

        for (var r = 3; r <= lastRow; r++)
        {
            if (MappedColumns.CI_VehicleNo == 0) continue;

            string Val(short col)
            {
                if (col == 0) return string.Empty;
                ranges.ResetRowColumn(r, col);
                return ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            }

            var vehicleNo = Val(MappedColumns.CI_VehicleNo);
            var chasisNo  = Val(MappedColumns.CI_ChasisNo);
            if (string.IsNullOrWhiteSpace(vehicleNo) && string.IsNullOrWhiteSpace(chasisNo)) continue;

            var record = new UploadRecord
            {
                VehicleNo          = vehicleNo,
                FormatedVehicleNo  = App.GetFormatedVehicleNo(vehicleNo),
                Model              = Val(MappedColumns.CI_Model),
                EngineNo           = Val(MappedColumns.CI_EngineNo),
                AgreementNo        = Val(MappedColumns.CI_AgreementNo),
                CustomerName       = Val(MappedColumns.CI_CustomerName),
                CustomerAddress    = Val(MappedColumns.CI_CustomerAddress),
                Region             = Val(MappedColumns.CI_Region),
                Area               = Val(MappedColumns.CI_Area),
                Bucket             = Val(MappedColumns.CI_Bucket),
                GV                 = Val(MappedColumns.CI_GV),
                OD                 = Val(MappedColumns.CI_OD),
                BranchName         = Val(MappedColumns.CI_Branch),
                Level1             = Val(MappedColumns.CI_Level1),
                Level1ContactNos   = Val(MappedColumns.CI_Level1ContactNo),
                Level2             = Val(MappedColumns.CI_Level2),
                Level2ContactNos   = Val(MappedColumns.CI_Level2ContactNo),
                Level3             = Val(MappedColumns.CI_Level3),
                Level3ContactNos   = Val(MappedColumns.CI_Level3ContactNo),
                Level4             = Val(MappedColumns.CI_Level4),
                Level4ContactNos   = Val(MappedColumns.CI_Level4ContactNo),
                Sec9Available      = Val(MappedColumns.CI_Sec9Available),
                Sec17Available     = Val(MappedColumns.CI_Sec17Available),
                TBRFlag            = Val(MappedColumns.CI_TBRFlag),
                Seasoning          = Val(MappedColumns.CI_Seasoning),
                SenderMailId1      = Val(MappedColumns.CI_SenderMailId1),
                SenderMailId2      = Val(MappedColumns.CI_SenderMailId2),
                ExecutiveName      = Val(MappedColumns.CI_ExecutiveName),
                POS                = Val(MappedColumns.CI_POS),
                TOSS               = Val(MappedColumns.CI_TOSS),
                CustomerContactNos = Val(MappedColumns.CI_CustomerContactNos),
                Remark             = Val(MappedColumns.CI_Remark)
            };

            if (record.FormatedVehicleNo.Length > 31)
                record.FormatedVehicleNo = record.FormatedVehicleNo[..31];
            record.FormatedVehicleNo = App.GetVehicleNoInSearchableFormated(record.FormatedVehicleNo);
            record.ChasisNo = chasisNo.Length > 32 ? chasisNo[..32] : chasisNo;

            _records.Add(record);
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Validation (RC number format check)
    // ──────────────────────────────────────────────────────────────

    private static readonly Regex RcRegex =
        new(@"^[A-Z]{2}-\d+-[A-Z]*-\d{4}|[A-Z]{2}-\d+-\d{4}$", RegexOptions.Compiled);

    private List<UploadRecord> FilterRecords(RecordFilters filter) => filter switch
    {
        RecordFilters.Invalid => _records.Where(r => !RcRegex.IsMatch(r.FormatedVehicleNo)).ToList(),
        RecordFilters.Valid   => _records.Where(r =>  RcRegex.IsMatch(r.FormatedVehicleNo)).ToList(),
        _                     => _records.ToList()
    };

    private void miInvalid_Click(object sender, RoutedEventArgs e)
    {
        _filteredRecords.Clear();
        IsRecordsLoading = true;
        foreach (var r in FilterRecords(RecordFilters.Invalid)) _filteredRecords.Add(r);
        mnFilterHeader.Text = $"Invalid: {_filteredRecords.Count}/{_records.Count}";
        IsRecordsLoading = false;
    }

    private void miValid_Click(object sender, RoutedEventArgs e)
    {
        _filteredRecords.Clear();
        IsRecordsLoading = true;
        foreach (var r in FilterRecords(RecordFilters.Valid)) _filteredRecords.Add(r);
        mnFilterHeader.Text = $"Valid: {_filteredRecords.Count}/{_records.Count}";
        IsRecordsLoading = false;
    }

    private void miAll_Click(object sender, RoutedEventArgs e)
    {
        _filteredRecords.Clear();
        IsRecordsLoading = true;
        foreach (var r in FilterRecords(RecordFilters.All)) _filteredRecords.Add(r);
        mnFilterHeader.Text = $"All: {_filteredRecords.Count}/{_records.Count}";
        IsRecordsLoading = false;
    }

    private void MaterialButton_Click(object sender, RoutedEventArgs e)
    {
        var list = dgList.SelectedItems.Cast<UploadRecord>().ToList();
        if (!list.Any() && dgList.SelectedCells.Any())
            list = dgList.SelectedCells.Select(c => c.Item).OfType<UploadRecord>().Distinct().ToList();
        foreach (var item in list)
        {
            _filteredRecords.Remove(item);
            _records.Remove(item);
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Branch loading — direct from MySQL
    // ──────────────────────────────────────────────────────────────

    private async Task LoadBranchesAsync()
    {
        btnUpload.IsEnabled = false;
        try
        {
            var branches = await _recordsRepo.GetAllBranchesAsync();
            Branches.Clear();
            Branches.AddRange(branches);
            btnUpload.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load branches: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            btnUpload.IsEnabled = true;
        }
    }

    private void btnSelectBranch_Click(object sender, RoutedEventArgs e)
    {
        btnSelectBranch.IsEnabled = false;
        var branchDialogWindow = new BranchDialogWindow(Branches) { Owner = this };
        if (branchDialogWindow.ShowDialog() == true)
        {
            SelectedBranch = branchDialogWindow.SelectedBranch;
            txtSelectedBranch.Text = SelectedBranch?.BranchName ?? string.Empty;
        }
        btnSelectBranch.IsEnabled = true;
    }

    // ──────────────────────────────────────────────────────────────
    //  Upload — direct MySQL, overwrite per branch
    // ──────────────────────────────────────────────────────────────

    private async void btnUpload_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBranch == null)
        {
            MessageBox.Show("Please select a branch first.", "Branch Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_records.Count == 0)
        {
            MessageBox.Show("No records to upload.", "Empty Records",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(SelectedBranch.BranchId, out int branchId))
        {
            MessageBox.Show("Invalid branch ID.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        btnUpload.IsEnabled    = false;
        txtPBR.Visibility      = Visibility.Visible;
        pbr.Visibility         = Visibility.Visible;
        pbr.IsIndeterminate    = true;
        txtPBR.Text            = $"Uploading {_records.Count} records to {SelectedBranch.BranchName}...";

        try
        {
            var inserted = await _recordsRepo.UploadRecordsAsync(branchId, _records);
            txtPBR.Text = $"✓ {inserted} records saved successfully!";
            MessageBox.Show(
                $"{inserted} records have been saved to branch \"{SelectedBranch.BranchName}\".\n\nPrevious records for this branch were replaced.",
                "Upload Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            txtPBR.Text = "Upload failed";
            MessageBox.Show($"Upload error: {ex.Message}", "Upload Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnUpload.IsEnabled = true;
            pbr.IsIndeterminate = false;
        }
    }
}
