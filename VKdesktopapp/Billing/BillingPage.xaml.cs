using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Grid;
using CRMRSDesktopApp.Data;
using CRMRSDesktopApp.Models;

namespace CRMRSDesktopApp.Billing;

public partial class BillingPage : Page
{
    private readonly FinanceRepository _finances = new();
    private readonly VehicleSearchRepository _search = new();
    private List<VehicleSearchItem> _results = new();

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

        LoadSettingsIntoForm();

        try
        {
            var list = await _finances.GetFinancesAsync();
            cmbFinance.ItemsSource = list.Select(f => new FinanceOption { Id = f.Id, Name = f.Name })
                                         .OrderBy(f => f.Name).ToList();
        }
        catch (Exception ex)
        {
            txtSearchStatus.Text = "Could not load head offices: " + ex.Message;
        }
    }

    private void LoadSettingsIntoForm()
    {
        var s = BillingSettings.Load();
        var u = App.SignedAppUser;
        txtAgencyName.Text     = string.IsNullOrWhiteSpace(s.AgencyName) ? (u?.AgencyName ?? App.Firm.FirmName) : s.AgencyName;
        txtHeaderAddress.Text  = string.IsNullOrWhiteSpace(s.HeaderAddress) ? (u?.Address ?? App.Firm.Address) : s.HeaderAddress;
        txtHeaderContact.Text  = string.IsNullOrWhiteSpace(s.HeaderContact) ? (u?.Mobile1 ?? App.Firm.ContactNos) : s.HeaderContact;
        txtHeaderEmail.Text    = string.IsNullOrWhiteSpace(s.HeaderEmail) ? (u?.Email ?? "") : s.HeaderEmail;
        txtPan.Text            = s.PanNo;
        txtGst.Text            = s.GstState;
        txtBankAcName.Text     = s.BankAccountName;
        txtAccountNo.Text      = s.AccountNo;
        txtIfsc.Text           = s.IfscCode;
        txtBankBranch.Text     = s.BankBranch;
        txtParkingYard.Text    = s.ParkingYard;
        txtPaymentName.Text    = string.IsNullOrWhiteSpace(s.PaymentName) ? txtAgencyName.Text : s.PaymentName;
        txtFooter.Text         = s.FooterLine;
    }

    private void SaveSettingsFromForm()
    {
        new BillingSettings
        {
            AgencyName      = txtAgencyName.Text.Trim(),
            HeaderAddress   = txtHeaderAddress.Text.Trim(),
            HeaderContact   = txtHeaderContact.Text.Trim(),
            HeaderEmail     = txtHeaderEmail.Text.Trim(),
            PanNo           = txtPan.Text.Trim(),
            GstState        = txtGst.Text.Trim(),
            BankAccountName = txtBankAcName.Text.Trim(),
            AccountNo       = txtAccountNo.Text.Trim(),
            IfscCode        = txtIfsc.Text.Trim(),
            BankBranch      = txtBankBranch.Text.Trim(),
            ParkingYard     = txtParkingYard.Text.Trim(),
            PaymentName     = txtPaymentName.Text.Trim(),
            FooterLine      = txtFooter.Text.Trim()
        }.Save();
    }

    private void cmbFinance_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

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
        catch (Exception ex)
        {
            txtSearchStatus.Text = "Search failed: " + ex.Message;
        }
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

    private void btnGenerate_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromForm();

        var toFinance = (cmbFinance.SelectedItem as FinanceOption)?.Name ?? "";
        var safe = new string(txtRcNo.Text.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0) safe = "bill";

        var dlg = new SaveFileDialog
        {
            Title = "Save Repossession Bill",
            Filter = "PDF file (*.pdf)|*.pdf",
            FileName = $"RepoBill_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            BuildPdf(dlg.FileName, toFinance);
            txtGenStatus.Text = "Bill generated.";
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to generate: " + ex.Message, "Billing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BuildPdf(string filePath, string toFinance)
    {
        using var doc = new PdfDocument();
        doc.PageSettings.Margins.All = 28;
        var page = doc.Pages.Add();
        var g = page.Graphics;
        float w = page.GetClientSize().Width;

        var nameFont   = new PdfStandardFont(PdfFontFamily.Helvetica, 18, PdfFontStyle.Bold);
        var smallFont  = new PdfStandardFont(PdfFontFamily.Helvetica, 8);
        var boldFont   = new PdfStandardFont(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);

        float y = 0;
        void Centered(string text, PdfFont f)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            float tw = f.MeasureString(text).Width;
            g.DrawString(text, f, PdfBrushes.Black, new Syncfusion.Drawing.PointF((w - tw) / 2f, y));
            y += f.Height + 2;
        }

        Centered(txtAgencyName.Text.Trim(), nameFont);
        Centered(txtHeaderAddress.Text.Trim(), smallFont);
        Centered(txtHeaderContact.Text.Trim(), smallFont);
        Centered(txtHeaderEmail.Text.Trim(), smallFont);
        y += 4;
        g.DrawLine(new PdfPen(Syncfusion.Drawing.Color.FromArgb(255, 176, 0, 32), 1.2f),
            new Syncfusion.Drawing.PointF(0, y), new Syncfusion.Drawing.PointF(w, y));
        y += 8;
        Centered($"To,  {toFinance},", boldFont);
        Centered("SUBJECT–SUBMISSION OF REPOSSESSION BILL.", boldFont);
        y += 6;

        var rows = new List<(string label, string val, string amt, bool span)>
        {
            ("INVOICE DATE", txtInvoiceDate.Text.Trim(), "", false),
            ("INVOICE NO", txtInvoiceNo.Text.Trim(), "", false),
            ("BRANCH", txtBranch.Text.Trim(), "", false),
            ("CONFIRMATION BY", txtConfirmationBy.Text.Trim(), "", false),
            ("DESCRIPTION EXPENSE", "ALL DETAILS", "AMOUNT", false),
            ("AGRI-LOAN NO", txtAgriLoan.Text.Trim(), "", false),
            ("NAME OF CUSTOMER", txtCustomer.Text.Trim(), "", false),
            ("MAKE-MODEL", txtMakeModel.Text.Trim(), "", false),
            ("RC NO", txtRcNo.Text.Trim(), "", false),
            ("DATE OF REPOSSESSION", txtDateRepo.Text.Trim(), "", false),
            ("PARKING YARD NAME", txtParkingYard.Text.Trim(), "", false),
            ("NAME OF AGNCY", txtAgencyName.Text.Trim(), "", false),
            ("ENCLOSED", txtEnclosed.Text.Trim(), "", false),
            ("QTY", txtQty.Text.Trim(), "", false),
            ("REPO CHARGES", txtRepoWords.Text.Trim(), txtRepoAmount.Text.Trim(), false),
            ("ADDITIONAL CHARGES", txtAddlCharges.Text.Trim(), "", false),
            ("PAN NO", txtPan.Text.Trim(), "", false),
            ("GST STATE", txtGst.Text.Trim(), "", false),
            ("BANK ACCOUNT NAME", txtBankAcName.Text.Trim(), "", false),
            ("ACCOUNT NO", txtAccountNo.Text.Trim(), "", false),
            ("IFSC CODE", txtIfsc.Text.Trim(), "", false),
            ("BRANCH", txtBankBranch.Text.Trim(), "", false),
            ("TOTAL GROSS AMOUNT", txtTotalWords.Text.Trim(), txtTotalAmount.Text.Trim(), false),
            ($"KINDLY RELEASE THE PAYMENT IN THE NAME OF {txtPaymentName.Text.Trim()}", "", "", true),
        };

        var grid = new PdfGrid();
        grid.Style.Font = smallFont;
        grid.Columns.Add(3);
        grid.Columns[0].Width = w * 0.34f;
        grid.Columns[1].Width = w * 0.46f;
        grid.Columns[2].Width = w * 0.20f;

        foreach (var r in rows)
        {
            var row = grid.Rows.Add();
            row.Cells[0].Value = r.label;
            row.Cells[0].Style.Font = boldFont;
            if (r.span)
            {
                row.Cells[0].ColumnSpan = 3;
            }
            else
            {
                row.Cells[1].Value = r.val;
                row.Cells[2].Value = r.amt;
                row.Cells[2].Style.Font = boldFont;
            }
        }

        var result = grid.Draw(page, new Syncfusion.Drawing.RectangleF(0, y, w, page.GetClientSize().Height - y));
        float fy = (result?.Bounds.Bottom ?? y) + 20;

        void Right(string text, PdfFont f)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            float tw = f.MeasureString(text).Width;
            page.Graphics.DrawString(text, f, PdfBrushes.Black, new Syncfusion.Drawing.PointF(w - tw, fy));
            fy += f.Height + 2;
        }
        Right("Thank You", boldFont);
        Right(txtAgencyName.Text.Trim(), boldFont);
        Right(txtFooter.Text.Trim(), smallFont);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        doc.Save(fs);
    }
}
