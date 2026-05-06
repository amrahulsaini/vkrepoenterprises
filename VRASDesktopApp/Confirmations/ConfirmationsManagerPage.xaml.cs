using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Data;
using System.IO;
using Microsoft.Win32;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Tables;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Confirmations;

public partial class ConfirmationsManagerPage : Page
{
    private List<ConfirmationResponseItem> _allConfirmations = new();

    public ConfirmationsManagerPage()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadConfirmationsAsync();
    }

    private async Task LoadConfirmationsAsync()
    {
        try
        {
            var confirmations = await App.HttpClient.GetFromJsonAsync<List<ConfirmationResponseItem>>(
                $"{App.ApiBaseUrl}api/Confirmations");
            
            _allConfirmations = confirmations ?? new List<ConfirmationResponseItem>();
            FilterGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load confirmations: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        lblSearchWatermark.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        FilterGrid();
    }

    private async void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadConfirmationsAsync();
    }

    private void FilterGrid()
    {
        var searchText = txtSearch.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(searchText))
        {
            dgConfirmations.ItemsSource = _allConfirmations;
            return;
        }

        dgConfirmations.ItemsSource = _allConfirmations.Where(c => 
            (c.VehicleNo ?? string.Empty).ToLowerInvariant().Contains(searchText) ||
            (c.ChassisNo ?? string.Empty).ToLowerInvariant().Contains(searchText)).ToList();
    }

    private void btnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = "ConfirmationsReport"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                btnExport.IsEnabled = false;
                using (var pdfDocument = new PdfDocument())
                {
                    pdfDocument.PageSettings.Size = PdfPageSize.A4;
                    var pdfPage = pdfDocument.Pages.Add();
                    var graphics = pdfPage.Graphics;
                    var font = new PdfStandardFont(PdfFontFamily.Helvetica, 9f);
                    
                    using (var fileStream = File.Create(saveFileDialog.FileName))
                    {
                        var pdfLightTable = new PdfLightTable();
                        var pdfLightTableStyle = new PdfLightTableStyle
                        {
                            CellPadding = 2f,
                            ShowHeader = true
                        };
                        pdfLightTable.Style = pdfLightTableStyle;
                        
                        var defaultStyle = new PdfCellStyle(font, PdfBrushes.Black, new PdfPen(PdfBrushes.DarkGray, 0.5f));
                        pdfLightTable.Style.DefaultStyle = defaultStyle;
                        
                        var dataTable = new DataTable();
                        dataTable.Columns.Add("Sr.No.");
                        dataTable.Columns.Add("Vehicle No");
                        dataTable.Columns.Add("Chassis No");
                        dataTable.Columns.Add("Model");
                        dataTable.Columns.Add("Seizer");
                        dataTable.Columns.Add("Status");
                        dataTable.Columns.Add("Confirmed On");
                        
                        var items = dgConfirmations.ItemsSource as List<ConfirmationResponseItem> ?? _allConfirmations;
                        int num = 1;
                        foreach (var confirmation in items)
                        {
                            dataTable.Rows.Add(
                                num.ToString(),
                                confirmation.VehicleNo ?? "",
                                confirmation.ChassisNo ?? "",
                                confirmation.Model ?? "",
                                confirmation.SeizerName ?? "",
                                confirmation.Status ?? "",
                                confirmation.ConfirmedOn ?? ""
                            );
                            num++;
                        }
                        
                        pdfLightTable.DataSource = dataTable;
                        pdfLightTable.Draw(pdfPage, new Syncfusion.Drawing.PointF(0f, 0f));
                        pdfDocument.Save(fileStream);
                        fileStream.Flush();
                    }
                    pdfDocument.Close(completely: true);
                }
                MessageBox.Show("Report downloaded successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnExport.IsEnabled = true;
        }
    }
}
