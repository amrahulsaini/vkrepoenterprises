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
        txtRepoAmount.TextChanged  += (_, __) => Recompute();
        txtAddlCharges.TextChanged += (_, __) => Recompute();
    }

    private void Recompute()
    {
        long repo  = ParseAmt(txtRepoAmount.Text);
        long addl  = ParseAmt(txtAddlCharges.Text);
        long total = repo + addl;
        txtRepoWords.Text   = Words(repo);
        txtTotalAmount.Text = Rs(total);
        txtTotalWords.Text  = Words(total);
    }

    private static long ParseAmt(string? s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"\d+(\.\d+)?");
        return m.Success && decimal.TryParse(m.Value, out var v) ? (long)Math.Round(v) : 0;
    }

    private static string Rs(long n) => n > 0 ? $"RS.{n}/-" : "";
    private static string Words(long n) => n > 0 ? IndianWords(n) + " ONLY" : "";

    private static readonly string[] _ones =
        { "", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE", "TEN",
          "ELEVEN", "TWELVE", "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN", "SEVENTEEN", "EIGHTEEN", "NINETEEN" };
    private static readonly string[] _tens =
        { "", "", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY" };

    private static string TwoDigits(int n) =>
        n < 20 ? _ones[n] : (_tens[n / 10] + (n % 10 > 0 ? " " + _ones[n % 10] : "")).Trim();

    private static string ThreeDigits(int n)
    {
        var s = "";
        if (n >= 100) { s += _ones[n / 100] + " HUNDRED"; n %= 100; if (n > 0) s += " "; }
        if (n > 0) s += TwoDigits(n);
        return s;
    }

    private static string IndianWords(long n)
    {
        if (n <= 0) return "";
        var parts = new List<string>();
        long crore = n / 10000000; n %= 10000000;
        int lakh = (int)(n / 100000); n %= 100000;
        int thousand = (int)(n / 1000); n %= 1000;
        int hundred = (int)n;
        if (crore > 0)    parts.Add(IndianWords(crore) + " CRORE");
        if (lakh > 0)     parts.Add(ThreeDigits(lakh) + " LAKH");
        if (thousand > 0) parts.Add(ThreeDigits(thousand) + " THOUSAND");
        if (hundred > 0)  parts.Add(ThreeDigits(hundred));
        return string.Join(" ", parts);
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
        txtPaymentName.Text = realAgency;

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

    private void BuildDocx(string filePath, byte[]? letterhead, byte[]? background)
    {
        using var doc = new WordDocument();
        var sec = doc.AddSection();
        sec.PageSetup.Margins.All = 36;
        float pageW = sec.PageSetup.PageSize.Width - 72;

        float marginTop = sec.PageSetup.Margins.Top;
        float lhBottom = marginTop;
        if (letterhead != null)
        {
            var hp = sec.AddParagraph();
            hp.ParagraphFormat.HorizontalAlignment = DocAlign.Center;
            var pic = hp.AppendPicture(letterhead);
            if (pic.Width > pageW) { float r = pageW / pic.Width; pic.Width *= r; pic.Height *= r; }
            lhBottom = marginTop + pic.Height;
        }
        else
        {
            var p = sec.AddParagraph();
            p.ParagraphFormat.HorizontalAlignment = DocAlign.Center;
            var r = p.AppendText(txtAgencyName.Text.Trim());
            r.CharacterFormat.FontName = FontName; r.CharacterFormat.FontSize = 18; r.CharacterFormat.Bold = true;
            lhBottom = marginTop + 30f;
        }

        if (background != null)
        {
            try
            {
                var hpara = sec.HeadersFooters.Header.AddParagraph();
                var bgPic = hpara.AppendPicture(background);
                float pageH = sec.PageSetup.PageSize.Height;
                float bgTop = lhBottom + 8f;
                bgPic.TextWrappingStyle  = TextWrappingStyle.Behind;
                bgPic.HorizontalOrigin   = HorizontalOrigin.Page;
                bgPic.VerticalOrigin     = VerticalOrigin.Page;
                bgPic.HorizontalPosition = sec.PageSetup.Margins.Left;
                bgPic.VerticalPosition   = bgTop;
                bgPic.Width  = pageW;
                bgPic.Height = pageH - bgTop - sec.PageSetup.Margins.Bottom;
            }
            catch { }
        }

        var pay = string.IsNullOrWhiteSpace(txtPaymentName.Text) ? txtAgencyName.Text.Trim() : txtPaymentName.Text.Trim();
        float wl = pageW * 0.317f;
        float wd = pageW * 0.507f;
        float wa = pageW - wl - wd;

        var t = sec.AddTable();
        t.ResetCells(23, 3);
        t.TableFormat.Borders.BorderType = BorderStyle.Single;
        t.TableFormat.Borders.LineWidth = 0.5f;
        t.TableFormat.Borders.Color = SFColor.Black;
        t.TableFormat.Borders.Horizontal.BorderType = BorderStyle.Single;
        t.TableFormat.Borders.Vertical.BorderType = BorderStyle.Single;

        void W(int r) { t[r, 0].Width = wl; t[r, 1].Width = wd; t[r, 2].Width = wa; }
        int ri = 0;

        CellLines(t, ri, 0, new[] { $"To,  {txtBankTo.Text.Trim()},", "SUBJECT–SUBMISSION OF REPOSSESSION BILL." }, align: DocAlign.Center);
        t.ApplyHorizontalMerge(ri, 0, 2); ri++;

        CellLines(t, ri, 0, new[] { "INVOICE DATE -", "INVOICE NO-", "BRANCH-", "CONFIRMATION BY-" });
        CellLines(t, ri, 1, new[] { txtInvoiceDate.Text.Trim(), txtInvoiceNo.Text.Trim(), txtBranch.Text.Trim(), txtConfirmationBy.Text.Trim() });
        W(ri); ri++;

        CellText(t, ri, 0, "DESCRIPTION EXPENSE"); CellText(t, ri, 1, "ALL DETAILS"); CellText(t, ri, 2, "AMOUNT"); W(ri); ri++;

        void KV(string label, string val, string amt = "")
        {
            CellText(t, ri, 0, label); CellText(t, ri, 1, val); CellText(t, ri, 2, amt); W(ri); ri++;
        }
        KV("AGRI-LOAN NO", txtAgriLoan.Text.Trim());
        KV("NAME OF CUSTOMER", txtCustomer.Text.Trim());
        KV("MAKE-MODEL", txtMakeModel.Text.Trim());
        KV("RC NO", txtRcNo.Text.Trim());
        KV("DATE OF REPOSSESSION", txtDateRepo.Text.Trim());
        KV("PARKING YARD NAME", txtParkingYard.Text.Trim());
        KV("NAME OF AGNCY", txtAgencyName.Text.Trim());
        KV("ENCLOSED", txtEnclosed.Text.Trim());
        KV("QTY", txtQty.Text.Trim());
        KV("REPO CHARGES", txtRepoWords.Text.Trim(), Rs(ParseAmt(txtRepoAmount.Text)));
        KV("ADDITIONAL CHARGES", txtAddlCharges.Text.Trim());
        KV("PAN NO", txtPan.Text.Trim());
        KV("GST STATE", txtGst.Text.Trim());
        KV("BANK ACCOUNT NAME", txtAcHolder.Text.Trim());
        KV("ACCOUNT NO", txtAccountNo.Text.Trim());
        KV("IFSC CODE", txtIfsc.Text.Trim());
        KV("BRANCH", txtBankBranch.Text.Trim());
        KV("TOTAL GROSS AMOUNT", txtTotalWords.Text.Trim(), Rs(ParseAmt(txtTotalAmount.Text)));

        CellText(t, ri, 0, $"KINDIY RELEASE THE PAYMENT IN THE NAME OF M/S {pay}");
        t.ApplyHorizontalMerge(ri, 0, 2); ri++;

        CellLines(t, ri, 0, new[] { "", "", "", "", "Thank You", txtAgencyName.Text.Trim(), txtFooter.Text.Trim() }, align: DocAlign.Right);
        t.ApplyHorizontalMerge(ri, 0, 2);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        doc.Save(fs, FormatType.Docx);
    }

    private static void CellText(IWTable t, int row, int col, string text, bool bold = true, DocAlign align = DocAlign.Left)
        => CellLines(t, row, col, new[] { text }, bold, align);

    private static void CellLines(IWTable t, int row, int col, string[] lines, bool bold = true, DocAlign align = DocAlign.Left)
    {
        var cell = t[row, col];
        for (int k = 0; k < lines.Length; k++)
        {
            var p = (k == 0 && cell.Paragraphs.Count > 0) ? cell.Paragraphs[0] : cell.AddParagraph();
            p.ParagraphFormat.HorizontalAlignment = align;
            p.ParagraphFormat.AfterSpacing = 0f;
            var r = p.AppendText(lines[k] ?? "");
            r.CharacterFormat.FontName = FontName;
            r.CharacterFormat.FontSize = 9f;
            r.CharacterFormat.Bold = bold;
        }
    }
}
