using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using CRMRSDesktopApp.Data;
using CRMRSDesktopApp.Models;
using SFColor = Syncfusion.Drawing.Color;
using DocAlign = Syncfusion.DocIO.DLS.HorizontalAlignment;

namespace CRMRSDesktopApp.Billing;

public partial class BillingPage : Page
{
    private readonly FinanceRepository _finances = new();
    private readonly VehicleSearchRepository _search = new();
    private List<VehicleSearchItem> _results = new();
    private string? _letterheadUrl;
    private string? _backgroundUrl;

    private class FinanceOption { public int Id { get; set; } public string Name { get; set; } = ""; }

    public BillingPage()
    {
        InitializeComponent();
        Loaded += BillingPage_Loaded;
    }

    private async void BillingPage_Loaded(object sender, RoutedEventArgs e)
    {
        txtInvoiceDate.Text = DateTime.Today.ToString("dd/MM/yyyy");
        txtDateRepo.Text    = DateTime.Today.ToString("dd-MM-yyyy");
        if (string.IsNullOrWhiteSpace(txtEnclosed.Text)) txtEnclosed.Text = "REPO KIT";
        if (string.IsNullOrWhiteSpace(txtQty.Text)) txtQty.Text = "01";
        if (string.IsNullOrWhiteSpace(txtAddlCharges.Text)) txtAddlCharges.Text = "NA";

        var realAgency = (App.SignedAppUser?.IsAgency == true && !string.IsNullOrWhiteSpace(App.SignedAppUser.AgencyName))
            ? App.SignedAppUser!.AgencyName
            : App.Firm.FirmName;
        txtAgencyName.Text = realAgency;
        txtAgencyName.IsReadOnly = true;

        try
        {
            var s = await DesktopApiClient.GetBillingSettingsAsync();
            if (s != null)
            {
                txtPan.Text        = s.PanNo;
                txtGst.Text        = s.GstState;
                txtAcHolder.Text   = s.BankAccountName;
                txtAccountNo.Text  = s.AccountNo;
                txtIfsc.Text       = s.IfscCode;
                txtBankBranch.Text = s.BankBranch;
                txtParkingYard.Text = s.ParkingYard;
                txtPaymentName.Text = string.IsNullOrWhiteSpace(s.PaymentName) ? realAgency : s.PaymentName;
                txtFooter.Text     = s.FooterLine;
                _letterheadUrl = s.LetterheadUrl;
                _backgroundUrl = s.BackgroundUrl;
                ShowPreview(imgLetterhead, _letterheadUrl);
                ShowPreview(imgBackground, _backgroundUrl);
            }
        }
        catch (Exception ex) { txtSearchStatus.Text = "Could not load billing settings: " + ex.Message; }

        try
        {
            var list = await _finances.GetFinancesAsync();
            cmbFinance.ItemsSource = list.Select(f => new FinanceOption { Id = f.Id, Name = f.Name })
                                         .OrderBy(f => f.Name).ToList();
        }
        catch (Exception ex) { txtSearchStatus.Text = "Could not load head offices: " + ex.Message; }
    }

    private static void ShowPreview(Image target, string? url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) { target.Source = null; return; }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(url + "?t=" + DateTime.Now.Ticks);
            bmp.EndInit();
            target.Source = bmp;
        }
        catch { }
    }

    private async void btnLetterhead_Click(object sender, RoutedEventArgs e) => await UploadImage("letterhead", imgLetterhead);
    private async void btnBackground_Click(object sender, RoutedEventArgs e) => await UploadImage("background", imgBackground);

    private async Task UploadImage(string kind, Image preview)
    {
        var dlg = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(dlg.FileName));
            var url = await DesktopApiClient.UploadBillingImageAsync(kind, b64);
            if (kind == "letterhead") _letterheadUrl = url; else _backgroundUrl = url;
            ShowPreview(preview, url);
            txtGenStatus.Text = $"{kind} uploaded.";
        }
        catch (Exception ex) { MessageBox.Show("Upload failed: " + ex.Message, "Billing", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void cmbFinance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbFinance.SelectedItem is FinanceOption f && string.IsNullOrWhiteSpace(txtBankTo.Text))
            txtBankTo.Text = f.Name;
    }

    private void txtVehSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) btnSearch_Click(sender, e);
    }

    private async void btnSearch_Click(object sender, RoutedEventArgs e)
    {
        var q = txtVehSearch.Text.Trim();
        if (q.Length == 0) { txtSearchStatus.Text = "Enter last 4 (RC) or 5 (chassis) digits."; return; }
        txtSearchStatus.Text = "Searching…";
        lstResults.ItemsSource = null;
        try
        {
            _results = rbChassis.IsChecked == true
                ? await _search.SearchByChassisLast5Async(q)
                : await _search.SearchByRcLast4Async(q);
            lstResults.ItemsSource = _results;
            txtSearchStatus.Text = $"{_results.Count} vehicle(s) found.";
        }
        catch (Exception ex) { txtSearchStatus.Text = "Search failed: " + ex.Message; }
    }

    private async void lstResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstResults.SelectedItem is not VehicleSearchItem sel) return;
        var rec = sel;
        if (long.TryParse(sel.Id, out var id))
        {
            try { rec = await _search.GetRecordByIdAsync(id) ?? sel; } catch { }
        }
        txtAgriLoan.Text  = rec.AgreementNo;
        txtCustomer.Text  = rec.CustomerName;
        txtMakeModel.Text = rec.Model;
        txtRcNo.Text      = rec.VehicleNo;
        txtBranch.Text    = string.IsNullOrWhiteSpace(rec.BranchName) ? rec.BranchFromExcel : rec.BranchName;
    }

    private async void btnGenerate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await DesktopApiClient.SaveBillingSettingsAsync(new
            {
                AgencyName = txtAgencyName.Text.Trim(), PanNo = txtPan.Text.Trim(), GstState = txtGst.Text.Trim(),
                BankAccountName = txtAcHolder.Text.Trim(), AccountNo = txtAccountNo.Text.Trim(), IfscCode = txtIfsc.Text.Trim(),
                BankBranch = txtBankBranch.Text.Trim(), ParkingYard = txtParkingYard.Text.Trim(),
                PaymentName = txtPaymentName.Text.Trim(), FooterLine = txtFooter.Text.Trim()
            });
        }
        catch (Exception ex) { MessageBox.Show("Could not save billing settings: " + ex.Message, "Billing", MessageBoxButton.OK, MessageBoxImage.Warning); }

        var safe = new string(txtRcNo.Text.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0) safe = "bill";
        var dlg = new SaveFileDialog
        {
            Title = "Save Repossession Bill",
            Filter = "Word document (*.docx)|*.docx",
            FileName = $"RepoBill_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.docx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var lh = await DownloadBytes(_letterheadUrl);
            var bg = await DownloadBytes(_backgroundUrl);
            BuildDocx(dlg.FileName, lh, bg);
            txtGenStatus.Text = "Bill generated.";
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to generate: " + ex.Message, "Billing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task<byte[]?> DownloadBytes(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try { return await App.HttpClient.GetByteArrayAsync(url); } catch { return null; }
    }

    private const string FontName = "Times New Roman";

    private enum RowKind { Span, SpanRight, Kv, Hdr }
    private record Row(RowKind Kind, string A, string B = "", string C = "");

    private void BuildDocx(string filePath, byte[]? letterhead, byte[]? background)
    {
        using var doc = new WordDocument();
        var sec = doc.AddSection();
        sec.PageSetup.Margins.All = 36;
        float pageW = sec.PageSetup.PageSize.Width - 72;

        if (background != null)
        {
            try
            {
                var hpara = sec.HeadersFooters.Header.AddParagraph();
                var bgPic = hpara.AppendPicture(background);
                bgPic.TextWrappingStyle  = TextWrappingStyle.Behind;
                bgPic.HorizontalOrigin   = HorizontalOrigin.Page;
                bgPic.VerticalOrigin     = VerticalOrigin.Page;
                bgPic.HorizontalPosition = 0f;
                bgPic.VerticalPosition   = 0f;
                bgPic.Width  = sec.PageSetup.PageSize.Width;
                bgPic.Height = sec.PageSetup.PageSize.Height;
            }
            catch { }
        }

        if (letterhead != null)
        {
            var hp = sec.AddParagraph();
            hp.ParagraphFormat.HorizontalAlignment = DocAlign.Center;
            var pic = hp.AppendPicture(letterhead);
            if (pic.Width > pageW) { float r = pageW / pic.Width; pic.Width *= r; pic.Height *= r; }
        }
        else
        {
            var p = sec.AddParagraph();
            p.ParagraphFormat.HorizontalAlignment = DocAlign.Center;
            var r = p.AppendText(txtAgencyName.Text.Trim());
            r.CharacterFormat.FontName = FontName; r.CharacterFormat.FontSize = 18; r.CharacterFormat.Bold = true;
        }

        var rule = sec.AddParagraph();
        rule.ParagraphFormat.Borders.Bottom.BorderType = BorderStyle.Single;
        rule.ParagraphFormat.Borders.Bottom.Color = SFColor.Red;
        rule.ParagraphFormat.Borders.Bottom.LineWidth = 1.5f;

        var pay = string.IsNullOrWhiteSpace(txtPaymentName.Text) ? txtAgencyName.Text.Trim() : txtPaymentName.Text.Trim();
        var totalAmt = string.IsNullOrWhiteSpace(txtTotalAmount.Text) ? txtRepoAmount.Text.Trim() : txtTotalAmount.Text.Trim();
        var rows = new List<Row>
        {
            new(RowKind.Span, $"To,  {txtBankTo.Text.Trim()},"),
            new(RowKind.Span, "SUBJECT–SUBMISSION OF REPOSSESSION BILL."),
            new(RowKind.Kv, "INVOICE DATE -", txtInvoiceDate.Text.Trim()),
            new(RowKind.Kv, "INVOICE NO-", txtInvoiceNo.Text.Trim()),
            new(RowKind.Kv, "BRANCH-", txtBranch.Text.Trim()),
            new(RowKind.Kv, "CONFIRMATION BY-", txtConfirmationBy.Text.Trim()),
            new(RowKind.Hdr, "DESCRIPTION EXPENSE", "ALL DETAILS", "AMOUNT"),
            new(RowKind.Kv, "AGRI-LOAN NO", txtAgriLoan.Text.Trim()),
            new(RowKind.Kv, "NAME OF CUSTOMER", txtCustomer.Text.Trim()),
            new(RowKind.Kv, "MAKE-MODEL", txtMakeModel.Text.Trim()),
            new(RowKind.Kv, "RC NO", txtRcNo.Text.Trim()),
            new(RowKind.Kv, "DATE OF REPOSSESSION", txtDateRepo.Text.Trim()),
            new(RowKind.Kv, "PARKING YARD NAME", txtParkingYard.Text.Trim()),
            new(RowKind.Kv, "NAME OF AGNCY", txtAgencyName.Text.Trim()),
            new(RowKind.Kv, "ENCLOSED", txtEnclosed.Text.Trim()),
            new(RowKind.Kv, "QTY", txtQty.Text.Trim()),
            new(RowKind.Kv, "REPO CHARGES", txtRepoWords.Text.Trim(), txtRepoAmount.Text.Trim()),
            new(RowKind.Kv, "ADDITIONAL CHARGES", txtAddlCharges.Text.Trim()),
            new(RowKind.Kv, "PAN NO", txtPan.Text.Trim()),
            new(RowKind.Kv, "GST STATE", txtGst.Text.Trim()),
            new(RowKind.Kv, "BANK ACCOUNT NAME", txtAcHolder.Text.Trim()),
            new(RowKind.Kv, "ACCOUNT NO", txtAccountNo.Text.Trim()),
            new(RowKind.Kv, "IFSC CODE", txtIfsc.Text.Trim()),
            new(RowKind.Kv, "BRANCH", txtBankBranch.Text.Trim()),
            new(RowKind.Kv, "TOTAL GROSS AMOUNT", txtTotalWords.Text.Trim(), totalAmt),
            new(RowKind.Span, $"KINDIY RELEASE THE PAYMENT IN THE NAME OF M/S {pay}"),
            new(RowKind.Span, ""),
            new(RowKind.SpanRight, "Thank You"),
            new(RowKind.SpanRight, txtAgencyName.Text.Trim()),
            new(RowKind.SpanRight, txtFooter.Text.Trim()),
        };

        var t = sec.AddTable();
        t.ResetCells(rows.Count, 3);
        t.TableFormat.Borders.BorderType = BorderStyle.Single;
        t.TableFormat.Borders.LineWidth = 0.5f;
        t.TableFormat.Borders.Color = SFColor.Black;
        t.TableFormat.Borders.Horizontal.BorderType = BorderStyle.Single;
        t.TableFormat.Borders.Vertical.BorderType = BorderStyle.Single;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            switch (row.Kind)
            {
                case RowKind.Span:
                    CellText(t, i, 0, row.A, bold: i < 2, align: DocAlign.Center);
                    t.ApplyHorizontalMerge(i, 0, 2);
                    break;
                case RowKind.SpanRight:
                    CellText(t, i, 0, row.A, bold: true, align: DocAlign.Right);
                    t.ApplyHorizontalMerge(i, 0, 2);
                    break;
                case RowKind.Hdr:
                    CellText(t, i, 0, row.A, bold: true);
                    CellText(t, i, 1, row.B, bold: true);
                    CellText(t, i, 2, row.C, bold: true);
                    break;
                default:
                    CellText(t, i, 0, row.A, bold: true);
                    CellText(t, i, 1, row.B);
                    CellText(t, i, 2, row.C, bold: true);
                    break;
            }
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        doc.Save(fs, FormatType.Docx);
    }

    private static void CellText(IWTable t, int row, int col, string text, bool bold = false, DocAlign align = DocAlign.Left)
    {
        var p = t[row, col].AddParagraph();
        p.ParagraphFormat.HorizontalAlignment = align;
        var r = p.AppendText(text ?? "");
        r.CharacterFormat.FontName = FontName;
        r.CharacterFormat.FontSize = 9f;
        r.CharacterFormat.Bold = bold;
    }
}
