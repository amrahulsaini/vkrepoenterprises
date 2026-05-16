using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Syncfusion.UI.Xaml.Grid.Utility;
using Syncfusion.UI.Xaml.Spreadsheet;
using Syncfusion.UI.Xaml.Spreadsheet.Helpers;
using Syncfusion.Windows.Tools;
using Syncfusion.Windows.Tools.Controls;
using Syncfusion.XlsIO;
using VRASDesktopApp.Data;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Records;

public partial class RecordsEditorWindow : RibbonWindow
{
    private const int RecordHeadingIndex = 2;
    private MappingDetails? _mappingDetails;
    private MappedColumns _mappedColumns;
    private Task? _mappingDetailsLoadTask;
    private readonly MappingRepository _mappingRepo = new();

    public RecordsEditorWindow()
    {
        InitializeComponent();
        _mappedColumns = new MappedColumns();
    }

    // On activation, minimize the other big windows so taskbar switching is
    // always a reliable restore-from-minimized.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        WindowSwitch.MinimizeOthers(this);
    }

    private Task EnsureMappingDetailsLoadedAsync()
    {
        _mappingDetailsLoadTask ??= LoadMappingDetailsAsync();
        return _mappingDetailsLoadTask;
    }

    private async Task LoadMappingDetailsAsync()
    {
        try
        {
            _mappingDetails = await _mappingRepo.GetMappingDetailsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load mapping details: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Unmap(Mapping mapping)
    {
        if (_mappingDetails == null) return;
        try
        {
            await _mappingRepo.DeleteMappingAsync(mapping.MappingId);
            _mappingDetails.Mappings.Remove(mapping);
            MapColumns();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to remove mapping: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RibbonWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await EnsureMappingDetailsLoadedAsync();
    }

    private void rbn_Loaded(object sender, RoutedEventArgs e)
    {
        var ribbon = GridUtil.GetVisualChild<Ribbon>(sender as FrameworkElement);
        if (ribbon == null) return;

        var mapButton = new RibbonButton { SizeForm = SizeForm.Large, Label = "Map Columns" };
        mapButton.Click += btnMapColumns_Click;

        var mapExplorerButton = new RibbonButton { SizeForm = SizeForm.Large, Label = "Mappings Explorer" };
        mapExplorerButton.Click += btnMappingExplorer_Click;

        var uploadButton = new RibbonButton { SizeForm = SizeForm.Large, Label = "Upload Excel" };
        uploadButton.Click += btnUploadExcel_Click;

        var mappingBar = new RibbonBar { Header = string.Empty, IsLauncherButtonVisible = false };
        mappingBar.Items.Add(mapButton);
        mappingBar.Items.Add(mapExplorerButton);
        mappingBar.Items.Add(uploadButton);

        var verifyButton = new RibbonButton { SizeForm = SizeForm.Large, Label = "Verify records" };
        verifyButton.Click += btnVerifyRecords_Click;

        var verifyBar = new RibbonBar { Header = string.Empty, IsLauncherButtonVisible = false };
        verifyBar.Items.Add(verifyButton);

        var tab = new RibbonTab { Caption = "UPLOAD" };
        tab.Items.Add(mappingBar);
        tab.Items.Add(verifyBar);
        ribbon.Items.Add(tab);
    }

    private void sp_WorkbookLoaded(object sender, WorkbookLoadedEventArgs args)
    {
        sp.Workbook.ActiveSheet.InsertRow(1, 1, ExcelInsertOptions.FormatDefault);
        var lastColumn = sp.ActiveSheet.UsedRange.LastColumn;
        var migrantRange = sp.ActiveSheet.MigrantRange;
        for (var i = 1; i <= lastColumn; i++)
        {
            migrantRange.ResetRowColumn(RecordHeadingIndex, i);
            migrantRange.CellStyle.ColorIndex = ExcelKnownColors.White;
            sp.ActiveGrid.InvalidateCell(RecordHeadingIndex, i);
        }
    }

    private async void sp_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;

        if (e.Key == Key.M)
        {
            e.Handled = true;
            var currentCellValue = sp.CurrentCellValue;
            if (string.IsNullOrWhiteSpace(currentCellValue)) return;

            if (_mappingDetails == null)
            {
                await EnsureMappingDetailsLoadedAsync();
                if (_mappingDetails == null) return;
            }

            // Collect which column type IDs are already mapped so they don't show in the picker
            var alreadyMapped = new HashSet<int>();
            if (_mappedColumns.CI_VehicleNo        != 0) alreadyMapped.Add(1);
            if (_mappedColumns.CI_ChasisNo         != 0) alreadyMapped.Add(2);
            if (_mappedColumns.CI_Model            != 0) alreadyMapped.Add(3);
            if (_mappedColumns.CI_EngineNo         != 0) alreadyMapped.Add(4);
            if (_mappedColumns.CI_AgreementNo      != 0) alreadyMapped.Add(5);
            if (_mappedColumns.CI_CustomerName     != 0) alreadyMapped.Add(6);
            if (_mappedColumns.CI_CustomerAddress  != 0) alreadyMapped.Add(7);
            if (_mappedColumns.CI_Region           != 0) alreadyMapped.Add(8);
            if (_mappedColumns.CI_Area             != 0) alreadyMapped.Add(9);
            if (_mappedColumns.CI_Bucket           != 0) alreadyMapped.Add(10);
            if (_mappedColumns.CI_GV               != 0) alreadyMapped.Add(11);
            if (_mappedColumns.CI_OD               != 0) alreadyMapped.Add(12);
            if (_mappedColumns.CI_Branch           != 0) alreadyMapped.Add(13);
            if (_mappedColumns.CI_Level1           != 0) alreadyMapped.Add(14);
            if (_mappedColumns.CI_Level1ContactNo  != 0) alreadyMapped.Add(15);
            if (_mappedColumns.CI_Level2           != 0) alreadyMapped.Add(16);
            if (_mappedColumns.CI_Level2ContactNo  != 0) alreadyMapped.Add(17);
            if (_mappedColumns.CI_Level3           != 0) alreadyMapped.Add(18);
            if (_mappedColumns.CI_Level3ContactNo  != 0) alreadyMapped.Add(19);
            if (_mappedColumns.CI_Level4           != 0) alreadyMapped.Add(20);
            if (_mappedColumns.CI_Level4ContactNo  != 0) alreadyMapped.Add(21);
            if (_mappedColumns.CI_Sec9Available    != 0) alreadyMapped.Add(22);
            if (_mappedColumns.CI_Sec17Available   != 0) alreadyMapped.Add(23);
            if (_mappedColumns.CI_TBRFlag          != 0) alreadyMapped.Add(24);
            if (_mappedColumns.CI_Seasoning        != 0) alreadyMapped.Add(25);
            if (_mappedColumns.CI_SenderMailId1    != 0) alreadyMapped.Add(26);
            if (_mappedColumns.CI_SenderMailId2    != 0) alreadyMapped.Add(27);
            if (_mappedColumns.CI_ExecutiveName    != 0) alreadyMapped.Add(28);
            if (_mappedColumns.CI_POS              != 0) alreadyMapped.Add(29);
            if (_mappedColumns.CI_TOSS             != 0) alreadyMapped.Add(30);
            if (_mappedColumns.CI_CustomerContactNos != 0) alreadyMapped.Add(31);
            if (_mappedColumns.CI_Remark           != 0) alreadyMapped.Add(32);

            var availableTypes = _mappingDetails.ColumnTypes
                .Where(t => !alreadyMapped.Contains(t.ColumnTypeId))
                .ToList();

            var addMappingWindow = new AddMappingWindow(availableTypes, currentCellValue) { Owner = this };
            if (addMappingWindow.ShowDialog() == true)
            {
                _mappingDetails.Mappings.Add(addMappingWindow.MappedColumn);
                MapColumns();
            }
        }
        else if (e.Key == Key.OemPipe)
        {
            e.Handled = true;
            if (MessageBox.Show("Are you sure to unmap this column?", "Unmap confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var currentCellValue = sp.CurrentCellValue;
            var mappingName = Regex.Replace(currentCellValue, "[^A-Za-z0-9]", "").ToLower();
            var mapping = _mappingDetails?.Mappings.FirstOrDefault(m => m.Name.Contains(mappingName));
            if (mapping == null)
                MessageBox.Show("No mapping found with this name.");
            else
                Unmap(mapping);
        }
    }

    private async void btnUploadExcel_Click(object sender, RoutedEventArgs e)
    {
        await EnsureMappingDetailsLoadedAsync();
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter      = "All Excel Files|*.xls;*.xlsx;*.xlsm;*.xlam;*.xlt;*.xltx;*.xltm;*.csv" +
                          "|Excel Workbook (*.xlsx)|*.xlsx" +
                          "|Excel 97-2003 Workbook (*.xls)|*.xls" +
                          "|CSV (Comma delimited)|*.csv",
            FilterIndex = 1,
            Title       = "Select an Excel File"
        };
        if (openFileDialog.ShowDialog() == true)
            sp.Open(openFileDialog.FileName);
    }

    private void MapColumns()
    {
        if (_mappingDetails == null)
        {
            MessageBox.Show("Mapping details are not loaded.");
            return;
        }

        var mapped       = new MappedColumns();
        var lastColumn   = sp.ActiveSheet.UsedRange.LastColumn;
        var migrantRange = sp.ActiveSheet.MigrantRange;

        for (short col = 1; col <= lastColumn; col++)
        {
            // Clear the label row above the header
            migrantRange.ResetRowColumn(RecordHeadingIndex - 1, col);
            migrantRange.Value = string.Empty;
            sp.ActiveGrid.InvalidateCell(RecordHeadingIndex - 1, col);

            migrantRange.ResetRowColumn(RecordHeadingIndex, col);
            var columnHeading = Regex.Replace(migrantRange.Value, "[^A-Za-z0-9]", "").ToLower();
            var mapping       = _mappingDetails.Mappings.FirstOrDefault(m => m.Name == columnHeading);

            if (mapping == null)
            {
                migrantRange.CellStyle.ColorIndex = ExcelKnownColors.White;
                sp.ActiveGrid.InvalidateCell(RecordHeadingIndex, col);
                continue;
            }

            // Highlight mapped column yellow
            migrantRange.CellStyle.ColorIndex = ExcelKnownColors.Yellow;
            sp.ActiveGrid.InvalidateCell(RecordHeadingIndex, col);

            switch (mapping.ColumnTypeId)
            {
                case 1:  mapped.CI_VehicleNo          = col; break;
                case 2:  mapped.CI_ChasisNo            = col; break;
                case 3:  mapped.CI_Model               = col; break;
                case 4:  mapped.CI_EngineNo            = col; break;
                case 5:  mapped.CI_AgreementNo         = col; break;
                case 6:  mapped.CI_CustomerName        = col; break;
                case 7:  mapped.CI_CustomerAddress     = col; break;
                case 8:  mapped.CI_Region              = col; break;
                case 9:  mapped.CI_Area                = col; break;
                case 10: mapped.CI_Bucket              = col; break;
                case 11: mapped.CI_GV                  = col; break;
                case 12: mapped.CI_OD                  = col; break;
                case 13: mapped.CI_Branch              = col; break;
                case 14: mapped.CI_Level1              = col; break;
                case 15: mapped.CI_Level1ContactNo     = col; break;
                case 16: mapped.CI_Level2              = col; break;
                case 17: mapped.CI_Level2ContactNo     = col; break;
                case 18: mapped.CI_Level3              = col; break;
                case 19: mapped.CI_Level3ContactNo     = col; break;
                case 20: mapped.CI_Level4              = col; break;
                case 21: mapped.CI_Level4ContactNo     = col; break;
                case 22: mapped.CI_Sec9Available       = col; break;
                case 23: mapped.CI_Sec17Available      = col; break;
                case 24: mapped.CI_TBRFlag             = col; break;
                case 25: mapped.CI_Seasoning           = col; break;
                case 26: mapped.CI_SenderMailId1       = col; break;
                case 27: mapped.CI_SenderMailId2       = col; break;
                case 28: mapped.CI_ExecutiveName       = col; break;
                case 29: mapped.CI_POS                 = col; break;
                case 30: mapped.CI_TOSS                = col; break;
                case 31: mapped.CI_CustomerContactNos  = col; break;
                case 32: mapped.CI_Remark              = col; break;
            }

            // Write the standard field name above the column header
            migrantRange.ResetRowColumn(RecordHeadingIndex - 1, col);
            var columnType = _mappingDetails.ColumnTypes.Find(t => t.ColumnTypeId == mapping.ColumnTypeId);
            migrantRange.Value = columnType?.ColumnTypeName ?? string.Empty;
            sp.ActiveGrid.InvalidateCell(RecordHeadingIndex - 1, col);
        }

        _mappedColumns = mapped;
    }

    private async void btnMapColumns_Click(object sender, RoutedEventArgs e)
    {
        if (_mappingDetails == null)
        {
            await EnsureMappingDetailsLoadedAsync();
            if (_mappingDetails == null)
            {
                MessageBox.Show("Mapping details are not loaded.");
                return;
            }
        }
        MapColumns();
    }

    private void btnVerifyRecords_Click(object sender, RoutedEventArgs e)
    {
        var validator = new RecordValidatorAndUploaderWindow(sp.ActiveSheet, _mappedColumns);
        validator.Show();
    }

    private void btnMappingExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_mappingDetails == null)
        {
            MessageBox.Show("Mapping details are not loaded.");
            return;
        }
        new MappingExplorerWindow(_mappingDetails) { Owner = this }.ShowDialog();
    }

    private void AddSampleData() { }
}
