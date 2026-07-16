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
using System.Security.Cryptography.X509Certificates;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Security;
using CRMRSDesktopApp.Data;
using CRMRSDesktopApp.Models;
using SFColor = Syncfusion.Drawing.Color;
using SFRectF = Syncfusion.Drawing.RectangleF;
using DocAlign = Syncfusion.DocIO.DLS.HorizontalAlignment;

namespace CRMRSDesktopApp.Billing;

public partial class BillingPage : Page
{
    private readonly FinanceRepository _finances = new();
    private readonly VehicleSearchRepository _search = new();
    private List<VehicleSearchItem> _results = new();
    private string? _letterheadUrl;
    private string? _backgroundUrl;
    private int _financeId;
    private bool _searching;
    private bool _clearingSearch;
    private readonly BillingSession? _session;
    private long _currentSubmissionId;
    private string _realAgencyName = "";

    private class FinanceOption { public int Id { get; set; } public string Name { get; set; } = ""; }

    public BillingPage() : this(null) { }

    public BillingPage(BillingSession? session)
    {
        InitializeComponent();
        _session = session;
        Loaded += BillingPage_Loaded;
        txtRepoAmount.TextChanged += (_, __) => Recompute();
        txtAddlAmount.TextChanged += (_, __) => Recompute();
    }

    private void Recompute()
    {
        long repo  = ParseAmt(txtRepoAmount.Text);
        long addl  = ParseAmt(txtAddlAmount.Text);
        long total = repo + addl;
        txtRepoWords.Text   = Words(repo);
        txtTotalAmount.Text = Rs(total);
        txtTotalWords.Text  = Words(total);
    }

    private static string Up(string? s) => (s ?? "").ToUpperInvariant();

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

        _realAgencyName = (App.SignedAppUser?.IsAgency == true && !string.IsNullOrWhiteSpace(App.SignedAppUser.AgencyName))
            ? App.SignedAppUser!.AgencyName
            : App.Firm.FirmName;
        txtAgencyRealName.Text = (_realAgencyName ?? "").ToUpperInvariant();
        txtInvoiceNo.IsReadOnly = true;
        RefreshCertStatus();

        try
        {
            var list = await _finances.GetFinancesAsync();
            var opts = list.Select(f => new FinanceOption { Id = f.Id, Name = f.Name });
            if (_session != null)
                opts = opts.Where(o => _session.FinanceIds.Contains(o.Id));
            cmbFinance.ItemsSource = opts.OrderBy(f => f.Name).ToList();
            txtSearchStatus.Text = _session != null
                ? $"Signed in as {_session.MemberName}. Select a finance to begin."
                : "Select a finance to begin.";
        }
        catch (Exception ex) { txtSearchStatus.Text = "Could not load finances: " + ex.Message; }
    }

    private async Task LoadFinanceSettingsAsync()
    {
        _letterheadUrl = null;
        _backgroundUrl = null;
        ShowPreview(imgLetterhead, null);
        ShowPreview(imgBackground, null);
        txtPan.Text = txtGst.Text = txtAcHolder.Text = txtAccountNo.Text = "";
        txtIfsc.Text = txtBankBranch.Text = txtParkingYard.Text = txtFooter.Text = "";
        txtAgencyName.Text = txtPaymentName.Text = txtInvoiceNo.Text = "";
        try
        {
            var s = await DesktopApiClient.GetBillingSettingsAsync(_financeId);
            if (s != null)
            {
                txtAgencyName.Text = Up(s.VendorCode);
                txtPan.Text        = Up(s.PanNo);
                txtGst.Text        = Up(s.GstState);
                txtAcHolder.Text   = Up(s.BankAccountName);
                txtAccountNo.Text  = Up(s.AccountNo);
                txtIfsc.Text       = Up(s.IfscCode);
                txtBankBranch.Text = Up(s.BankBranch);
                txtParkingYard.Text = Up(s.ParkingYard);
                txtPaymentName.Text = Up(s.PaymentName);
                txtFooter.Text     = Up(s.FooterLine);
                txtInvoiceNo.Text  = s.NextInvoiceNo.ToString();
                _letterheadUrl = s.LetterheadUrl;
                _backgroundUrl = s.BackgroundUrl;
                ShowPreview(imgLetterhead, _letterheadUrl);
                ShowPreview(imgBackground, _backgroundUrl);
            }
        }
        catch (Exception ex) { txtSearchStatus.Text = "Could not load billing settings: " + ex.Message; }
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

    private void RefreshCertStatus()
    {
        var saved = SigningCertificates.SavedThumbprint();
        if (string.IsNullOrWhiteSpace(saved))
        {
            chkSign.IsChecked = false;
            txtCertStatus.Text = "No certificate selected — bill will be a Word file only.";
            return;
        }
        var cert = SigningCertificates.Find(saved);
        if (cert == null)
        {
            chkSign.IsChecked = false;
            txtCertStatus.Text = "Selected certificate not found — plug in the DSC token, then press Select Certificate.";
            return;
        }
        chkSign.IsChecked = true;
        var expired = cert.NotAfter < DateTime.Now;
        txtCertStatus.Text = (expired ? "EXPIRED: " : "Signing as: ")
            + SigningCertificates.DisplayName(cert)
            + "  (valid till " + cert.NotAfter.ToString("dd MMM yyyy") + ")";
    }

    private void chkSign_Changed(object sender, RoutedEventArgs e)
    {
        if (chkSign.IsChecked == true && SigningCertificates.Saved() == null)
        {
            chkSign.IsChecked = false;
            btnSignCert_Click(sender, e);
        }
    }

    private void btnSignCert_Click(object sender, RoutedEventArgs e)
    {
        var w = new CertPickerWindow { Owner = Window.GetWindow(this) };
        if (w.ShowDialog() != true) return;
        SigningCertificates.SaveThumbprint(w.Cleared ? null : w.SelectedThumbprint);
        RefreshCertStatus();
        txtGenStatus.Foreground = System.Windows.Media.Brushes.Green;
        txtGenStatus.Text = w.Cleared ? "Signing turned off." : "Signing certificate selected.";
    }

    private async void btnLetterhead_Click(object sender, RoutedEventArgs e) => await UploadImage("letterhead", imgLetterhead);
    private async void btnBackground_Click(object sender, RoutedEventArgs e) => await UploadImage("background", imgBackground);

    private async Task UploadImage(string kind, Image preview)
    {
        if (_financeId <= 0) { MessageBox.Show("Select a finance first.", "Billing", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dlg = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(dlg.FileName));
            var url = await DesktopApiClient.UploadBillingImageAsync(kind, b64, _financeId);
            if (kind == "letterhead") _letterheadUrl = url; else _backgroundUrl = url;
            ShowPreview(preview, url);
            txtGenStatus.Text = $"{kind} uploaded.";
        }
        catch (Exception ex) { MessageBox.Show("Upload failed: " + ex.Message, "Billing", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void cmbFinance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbFinance.SelectedItem is not FinanceOption f) return;
        _financeId = f.Id;
        txtBankTo.Text = f.Name;
        ResetVehicle();
        await LoadFinanceSettingsAsync();
        txtSearchStatus.Text = $"Finance: {f.Name}";
    }

    private void ResetVehicle()
    {
        _clearingSearch = true;
        txtVehSearch.Text = "";
        _clearingSearch = false;
        lstResults.ItemsSource = null;
        _results = new List<VehicleSearchItem>();
        txtAgriLoan.Text = txtCustomer.Text = txtMakeModel.Text = txtRcNo.Text = txtBranch.Text = "";
        txtAgentName.Text = txtParkingYardMobile.Text = txtLoadDetails.Text = "";
        txtConfirmationByMobile.Text = txtExecutiveName.Text = "";
        txtCollectionUpdate.Text = txtRemark.Text = "";
    }

    private void btnBack_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
    }

    private void btnViewAll_Click(object sender, RoutedEventArgs e)
    {
        var allowed = _session?.FinanceIds
            ?? (cmbFinance.ItemsSource as IEnumerable<FinanceOption>)?.Select(f => f.Id).ToList()
            ?? new List<int>();
        var w = new ViewAllDetailsWindow(this, _session, allowed);
        w.ShowDialog();
    }

    internal async Task LoadSubmission(DesktopApiClient.RepoSubmissionDto s)
    {
        _currentSubmissionId = s.Id;

        if (s.FinanceId is int fid && cmbFinance.ItemsSource is IEnumerable<FinanceOption> opts)
        {
            var match = opts.FirstOrDefault(o => o.Id == fid);
            if (match != null)
            {
                cmbFinance.SelectionChanged -= cmbFinance_SelectionChanged;
                cmbFinance.SelectedItem = match;
                _financeId = fid;
                txtBankTo.Text = match.Name;
                ResetVehicle();
                await LoadFinanceSettingsAsync();
                cmbFinance.SelectionChanged += cmbFinance_SelectionChanged;
            }
        }

        txtAgriLoan.Text  = Up(s.LoanNo);
        txtCustomer.Text  = Up(s.CustomerName);
        txtMakeModel.Text = Up(s.Model);
        txtRcNo.Text      = Up(s.VehicleNo);
        txtBranch.Text    = Up(s.BranchName);
        txtConfirmationBy.Text = Up(s.ConfirmationByName);
        txtConfirmationByMobile.Text = Up(s.ConfirmationByMobile);
        txtAgentName.Text = Up(s.AgentName);
        txtParkingYardMobile.Text = Up(s.ParkingYardMobile);
        txtLoadDetails.Text = Up(s.LoadDetails);
        txtExecutiveName.Text = Up(s.ExecutiveName);
        txtCollectionUpdate.Text = Up(s.CollectionUpdate);
        txtRemark.Text = Up(s.Remark);
        if (!string.IsNullOrWhiteSpace(s.ParkingYardName)) txtParkingYard.Text = Up(s.ParkingYardName);
        if (!string.IsNullOrWhiteSpace(s.AddlChargesNotes)) txtAddlCharges.Text = Up(s.AddlChargesNotes);
        if (s.AddlChargesAmount is decimal amt && amt > 0) txtAddlAmount.Text = amt.ToString("0.##");
        txtGenStatus.Foreground = System.Windows.Media.Brushes.Green;
        txtGenStatus.Text = $"Loaded submission for {s.VehicleNo}. Review and generate.";
    }

    private void txtVehSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = DoSearchAsync(txtVehSearch.Text.Trim());
    }

    private void txtVehSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_clearingSearch) return;
        var digits = new string(txtVehSearch.Text.Where(char.IsLetterOrDigit).ToArray());
        int need = rbChassis.IsChecked == true ? 5 : 4;
        if (digits.Length >= need) _ = DoSearchAsync(digits.Substring(digits.Length - need));
    }

    private async Task DoSearchAsync(string q)
    {
        if (_searching || q.Length == 0) return;
        if (_financeId <= 0) { txtSearchStatus.Text = "Select a finance first."; return; }
        _searching = true;
        _clearingSearch = true;
        txtVehSearch.Text = "";
        _clearingSearch = false;
        txtSearchStatus.Text = "Searching…";
        lstResults.ItemsSource = null;
        try
        {
            _results = rbChassis.IsChecked == true
                ? await _search.SearchByChassisLast5Async(q, _financeId)
                : await _search.SearchByRcLast4Async(q, _financeId);
            lstResults.ItemsSource = _results;
            txtSearchStatus.Text = $"{_results.Count} vehicle(s) found in this finance.";
        }
        catch (Exception ex) { txtSearchStatus.Text = "Search failed: " + ex.Message; }
        finally { _searching = false; }
    }

    private async void lstResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstResults.SelectedItem is not VehicleSearchItem sel) return;
        var rec = sel;
        if (long.TryParse(sel.Id, out var id))
        {
            try { rec = await _search.GetRecordByIdAsync(id) ?? sel; } catch { }
        }
        txtAgriLoan.Text  = Up(rec.AgreementNo);
        txtCustomer.Text  = Up(rec.CustomerName);
        txtMakeModel.Text = Up(rec.Model);
        txtRcNo.Text      = Up(rec.VehicleNo);
        txtBranch.Text    = Up(string.IsNullOrWhiteSpace(rec.BranchFromExcel) ? rec.BranchName : rec.BranchFromExcel);
    }

    private async void btnGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (_financeId <= 0) { MessageBox.Show("Select a finance first.", "Billing", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try
        {
            await DesktopApiClient.SaveBillingSettingsAsync(new
            {
                FinanceId = _financeId,
                AgencyName = _realAgencyName, VendorCode = txtAgencyName.Text.Trim(),
                PanNo = txtPan.Text.Trim(), GstState = txtGst.Text.Trim(),
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
            var assigned = await DesktopApiClient.CommitNextInvoiceNoAsync(_financeId);
            if (assigned > 0) txtInvoiceNo.Text = assigned.ToString();
        }
        catch (Exception ex) { MessageBox.Show("Could not assign invoice number: " + ex.Message, "Billing", MessageBoxButton.OK, MessageBoxImage.Warning); }

        try
        {
            var lh = await DownloadBytes(_letterheadUrl);
            var bg = await DownloadBytes(_backgroundUrl);
            var (pdfPath, signErr) = BuildDocx(dlg.FileName, lh, bg);
            txtGenStatus.Foreground = System.Windows.Media.Brushes.Green;
            txtGenStatus.Text = pdfPath != null ? "Bill generated + digitally signed." : "Bill generated.";
            Process.Start(new ProcessStartInfo(pdfPath ?? dlg.FileName) { UseShellExecute = true });
            if (signErr != null)
                MessageBox.Show("The Word bill was created, but the signed PDF could not be made:\n\n" + signErr,
                    "Billing", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (_currentSubmissionId > 0 && _session != null)
            {
                try { await DesktopApiClient.MarkSubmissionBilledAsync(_currentSubmissionId, _session.MemberId); } catch { }
                _currentSubmissionId = 0;
            }
            ResetVehicle();
            txtInvoiceNo.Text = int.TryParse(txtInvoiceNo.Text, out var last) ? (last + 1).ToString() : "";
            txtConfirmationBy.Text = "";
            txtRepoAmount.Text = txtRepoWords.Text = "";
            txtTotalAmount.Text = txtTotalWords.Text = "";
            txtAddlCharges.Text = "NA";
            txtAddlAmount.Text = "";
            txtSearchStatus.Text = "Ready for the next bill.";
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

    private (string? PdfPath, string? Error) BuildDocx(string filePath, byte[]? letterhead, byte[]? background)
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
            lhBottom = marginTop;
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
        var agencyAddr = (App.SignedAppUser?.IsAgency == true && !string.IsNullOrWhiteSpace(App.SignedAppUser.Address))
            ? App.SignedAppUser!.Address : App.Firm.Address;
        float wl = pageW * 0.317f;
        float wd = pageW * 0.507f;
        float wa = pageW - wl - wd;

        var t = sec.AddTable();
        t.ResetCells(25, 3);
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
        KV("VENDOR CODE", txtAgencyName.Text.Trim());
        KV("ENCLOSED", txtEnclosed.Text.Trim());
        KV("QTY", txtQty.Text.Trim());
        KV("REPO CHARGES", txtRepoWords.Text.Trim(), Rs(ParseAmt(txtRepoAmount.Text)));
        KV("ADDITIONAL CHARGES", txtAddlCharges.Text.Trim(), Rs(ParseAmt(txtAddlAmount.Text)));
        KV("COLLECTION UPDATE", txtCollectionUpdate.Text.Trim());
        KV("REMARK", txtRemark.Text.Trim());
        KV("PAN NO", txtPan.Text.Trim());
        KV("GST STATE", txtGst.Text.Trim());
        KV("BANK ACCOUNT NAME", txtAcHolder.Text.Trim());
        KV("ACCOUNT NO", txtAccountNo.Text.Trim());
        KV("IFSC CODE", txtIfsc.Text.Trim());
        KV("BRANCH", txtBankBranch.Text.Trim());
        KV("TOTAL GROSS AMOUNT", txtTotalWords.Text.Trim(), Rs(ParseAmt(txtTotalAmount.Text)));

        CellText(t, ri, 0, $"KINDIY RELEASE THE PAYMENT IN THE NAME OF M/S {pay}");
        t.ApplyHorizontalMerge(ri, 0, 2); ri++;

        var tyLines = new List<string> { "", "", "", "", "Thank You", Up(txtAgencyRealName.Text.Trim()) };
        if (!string.IsNullOrWhiteSpace(agencyAddr)) tyLines.Add(Up(agencyAddr.Trim()));
        if (!string.IsNullOrWhiteSpace(txtFooter.Text)) tyLines.Add(txtFooter.Text.Trim());
        CellLines(t, ri, 0, tyLines.ToArray(), align: DocAlign.Right);
        t.ApplyHorizontalMerge(ri, 0, 2);

        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            doc.Save(fs, FormatType.Docx);

        if (chkSign.IsChecked != true) return (null, null);

        var cert = SigningCertificates.Saved();
        if (cert == null) return (null, "No signing certificate is selected — plug in the DSC token and press Select Certificate.");

        var pdfPath = Path.ChangeExtension(filePath, ".pdf");
        var signer = Up(txtAgencyRealName.Text.Trim());

        try
        {
            RenderSignedPdf(doc, pdfPath, cert, signer, useToken: true);
            return (pdfPath, null);
        }
        catch (Exception token)
        {
            try
            {
                RenderSignedPdf(doc, pdfPath, cert, signer, useToken: false);
                return (pdfPath, null);
            }
            catch (Exception direct)
            {
                return (null, $"{token.Message}\n\n(fallback also failed: {direct.Message})");
            }
        }
    }

    private static void RenderSignedPdf(WordDocument doc, string pdfPath, X509Certificate2 cert, string fallbackName, bool useToken)
    {
        using var render = new DocIORenderer();
        using var pdf = render.ConvertToPDF(doc);

        var page = pdf.Pages[pdf.Pages.Count - 1];
        var size = page.GetClientSize();
        float w = 235f, h = 58f;
        var bounds = new SFRectF(size.Width - w - 20f, size.Height - h - 20f, w, h);

        var signature = useToken
            ? new PdfSignature(pdf, page, null, "BillSignature") { Bounds = bounds }
            : new PdfSignature(pdf, page, new PdfCertificate(cert), "BillSignature") { Bounds = bounds };

        signature.Settings.CryptographicStandard = CryptographicStandard.CADES;
        signature.Settings.DigestAlgorithm = DigestAlgorithm.SHA256;
        signature.Reason = "Repossession bill";

        var name = SigningCertificates.DisplayName(cert);
        if (string.IsNullOrWhiteSpace(name)) name = fallbackName;
        if (string.IsNullOrWhiteSpace(name)) name = "Authorised Signatory";

        var g = signature.Appearance.Normal.Graphics;
        var big = new PdfStandardFont(PdfFontFamily.TimesRoman, 11f, PdfFontStyle.Bold);
        var small = new PdfStandardFont(PdfFontFamily.TimesRoman, 6.5f);
        g.DrawString(name, big, PdfBrushes.Black, new SFRectF(0, 0, w * 0.42f, h));
        var now = DateTimeOffset.Now;
        var info = $"Digitally signed by {name}\nDate: {now:yyyy.MM.dd HH:mm:ss} {now:zzz}";
        g.DrawString(info, small, PdfBrushes.Black, new SFRectF(w * 0.42f + 4f, 4f, w * 0.58f - 4f, h - 8f));

        if (useToken)
            signature.AddExternalSigner(new TokenSigner(cert), SigningCertificates.ChainFor(cert), null);

        using var fs = new FileStream(pdfPath, FileMode.Create, FileAccess.ReadWrite);
        pdf.Save(fs);
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
