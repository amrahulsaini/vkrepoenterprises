using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using CRMRSDesktopApp.Data;
using SFColor = Syncfusion.Drawing.Color;
using DocAlign = Syncfusion.DocIO.DLS.HorizontalAlignment;

namespace CRMRSDesktopApp.Accounts;

public partial class AgentBillWindow : Window
{
    private readonly string _agent;
    private readonly List<DesktopApiClient.RepoSubmissionDto> _rows;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CRMRS");
    private static string LetterPath => Path.Combine(Dir, "accounts_letterhead.png");
    private static string BgPath     => Path.Combine(Dir, "accounts_background.png");

    internal AgentBillWindow(string agent, List<DesktopApiClient.RepoSubmissionDto> rows)
    {
        InitializeComponent();
        _agent = agent;
        _rows = rows;

        txtHead.Text = "Agent Bill — " + agent.ToUpperInvariant();
        txtSub.Text = $"{rows.Count} vehicle(s) for this agent";

        decimal repo = rows.Sum(r => r.RepoCharges ?? 0m);
        decimal adv  = rows.Sum(r => r.Advance ?? 0m);
        txtCount.Text   = $"Vehicles: {rows.Count}";
        txtRepoTot.Text = "Total Repo: " + repo.ToString("0.##");
        txtAdvTot.Text  = "Total Advance: " + adv.ToString("0.##");

        LoadPreview(imgLetter, LetterPath);
        LoadPreview(imgBg, BgPath);

        Loaded += async (_, __) => await LoadAgentBillingAsync();
    }

    private async Task LoadAgentBillingAsync()
    {
        try
        {
            var ab = await DesktopApiClient.GetAgentBillingAsync(_agent);
            if (ab == null) return;
            txtHolder.Text    = ab.AcctHolderName;
            txtBank.Text      = ab.BankName;
            txtAccountNo.Text = ab.BankAccountNo;
            txtIfsc.Text      = ab.IfscCode;
            if (ab.ApplicationCharges.HasValue) txtAppCharges.Text = ab.ApplicationCharges.Value.ToString("0.##");
        }
        catch { }
    }

    private static void LoadPreview(System.Windows.Controls.Image img, string path)
    {
        try
        {
            if (!File.Exists(path)) { img.Source = null; return; }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;
        }
        catch { img.Source = null; }
    }

    private void Pick(string dest, System.Windows.Controls.Image preview)
    {
        var dlg = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.Copy(dlg.FileName, dest, true);
            LoadPreview(preview, dest);
        }
        catch (Exception ex) { txtErr.Text = "Could not load image: " + ex.Message; }
    }

    private void btnLetter_Click(object sender, RoutedEventArgs e) => Pick(LetterPath, imgLetter);
    private void btnBg_Click(object sender, RoutedEventArgs e)     => Pick(BgPath, imgBg);
    private void btnLetterClear_Click(object sender, RoutedEventArgs e) { try { File.Delete(LetterPath); } catch { } imgLetter.Source = null; }
    private void btnBgClear_Click(object sender, RoutedEventArgs e)     { try { File.Delete(BgPath); } catch { } imgBg.Source = null; }

    private void btnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private static decimal? ParseAmt(string s)
        => decimal.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private async void btnGen_Click(object sender, RoutedEventArgs e)
    {
        txtErr.Text = "";
        var safe = new string(_agent.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0) safe = "agent";
        var dlg = new SaveFileDialog
        {
            Filter = "Word document (*.docx)|*.docx",
            FileName = $"AgentBill_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.docx"
        };
        if (dlg.ShowDialog() != true) return;

        // Persist the collective bank details + application charges for this agent.
        try
        {
            await DesktopApiClient.SaveAgentBillingAsync(new
            {
                AgentName = _agent,
                AcctHolderName = txtHolder.Text.Trim(),
                BankName = txtBank.Text.Trim(),
                BankAccountNo = txtAccountNo.Text.Trim(),
                IfscCode = txtIfsc.Text.Trim(),
                ApplicationCharges = ParseAmt(txtAppCharges.Text),
                LastInvoiceNo = 0
            });
        }
        catch { }

        try
        {
            byte[]? lh = File.Exists(LetterPath) ? File.ReadAllBytes(LetterPath) : null;
            byte[]? bg = File.Exists(BgPath) ? File.ReadAllBytes(BgPath) : null;
            BuildDocx(dlg.FileName, lh, bg);

            string? pdf = null;
            try
            {
                pdf = Path.ChangeExtension(dlg.FileName, ".pdf");
                using var inFs = new FileStream(dlg.FileName, FileMode.Open, FileAccess.Read);
                using var doc = new WordDocument(inFs, FormatType.Docx);
                using var render = new DocIORenderer();
                using var pdfDoc = render.ConvertToPDF(doc);
                using var fs = new FileStream(pdf, FileMode.Create, FileAccess.Write);
                pdfDoc.Save(fs);
            }
            catch { pdf = null; }

            Process.Start(new ProcessStartInfo(pdf ?? dlg.FileName) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex) { txtErr.Text = "Failed: " + ex.Message; }
    }

    private void BuildDocx(string filePath, byte[]? letterhead, byte[]? background)
    {
        using var doc = new WordDocument();
        var sec = doc.AddSection();
        // Landscape so the wider per-vehicle table (with make/model + parking yard) fits.
        sec.PageSetup.Orientation = PageOrientation.Landscape;
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

        var title = sec.AddParagraph();
        title.ParagraphFormat.HorizontalAlignment = DocAlign.Center;
        var tr = title.AppendText($"REPOSSESSION BILL — AGENT: {_agent.ToUpperInvariant()}");
        tr.CharacterFormat.Bold = true; tr.CharacterFormat.FontSize = 13;

        sec.AddParagraph();

        // Vehicle table
        var t = sec.AddTable();
        int rowsN = _rows.Count + 1;
        t.ResetCells(rowsN, 9);
        t.TableFormat.Borders.BorderType = BorderStyle.Single;
        t.TableFormat.Borders.LineWidth = 0.5f;
        t.TableFormat.Borders.Color = SFColor.Black;

        string[] heads = { "#", "VEHICLE NO", "MAKE/MODEL", "PARKING YARD", "REPO DATE", "PAYMENT DATE", "UTR NO", "REPO", "ADVANCE" };
        for (int c = 0; c < 9; c++) Cell(t, 0, c, heads[c], bold: true);

        decimal repoTot = 0m, advTot = 0m;
        for (int i = 0; i < _rows.Count; i++)
        {
            var s = _rows[i];
            var veh = string.IsNullOrWhiteSpace(s.VehicleNo) ? s.ChassisNo : s.VehicleNo;
            var repoDate = (s.CreatedAt ?? "").Length >= 10 ? s.CreatedAt.Substring(0, 10) : (s.CreatedAt ?? "");
            decimal repo = s.RepoCharges ?? 0m, adv = s.Advance ?? 0m;
            repoTot += repo; advTot += adv;
            Cell(t, i + 1, 0, (i + 1).ToString());
            Cell(t, i + 1, 1, veh);
            Cell(t, i + 1, 2, s.Model);
            Cell(t, i + 1, 3, s.ParkingYardName);
            Cell(t, i + 1, 4, repoDate);
            Cell(t, i + 1, 5, s.PaymentDate);
            Cell(t, i + 1, 6, s.UtrNo);
            Cell(t, i + 1, 7, repo.ToString("0.##"));
            Cell(t, i + 1, 8, adv.ToString("0.##"));
        }

        sec.AddParagraph();
        decimal appc = ParseAmt(txtAppCharges.Text) ?? 0m;
        decimal net = repoTot - advTot - appc;

        var tot = sec.AddTable();
        tot.ResetCells(4, 2);
        tot.TableFormat.Borders.BorderType = BorderStyle.Single;
        tot.TableFormat.Borders.LineWidth = 0.5f;
        tot.TableFormat.Borders.Color = SFColor.Black;
        TotRow(tot, 0, "TOTAL REPO CHARGES", repoTot.ToString("0.##"));
        TotRow(tot, 1, "TOTAL ADVANCE", advTot.ToString("0.##"));
        TotRow(tot, 2, "APPLICATION CHARGES", appc.ToString("0.##"));
        TotRow(tot, 3, "NET PAYABLE", net.ToString("0.##"), bold: true);

        sec.AddParagraph();
        var bankHead = sec.AddParagraph();
        var bh = bankHead.AppendText("RELEASE PAYMENT TO:");
        bh.CharacterFormat.Bold = true; bh.CharacterFormat.FontSize = 11;

        var bank = sec.AddTable();
        bank.ResetCells(4, 2);
        bank.TableFormat.Borders.BorderType = BorderStyle.Single;
        bank.TableFormat.Borders.LineWidth = 0.5f;
        bank.TableFormat.Borders.Color = SFColor.Black;
        BankRow(bank, 0, "ACCOUNT HOLDER NAME", txtHolder.Text.Trim());
        BankRow(bank, 1, "BANK NAME", txtBank.Text.Trim());
        BankRow(bank, 2, "ACCOUNT NUMBER", txtAccountNo.Text.Trim());
        BankRow(bank, 3, "IFSC CODE", txtIfsc.Text.Trim());

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        doc.Save(fs, FormatType.Docx);
    }

    private static void BankRow(IWTable t, int r, string label, string val)
    {
        var p0 = t[r, 0].AddParagraph();
        var l = p0.AppendText(label); l.CharacterFormat.Bold = true; l.CharacterFormat.FontSize = 10;
        var p1 = t[r, 1].AddParagraph();
        var a = p1.AppendText(val ?? ""); a.CharacterFormat.FontSize = 10;
    }

    private static void Cell(IWTable t, int r, int c, string text, bool bold = false)
    {
        var p = t[r, c].AddParagraph();
        var tr = p.AppendText(text ?? "");
        tr.CharacterFormat.FontSize = 9.5f;
        if (bold) tr.CharacterFormat.Bold = true;
    }

    private static void TotRow(IWTable t, int r, string label, string amt, bool bold = false)
    {
        var p0 = t[r, 0].AddParagraph();
        var l = p0.AppendText(label); l.CharacterFormat.Bold = true; l.CharacterFormat.FontSize = 10;
        var p1 = t[r, 1].AddParagraph();
        p1.ParagraphFormat.HorizontalAlignment = DocAlign.Right;
        var a = p1.AppendText("RS. " + amt + "/-"); a.CharacterFormat.FontSize = 10;
        if (bold) a.CharacterFormat.Bold = true;
    }
}
