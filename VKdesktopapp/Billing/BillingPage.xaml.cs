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

    private string SigningIdentity =>
        $"u{App.SignedAppUser?.AppUserId ?? 0}-m{_session?.MemberId ?? 0}";

    private void RefreshCertStatus()
    {
        if (!SigningCertificates.LoadSigningEnabled())
        {
            txtCertStatus.Text = "Signature off.";
            return;
        }
        var cert = SigningCertificates.Saved(SigningIdentity);
        if (cert == null)
        {
            txtCertStatus.Text = "Signature on, but no certificate chosen yet.";
            return;
        }
        txtCertStatus.Text = (cert.NotAfter < DateTime.Now ? "Expired: " : "Signing as ")
            + SigningCertificates.DisplayName(cert);
    }

    private void btnSignature_Click(object sender, RoutedEventArgs e)
    {
        var w = new SignatureWindow(SigningIdentity) { Owner = Window.GetWindow(this) };
        if (w.ShowDialog() != true) return;
        RefreshCertStatus();
        txtGenStatus.Foreground = System.Windows.Media.Brushes.Green;
        txtGenStatus.Text = "Signature settings saved.";
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
        catch (Exception ex) { MessageBox.Show("Upload failed: " + ex.Message, "Billing", MessageBoxButton.OK, MessageBoxButton.Error); }
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
        txtCollectionUpdate.Text = txtRemark.Text = txtBillingRemark.Text = "";
        _currentSubmissionId = 0;
        SetSubmissionFieldsReadOnly(false);
    }

    private bool _cameFromViewAll;

    private void btnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_cameFromViewAll)
        {
            _cameFromViewAll = false;
            OpenViewAll();
            return;
        }
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
    }

    private void btnViewAll_Click(object sender, RoutedEventArgs e) => OpenViewAll();

    private void OpenViewAll()
    {
        var allowed = _session?.FinanceIds
            ?? (cmbFinance.ItemsSource as IEnumerable<FinanceOption>)?.Select(f => f.Id).ToList()
            ?? new List<int>();
        var w = new ViewAllDetailsWindow(this, _session, allowed);
        w.ShowDialog();
    }

    internal async Task LoadSubmission(DesktopApiClient.RepoSubmissionDto s)
    {
        _cameFromViewAll = true;
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

        _currentSubmissionId = s.Id;
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
        txtBillingRemark.Text = s.BillingRemark;
        if (!string.IsNullOrWhiteSpace(s.ParkingYardName)) txtParkingYard.Text = Up(s.ParkingYardName);
        if (!string.IsNullOrWhiteSpace(s.AddlChargesNotes)) txtAddlCharges.Text = Up(s.AddlChargesNotes);
        if (s.AddlChargesAmount is decimal amt && amt > 0) txtAddlAmount.Text = amt.ToString("0.##");
        SetSubmissionFieldsReadOnly(false);
        txtGenStatus.Foreground = System.Windows.Media.Brushes.Green;
        txtGenStatus.Text = $"Loaded submission for {s.VehicleNo}. Edit any field, then generate.";
    }

    private void SetSubmissionFieldsReadOnly(bool ro)
    {
        var boxes = new[]
        {
            txtAgriLoan, txtCustomer, txtMakeModel, txtRcNo,
            txtConfirmationBy, txtConfirmationByMobile, txtAgentName, txtParkingYardMobile,
            txtLoadDetails, txtExecutiveName, txtCollectionUpdate, txtRemark, txtBillingRemark,
            txtParkingYard, txtAddlCharges, txtAddlAmount
        };
        foreach (var b in boxes)
        {
            b.IsReadOnly = ro;
            b.Background = ro ? System.Windows.Media.Brushes.WhiteSmoke : System.Windows.Media.Brushes.White;
        }
        txtBranch.IsReadOnly = ro;
        txtBranch.Background = ro ? System.Windows.Media.Brushes.WhiteSmoke : System.Windows.Media.Brushes.White;
    }

    private async Task SaveSubmissionEditsAsync(long submissionId)
    {
        decimal? addl = decimal.TryParse((txtAddlAmount.Text ?? "").Trim(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : (decimal?)null;

        await DesktopApiClient.UpdateSubmissionFieldsAsync(submissionId, new
        {
            LoanNo               = txtAgriLoan.Text.Trim(),
            CustomerName         = txtCustomer.Text.Trim(),
            Model                = txtMakeModel.Text.Trim(),
            VehicleNo            = txtRcNo.Text.Trim(),
            AgentName            = txtAgentName.Text.Trim(),
            ParkingYardName      = txtParkingYard.Text.Trim(),
            ParkingYardMobile    = txtParkingYardMobile.Text.Trim(),
            LoadDetails          = txtLoadDetails.Text.Trim(),
            AddlChargesNotes     = txtAddlCharges.Text.Trim(),
            AddlChargesAmount    = addl,
            ConfirmationByName   = txtConfirmationBy.Text.Trim(),
            ConfirmationByMobile = txtConfirmationByMobile.Text.Trim(),
            ExecutiveName        = txtExecutiveName.Text.Trim(),
            CollectionUpdate     = txtCollectionUpdate.Text.Trim(),
            Remark               = txtRemark.Text.Trim(),
            BillingRemark        = txtBillingRemark.Text.Trim()
        });
    }

    private void txtVehSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = DoSearchAsync(txtVehSearch.Text.Trim());
    }

    // ... remainder of file unchanged ...
}
