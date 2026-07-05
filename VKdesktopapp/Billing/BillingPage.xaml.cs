using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private string _headerImagePath = "";

    private class FinanceOption { public int Id { get; set; } public string Name { get; set; } = ""; }

    public BillingPage()
    {
        InitializeComponent();
        Loaded += BillingPage_Loaded;
    }

    private async void BillingPage_Loaded(object sender, RoutedEventArgs e)
    {
        txtBillDate.Text = DateTime.Today.ToString("dd/MM/yyyy");
        txtRepoDate.Text = DateTime.Today.ToString("dd/MM/yyyy");

        LoadSettingsIntoForm();

        try
        {
            var list = await _finances.GetFinancesAsync();
            cmbFinance.ItemsSource = list.Select(f => new FinanceOption { Id = f.Id, Name = f.Name })
                                         .OrderBy(f => f.Name).ToList();
        }
        catch (Exception ex) { txtSearchStatus.Text = "Could not load head offices: " + ex.Message; }
    }

    private void LoadSettingsIntoForm()
    {
        var s = BillingSettings.Load();
        var u = App.SignedAppUser;

        txtAgencyName.Text    = string.IsNullOrWhiteSpace(s.AgencyName) ? (u?.AgencyName ?? App.Firm.FirmName) : s.AgencyName;
        txtAgencyAddress.Text = string.IsNullOrWhiteSpace(s.AgencyAddress) ? (u?.Address ?? App.Firm.Address) : s.AgencyAddress;
        txtState.Text         = s.State;
        txtPan.Text           = s.PanNo;
        txtVendorCode.Text    = s.VendorCode;
        txtSub.Text           = string.IsNullOrWhiteSpace(s.Sub) ? "Claim of Repossession Charges" : s.Sub;
        txtDescGoods.Text     = string.IsNullOrWhiteSpace(s.DescriptionGoods) ? "REPOSESSION CHARGES" : s.DescriptionGoods;
        txtHsn.Text           = s.HsnSac;
        txtBankName.Text       = s.BankName;
        txtBankBranch.Text     = s.BankBranch;
        txtAcHolder.Text       = string.IsNullOrWhiteSpace(s.AcHolderName) ? txtAgencyName.Text : s.AcHolderName;
        txtAccountNo.Text      = s.AccountNo;
        txtIfsc.Text           = s.IfscCode;

        _headerImagePath = s.HeaderImagePath;
        ShowHeaderPreview();
    }

    private void SaveSettingsFromForm()
    {
        new BillingSettings
        {
            AgencyName       = txtAgencyName.Text.Trim(),
            AgencyAddress    = txtAgencyAddress.Text.Trim(),
            State            = txtState.Text.Trim(),
            PanNo            = txtPan.Text.Trim(),
            VendorCode       = txtVendorCode.Text.Trim(),
            Sub              = txtSub.Text.Trim(),
            DescriptionGoods = txtDescGoods.Text.Trim(),
            HsnSac           = txtHsn.Text.Trim(),
            BankName         = txtBankName.Text.Trim(),
            BankBranch       = txtBankBranch.Text.Trim(),
            AcHolderName     = txtAcHolder.Text.Trim(),
            AccountNo        = txtAccountNo.Text.Trim(),
            IfscCode         = txtIfsc.Text.Trim(),
            HeaderImagePath  = _headerImagePath
        }.Save();
    }

    private void ShowHeaderPreview()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_headerImagePath) && File.Exists(_headerImagePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_headerImagePath);
                bmp.EndInit();
                imgHeader.Source = bmp;
            }
        }
        catch { }
    }

    private void btnHeaderImg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() != true) return;
        _headerImagePath = BillingSettings.CopyHeaderImage(dlg.FileName);
        ShowHeaderPreview();
        SaveSettingsFromForm();
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
        txtCustomer.Text = rec.CustomerName;
        txtRcNo.Text     = rec.VehicleNo;
        txtModel.Text    = rec.Model;
    }

    private void btnGenerate_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromForm();

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
            BuildDocx(dlg.FileName);
            txtGenStatus.Text = "Bill generated.";
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to generate: " + ex.Message, "Billing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private const string FontName = "Times New Roman";

    private void BuildDocx(string filePath)
    {
        using var doc = new WordDocument();
        var sec = doc.AddSection();
        sec.PageSetup.Margins.All = 40;
        float pageW = sec.PageSetup.PageSize.Width - 80;

        if (!string.IsNullOrWhiteSpace(_headerImagePath) && File.Exists(_headerImagePath))
        {
            var hp = sec.AddParagraph();
            hp.ParagraphFormat.HorizontalAlignment = DocAlign.Center;
            var pic = hp.AppendPicture(File.ReadAllBytes(_headerImagePath));
            if (pic.Width > pageW) { float r = pageW / pic.Width; pic.Width *= r; pic.Height *= r; }
        }
        else
        {
            Line(sec, txtAgencyName.Text.Trim(), bold: true, size: 16, align: DocAlign.Center);
        }

        var rule = sec.AddParagraph();
        rule.ParagraphFormat.Borders.Bottom.BorderType = BorderStyle.Single;
        rule.ParagraphFormat.Borders.Bottom.Color = SFColor.Red;
        rule.ParagraphFormat.Borders.Bottom.LineWidth = 1.5f;

        Line(sec, $"BILL DATE: {txtBillDate.Text.Trim()}", bold: true, align: DocAlign.Right);
        Blank(sec);

        Line(sec, txtBankTo.Text.Trim());
        foreach (var l in SplitLines(txtBankAddress.Text)) Line(sec, l);
        if (!string.IsNullOrWhiteSpace(txtStateCode.Text)) Line(sec, $"STATE CODE- {txtStateCode.Text.Trim()}");
        Blank(sec);
        Line(sec, $"From, Name: - {txtAgencyName.Text.Trim()}");
        Line(sec, $"Address: - {txtAgencyAddress.Text.Trim()}");
        Blank(sec);
        Line(sec, $"State:- {txtState.Text.Trim()}");
        Line(sec, $"Bill No: {txtBillNo.Text.Trim()}");
        Line(sec, $"PAN No : {txtPan.Text.Trim()}");
        Line(sec, $"Vendor code: - {txtVendorCode.Text.Trim()}");
        Line(sec, $"Sub: - {txtSub.Text.Trim()}");
        Blank(sec);
        Line(sec, "Dear Madam/ Sir, As per subject, here we are claim repossessed commercial Vehicle charges as per below mentioned customers.");
        Blank(sec);

        var t = sec.AddTable();
        t.ResetCells(4, 9);
        TableBorders(t);
        string[] hdr = { "Sr No", "Description of Goods", "HSN/SAC CODE", "APAC No", "Customer Name", "Vehicle No", "Model", "Repo Date", "Repo Charges (P.F)Amt" };
        for (int c = 0; c < hdr.Length; c++) Cell(t, 0, c, hdr[c], bold: true, center: true);
        Cell(t, 1, 0, "1", center: true);
        Cell(t, 1, 1, txtDescGoods.Text.Trim());
        Cell(t, 1, 2, txtHsn.Text.Trim(), center: true);
        Cell(t, 1, 3, txtApac.Text.Trim());
        Cell(t, 1, 4, txtCustomer.Text.Trim());
        Cell(t, 1, 5, txtRcNo.Text.Trim());
        Cell(t, 1, 6, txtModel.Text.Trim());
        Cell(t, 1, 7, txtRepoDate.Text.Trim(), center: true);
        Cell(t, 1, 8, txtRepoAmount.Text.Trim(), center: true);
        for (int c = 0; c < 9; c++) Cell(t, 2, c, "");
        Cell(t, 3, 7, "TOTAL", bold: true, center: true);
        Cell(t, 3, 8, string.IsNullOrWhiteSpace(txtTotalAmount.Text) ? txtRepoAmount.Text.Trim() : txtTotalAmount.Text.Trim(), bold: true, center: true);

        Blank(sec);

        var b = sec.AddTable();
        b.ResetCells(2, 5);
        TableBorders(b);
        string[] bh = { "Name of Bank", "Branch", "A/C Holder Name", "Account Number", "IFSC Code" };
        for (int c = 0; c < bh.Length; c++) Cell(b, 0, c, bh[c], bold: true, center: true);
        Cell(b, 1, 0, txtBankName.Text.Trim(), center: true);
        Cell(b, 1, 1, txtBankBranch.Text.Trim(), center: true);
        Cell(b, 1, 2, txtAcHolder.Text.Trim(), center: true);
        Cell(b, 1, 3, txtAccountNo.Text.Trim(), center: true);
        Cell(b, 1, 4, txtIfsc.Text.Trim(), center: true);

        Blank(sec);
        Blank(sec);
        Line(sec, "Thank You", align: DocAlign.Right);
        Line(sec, txtAgencyName.Text.Trim(), align: DocAlign.Right);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        doc.Save(fs, FormatType.Docx);
    }

    private static IEnumerable<string> SplitLines(string s) =>
        (s ?? "").Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0);

    private static void Line(IWSection sec, string text, bool bold = false, float size = 10.5f, DocAlign align = DocAlign.Left)
    {
        var p = sec.AddParagraph();
        p.ParagraphFormat.HorizontalAlignment = align;
        var r = p.AppendText(text ?? "");
        r.CharacterFormat.FontName = FontName;
        r.CharacterFormat.FontSize = size;
        r.CharacterFormat.Bold = bold;
    }

    private static void Blank(IWSection sec) => sec.AddParagraph();

    private static void Cell(IWTable t, int row, int col, string text, bool bold = false, bool center = false)
    {
        var p = t[row, col].AddParagraph();
        if (center) p.ParagraphFormat.HorizontalAlignment = DocAlign.Center;
        var r = p.AppendText(text ?? "");
        r.CharacterFormat.FontName = FontName;
        r.CharacterFormat.FontSize = 8.5f;
        r.CharacterFormat.Bold = bold;
    }

    private static void TableBorders(IWTable t)
    {
        t.TableFormat.Borders.BorderType = BorderStyle.Single;
        t.TableFormat.Borders.LineWidth = 0.5f;
        t.TableFormat.Borders.Color = SFColor.Black;
        t.TableFormat.Borders.Horizontal.BorderType = BorderStyle.Single;
        t.TableFormat.Borders.Vertical.BorderType = BorderStyle.Single;
    }
}
