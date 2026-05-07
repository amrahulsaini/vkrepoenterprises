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

        if (e.Key == Key.D8)
        {
            e.Handled = true;
            var currentCellValue = sp.CurrentCellValue;
            if (string.IsNullOrWhiteSpace(currentCellValue)) return;

            if (_mappingDetails == null)
            {
                await EnsureMappingDetailsLoadedAsync();
                if (_mappingDetails == null) return;
            }

            var addMappingWindow = new AddMappingWindow(_mappingDetails.ColumnTypes, currentCellValue) { Owner = this };
            if (addMappingWindow.ShowDialog() == true)
                _mappingDetails.Mappings.Add(addMappingWindow.MappedColumn);
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
            Filter = "Excel Files|*.xls;*.xlsx;*.xlsm;*.csv",
            Title  = "Select an Excel File"
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
        Hide();
        validator.ShowDialog();
        Show();
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
