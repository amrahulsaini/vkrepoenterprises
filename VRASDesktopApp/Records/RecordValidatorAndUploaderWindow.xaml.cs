using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Syncfusion.XlsIO;
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
        ActiveSheet = activeSheet;
        MappedColumns = mappedColumns;
        txtPBR.Visibility = Visibility.Collapsed;
        pbr.Visibility = Visibility.Collapsed;
        pbr.IsIndeterminate = true;
        dgList.ItemsSource = _filteredRecords;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        IsRecordsLoading = true;
        _records.AddRange(await ReadRecordsAsync());
        foreach (var record in await FilterRecords(RecordFilters.Invalid))
        {
            _filteredRecords.Add(record);
        }
        mnFilterHeader.Text = $"Invalid: {_filteredRecords.Count}/{_records.Count}";
        IsRecordsLoading = false;
        await LoadBranches();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void btnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private async Task<List<UploadRecord>> ReadRecordsAsync()
    {
        var records = new List<UploadRecord>();
        var ranges = ActiveSheet.MigrantRange;
        var lastRow = ActiveSheet.UsedRange.LastRow;
        for (var r = 3; r <= lastRow; r++)
        {
            if (MappedColumns.CI_VehicleNo == 0)
            {
                continue;
            }

            ranges.ResetRowColumn(r, MappedColumns.CI_VehicleNo);
            var vehicleNo = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_ChasisNo);
            var chasisNo = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            if (string.IsNullOrWhiteSpace(vehicleNo) && string.IsNullOrWhiteSpace(chasisNo))
            {
                continue;
            }

            ranges.ResetRowColumn(r, MappedColumns.CI_Model);
            var model = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_EngineNo);
            var engineNo = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_AgreementNo);
            var agreementNo = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_CustomerName);
            var customerName = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_CustomerAddress);
            var customerAddress = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Region);
            var region = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Area);
            var area = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Bucket);
            var bucket = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_GV);
            var gv = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_OD);
            var od = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Branch);
            var branchName = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Level1);
            var level1 = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Level1ContactNo);
            var level1ContactNos = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Level2);
            var level2 = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Level2ContactNo);
            var level2ContactNos = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Level3);
            var level3 = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Level3ContactNo);
            var level3ContactNos = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Level4);
            var level4 = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Level4ContactNo);
            var level4ContactNos = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Sec9Available);
            var sec9Available = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Sec17Available);
            var sec17Available = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_TBRFlag);
            var tbrFlag = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Seasoning);
            var seasoning = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_SenderMailId1);
            var senderMailId1 = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_SenderMailId2);
            var senderMailId2 = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_ExecutiveName);
            var executiveName = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_POS);
            var pos = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_TOSS);
            var toss = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_CustomerContactNos);
            var customerContactNos = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");
            ranges.ResetRowColumn(r, MappedColumns.CI_Remark);
            var remark = ranges.Value.Replace("|", "").Replace("\n", "").Replace("\r", "");

            var record = new UploadRecord
            {
                VehicleNo = vehicleNo,
                FormatedVehicleNo = App.GetFormatedVehicleNo(vehicleNo),
                Model = model,
                EngineNo = engineNo,
                AgreementNo = agreementNo,
                CustomerName = customerName,
                CustomerAddress = customerAddress,
                Region = region,
                Area = area,
                Bucket = bucket,
                GV = gv,
                OD = od,
                BranchName = branchName,
                Level1 = level1,
                Level1ContactNos = level1ContactNos,
                Level2 = level2,
                Level2ContactNos = level2ContactNos,
                Level3 = level3,
                Level3ContactNos = level3ContactNos,
                Level4 = level4,
                Level4ContactNos = level4ContactNos,
                Sec9Available = sec9Available,
                Sec17Available = sec17Available,
                TBRFlag = tbrFlag,
                Seasoning = seasoning,
                SenderMailId1 = senderMailId1,
                SenderMailId2 = senderMailId2,
                ExecutiveName = executiveName,
                POS = pos,
                TOSS = toss,
                CustomerContactNos = customerContactNos,
                Remark = remark
            };

            if (record.FormatedVehicleNo.Length > 31)
            {
                record.FormatedVehicleNo = record.FormatedVehicleNo.Substring(0, 31);
            }
            record.FormatedVehicleNo = App.GetVehicleNoInSearchableFormated(record.FormatedVehicleNo);
            record.ChasisNo = chasisNo.Length > 32 ? chasisNo.Substring(0, 32) : chasisNo;

            _records.Add(record);
        }

        return await Task.FromResult(records);
    }

    private async Task<List<UploadRecord>> FilterRecords(RecordFilters filter)
    {
        var records = new List<UploadRecord>();
        switch (filter)
        {
            case RecordFilters.Invalid:
                foreach (var record in _records)
                {
                    if (!Regex.IsMatch(record.FormatedVehicleNo, "^[A-Z]{2}-\\d+-[A-Z]*-\\d{4}|[A-Z]{2}-\\d+-\\d{4}$"))
                    {
                        records.Add(record);
                    }
                }
                break;
            case RecordFilters.Valid:
                foreach (var record in _records)
                {
                    if (Regex.IsMatch(record.FormatedVehicleNo, "^[A-Z]{2}-\\d+-[A-Z]*-\\d{4}|[A-Z]{2}-\\d+-\\d{4}$"))
                    {
                        records.Add(record);
                    }
                }
                break;
            default:
                records.AddRange(_records);
                break;
        }
        return await Task.FromResult(records);
    }

    private async void miInvalid_Click(object sender, RoutedEventArgs e)
    {
        mnFilterHeader.Text = "Invalid";
        _filteredRecords.Clear();
        IsRecordsLoading = true;
        foreach (var record in await FilterRecords(RecordFilters.Invalid))
        {
            _filteredRecords.Add(record);
        }
        mnFilterHeader.Text = $"Invalid: {_filteredRecords.Count}/{_records.Count}";
        IsRecordsLoading = false;
    }

    private async void miValid_Click(object sender, RoutedEventArgs e)
    {
        mnFilterHeader.Text = "Valid";
        _filteredRecords.Clear();
        IsRecordsLoading = true;
        foreach (var record in await FilterRecords(RecordFilters.Valid))
        {
            _filteredRecords.Add(record);
        }
        mnFilterHeader.Text = $"Valid: {_filteredRecords.Count}/{_records.Count}";
        IsRecordsLoading = false;
    }

    private async void miAll_Click(object sender, RoutedEventArgs e)
    {
        mnFilterHeader.Text = "All";
        _filteredRecords.Clear();
        IsRecordsLoading = true;
        foreach (var record in await FilterRecords(RecordFilters.All))
        {
            _filteredRecords.Add(record);
        }
        mnFilterHeader.Text = $"All: {_filteredRecords.Count}/{_records.Count}";
        IsRecordsLoading = false;
    }

    private void MaterialButton_Click(object sender, RoutedEventArgs e)
    {
        var list = dgList.SelectedItems.Cast<UploadRecord>().ToList();
        if (!list.Any() && dgList.SelectedCells.Any())
        {
            list = dgList.SelectedCells.Select(c => c.Item).OfType<UploadRecord>().Distinct().ToList();
        }

        foreach (var item in list)
        {
            _filteredRecords.Remove(item);
            _records.Remove(item);
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

    private async void btnUpload_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBranch == null)
        {
            MessageBox.Show("Select branch");
            return;
        }

        var count = _records.Count;
        btnUpload.IsEnabled = false;
        txtPBR.Visibility = Visibility.Visible;
        pbr.Visibility = Visibility.Visible;
        var fileInfo = new FileInfo(Path.GetTempPath() + "vras_upload_records.csv");
        try
        {
            if (fileInfo.Exists)
            {
                fileInfo.Delete();
            }
            using (var writer = fileInfo.CreateText())
            {
                for (var i = 0; i < count; i++)
                {
                    var record = _records[i];
                    var line = $"{App.Reverse(record.FormatedVehicleNo)}|{App.Reverse(record.ChasisNo)}|{record.Model}|{record.EngineNo}|{record.AgreementNo}|{record.CustomerName}|{record.CustomerAddress}|{record.Region}|{record.Area}|{record.Bucket}|{record.GV}|{record.OD}|{record.BranchName}|{record.Level1}|{record.Level1ContactNos}|{record.Level2}|{record.Level2ContactNos}|{record.Level3}|{record.Level3ContactNos}|{record.Level4}|{record.Level4ContactNos}|{record.Sec9Available}|{record.Sec17Available}|{record.TBRFlag}|{record.Seasoning}|{record.SenderMailId1}|{record.SenderMailId2}|{record.ExecutiveName}|{record.POS}|{record.TOSS}|{record.CustomerContactNos}|{record.Remark}";
                    await writer.WriteLineAsync(line);
                }
            }

            using var fileToCompress = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read);
            using var content = new MultipartFormDataContent("Upload----" + DateTime.Now.ToString(CultureInfo.InvariantCulture));
            content.Add(new StreamContent(fileToCompress), "RecordsFile", fileInfo.Name);
            (await App.HttpClient.PostAsync(App.ApiBaseUrl + "api/Records/PostRecordsFile?BranchId=" + SelectedBranch.BranchId, content))
                .EnsureSuccessStatusCode();
            txtPBR.Text = "Records uploaded successfully";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
        finally
        {
            btnUpload.IsEnabled = true;
            pbr.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadBranches(int financeId = 0)
    {
        try
        {
            btnUpload.IsEnabled = false;
            var response = await App.HttpClient.GetAsync(App.ApiBaseUrl + "api/Branches/GetBranches/" + financeId);
            response.EnsureSuccessStatusCode();
            var branches = await response.Content.ReadFromJsonAsync<List<Branch>>() ?? new List<Branch>();
            Branches.AddRange(branches);
            btnUpload.IsEnabled = true;
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show("Http request exception: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show("Exception: " + ex.Message);
        }
        finally
        {
            btnUpload.IsEnabled = true;
        }
    }
}
