using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Couriers;

public partial class CouriersPage : Page
{
    private List<Row> _rows = new();

    private class Row
    {
        public DesktopApiClient.RepoSubmissionDto Src { get; set; } = null!;
        public long Id => Src.Id;
        public string RepoDate => Src.CreatedAt;
        public string LoanNo => Src.LoanNo;
        public string InvoiceNo => Src.InvoiceNo;
        public string VehicleNo => Src.VehicleNo;
        public string CustomerName => Src.CustomerName;
        public string BranchName => (Src.BranchName ?? "").ToUpperInvariant();
        public string Model => Src.Model;
        public string ChassisNo => Src.ChassisNo;
        public string EngineNo => Src.EngineNo;
        public string AgentName => Src.AgentName;
        public string ParkingYardName => Src.ParkingYardName;
        public string ParkingYardMobile => Src.ParkingYardMobile;
        public string LoadDetails => Src.LoadDetails;
        public string AddlCharges => JoinParts(Src.AddlChargesNotes, Src.AddlChargesAmount?.ToString("0.##"));
        public string ConfirmationBy => JoinParts(Src.ConfirmationByName, Src.ConfirmationByMobile);
        public string ExecutiveName => Src.ExecutiveName;
        public string CollectionUpdate => Src.CollectionUpdate;
        public string FinanceName => (Src.FinanceName ?? "").ToUpperInvariant();
        public string ActionText => Src.BillingAction switch
        {
            "immediate"       => "OK for billing",
            "hold"            => "Hold for collection",
            "collection_done" => "Collection done",
            "cancel"          => "Cancel",
            _                 => Src.BillingAction
        };
        public string RepoChargesText => Src.RepoCharges?.ToString("0.##") ?? "";
        public string AdvanceText => Src.Advance?.ToString("0.##") ?? "";
        public string CourierYn => Src.CourierYn;
        public string BankerAddress => Src.BankerAddress;
        public string PodNumber => Src.PodNumber;

        /// Joins the paired fields the way the app's OK-for-repo message does,
        /// skipping whichever side is blank so no stray comma is left behind.
        private static string JoinParts(params string?[] parts)
            => string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
    }

    private class AdvRow : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public DateTime Date { get; set; }
        public string DateText => Date.ToString("dd-MM-yyyy");
        public string Note { get; set; } = "";

        private string _amountText = "";
        public string AmountText
        {
            get => _amountText;
            set { _amountText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmountText))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<AdvRow> _advances = new();

    private bool _ready;

    private static long RowIdOf(object? item) => item is Row r ? r.Id : 0;

    private long _lastRowId;

    /// Clicking the already-expanded row collapses it again.
    private void XlGrid_RowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as System.Windows.DependencyObject;
        while (src != null && src is not DataGridRow && src is not System.Windows.Controls.Primitives.DataGridColumnHeader)
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        if (src is not DataGridRow row) return;

        long id = RowIdOf(row.Item);
        if (id != 0 && id == _lastRowId && row.IsSelected)
        {
            row.IsSelected = false;
            _lastRowId = 0;
            e.Handled = true;
            return;
        }
        _lastRowId = id;
    }

    private static string Squash4(string? s) =>
        new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();


    public CouriersPage()
    {
        InitializeComponent();
        dpFrom.DisplayDateEnd = DateTime.Today;
        dpTo.DisplayDateEnd = DateTime.Today;
        dpFrom.SelectedDate = DateTime.Today.AddDays(-30);
        dpTo.SelectedDate = DateTime.Today;
        dpAdvDate.SelectedDate = DateTime.Today;
        lstAdvances.ItemsSource = _advances;
        Loaded += async (_, __) => { _ready = true; await LoadAsync(); };
    }

    // Instant filtering — no Load button; any filter/date change reloads.
    private async void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        long keepId = (grid.SelectedItem as Row)?.Id ?? 0L;
        btnRefresh.IsEnabled = false;
        try
        {
            await LoadAsync();
            if (keepId > 0)
            {
                var again = _rows.FirstOrDefault(x => x.Id == keepId);
                if (again != null) { grid.SelectedItem = again; grid.ScrollIntoView(again); }
            }
        }
        finally { btnRefresh.IsEnabled = true; }
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) await LoadAsync();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        txtStatus.Text = "Loading…";
        try
        {
            string? from = dpFrom.SelectedDate?.ToString("yyyy-MM-dd");
            string? to   = dpTo.SelectedDate?.ToString("yyyy-MM-dd");

            var data = await DesktopApiClient.GetRepoSubmissionsAsync(from, to, new List<int>(), null);

            if (cmbAction.SelectedIndex == 5)
            {
                data = data.Where(d => d.BillStatus == "billed").ToList();
            }
            else
            {
                string? actionFilter = cmbAction.SelectedIndex switch
                {
                    1 => "immediate",
                    2 => "hold",
                    3 => "collection_done",
                    4 => "cancel",
                    _ => null
                };
                if (actionFilter != null) data = data.Where(d => d.BillingAction == actionFilter).ToList();
            }

            _rows = data.Select(d => new Row { Src = d }).ToList();
            RefreshFilterLists();
            ApplyFilters();
        }
        catch (Exception ex) { txtStatus.Text = "Failed: " + ex.Message; }
    }

    private void RefreshFilterLists()
    {
        Fill(cmbFinance, _rows.Select(r => r.FinanceName));
        Fill(cmbAgent, _rows.Select(r => r.AgentName));

        static void Fill(ComboBox box, IEnumerable<string?> values)
        {
            var keep = box.Text;
            box.ItemsSource = values
                .Select(v => (v ?? "").Trim())
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
            box.Text = keep;
        }
    }

    private static List<Row> Narrow(List<Row> rows, string term, Func<Row, string?> field)
    {
        if (term.Length == 0) return rows;
        var exact = rows.Where(r => string.Equals((field(r) ?? "").Trim(), term, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count > 0
            ? exact
            : rows.Where(r => CRMRSDesktopApp.Billing.ViewAllDetailsWindow.NameMatches(field(r), term)).ToList();
    }

    private void ApplyFilters()
    {
        var shown = Narrow(_rows, (cmbFinance.Text ?? "").Trim(), r => r.FinanceName);
        shown = Narrow(shown, (cmbAgent.Text ?? "").Trim(), r => r.AgentName);

        var last4 = Squash4(txtRcLast4?.Text);
        if (last4.Length > 0)
            shown = shown.Where(r => Squash4(r.VehicleNo).Contains(last4) ||
                                     Squash4(r.ChassisNo).Contains(last4)).ToList();

        grid.ItemsSource = shown;
        txtStatus.Text = shown.Count == _rows.Count
            ? $"{shown.Count} record(s)."
            : $"{shown.Count} of {_rows.Count} record(s).";
    }

    private void RcLast4_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) ApplyFilters();
    }

    private void Finance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(new Action(ApplyFilters), System.Windows.Threading.DispatcherPriority.Input);
    }
    private void Finance_Key(object sender, KeyEventArgs e) { if (_ready) ApplyFilters(); }
    private void btnClearFinance_Click(object sender, RoutedEventArgs e) { cmbFinance.Text = ""; if (_ready) ApplyFilters(); }

    private void Agent_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(new Action(ApplyFilters), System.Windows.Threading.DispatcherPriority.Input);
    }
    private void Agent_Key(object sender, KeyEventArgs e) { if (_ready) ApplyFilters(); }
    private void btnClearAgent_Click(object sender, RoutedEventArgs e) { cmbAgent.Text = ""; if (_ready) ApplyFilters(); }

    private async void btnLoad_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (grid.SelectedItem is not Row r)
        {
            pnlForm.IsEnabled = false;
            btnSubmit.IsEnabled = false;
            btnClear.IsEnabled = false;
            btnDetails.IsEnabled = false;
            pnlBilled.Visibility = System.Windows.Visibility.Collapsed;
            txtSel.Text = "Select a record from the list.";
            return;
        }

        btnDetails.IsEnabled = true;

        var veh = string.IsNullOrWhiteSpace(r.VehicleNo) ? r.Src.ChassisNo : r.VehicleNo;
        txtSel.Text = $"{veh}  •  {r.CustomerName}  •  {r.FinanceName}";

        bool hasRealBill = !string.IsNullOrWhiteSpace(r.Src.InvoiceNo)
                        || !string.IsNullOrWhiteSpace(r.Src.BillUrl);
        if (hasRealBill)
        {
            pnlBilled.Visibility = System.Windows.Visibility.Visible;
            txtInvoice.Text = string.IsNullOrWhiteSpace(r.Src.InvoiceNo)
                ? "Bill generated." : "Invoice No: " + r.Src.InvoiceNo;
            btnDownloadBill.IsEnabled = !string.IsNullOrWhiteSpace(r.Src.BillUrl);
        }
        else pnlBilled.Visibility = System.Windows.Visibility.Collapsed;

        _suppressCalc = true;
        txtBillingStatus.Text = "Status: " + r.ActionText;
        txtRemark.Text = r.Src.Remark;
        txtGross.Text = r.Src.TotalGross?.ToString("0.##") ?? "";
        txtPercent.Text = r.Src.CourierPercent?.ToString("0.##") ?? "";
        txtRepoCharges.Text = r.Src.RepoCharges?.ToString("0.##") ?? "";
        cmbCourier.SelectedIndex = string.Equals(r.Src.CourierYn, "Yes", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        txtBankerAddress.Text = r.Src.BankerAddress;
        txtPod.Text = r.Src.PodNumber;
        _suppressCalc = false;

        ShowAppInfo(r);
        ConfigureForStatus(r.Src.BillingAction);
        _ = LoadAdvancesAsync(r);
        LoadScreenshot(r.Src.ScreenshotUrl);

        pnlForm.IsEnabled = true;
        btnSubmit.IsEnabled = true;
        btnClear.IsEnabled = true;
        txtFormStatus.Text = "";
    }

    private void ShowAppInfo(Row r)
    {
        var vis = System.Windows.Visibility.Visible;
        var gone = System.Windows.Visibility.Collapsed;
        bool showUpdate = r.Src.BillingAction is "hold" or "collection_done";
        lblCollectionUpdate.Visibility = showUpdate ? vis : gone;
        txtCollectionUpdate.Visibility = showUpdate ? vis : gone;
        txtCollectionUpdate.Text = r.Src.CollectionUpdate;

        lblAddlCharges.Visibility = showUpdate ? vis : gone;
        txtAddlChargesInfo.Visibility = showUpdate ? vis : gone;
        txtAddlChargesInfo.Text = r.AddlCharges;

        decimal cash = r.Src.CashAmount ?? 0m;
        bool showCash = r.Src.BillingAction == "collection_done" && cash > 0m;
        pnlCash.Visibility = showCash ? vis : gone;
        if (!showCash) return;

        txtCashPaid.Text = cash.ToString("0.##");
        decimal repo = ParseAmt(txtRepoCharges.Text) ?? 0m;
        decimal net = repo - cash;
        txtCashNote.Text = net < 0m
            ? $"Agent holds this cash. Against repo charges {repo:0.##}, the agent owes the agency {(-net):0.##}."
            : $"Agent holds this cash. Against repo charges {repo:0.##}, agency still owes {net:0.##}.";
    }

    private bool _suppressCalc;

    /// Percentage only applies to "OK for billing" (gross × %). Hold-for-collection
    /// disables charge entry entirely; Collection-done allows manual repo charges.
    private void ConfigureForStatus(string action)
    {
        bool ok = action == "immediate";
        bool chargesEditable = action is "immediate" or "hold" or "collection_done";

        var showIfOk = ok ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        lblPercent.Visibility = showIfOk;
        txtPercent.Visibility = showIfOk;
        lblGross.Visibility   = showIfOk;
        txtGross.Visibility   = showIfOk;

        txtPercent.IsReadOnly     = !ok;
        txtRepoCharges.IsReadOnly = !chargesEditable;
        pnlAddAdvance.IsEnabled   = chargesEditable;
        lstAdvances.IsEnabled     = chargesEditable;

        var disabled = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#F0F0F0")!;
        txtRepoCharges.Background = chargesEditable ? System.Windows.Media.Brushes.White : disabled;
    }

    private long _advLoadedFor;

    private async System.Threading.Tasks.Task LoadAdvancesAsync(Row r)
    {
        _advLoadedFor = r.Id;
        _suppressCalc = true;
        _advances.Clear();
        _suppressCalc = false;
        RecalcAdvance();

        List<DesktopApiClient.CourierAdvanceDto> list;
        try { list = await DesktopApiClient.GetCourierAdvancesAsync(r.Id); }
        catch { return; }
        if (_advLoadedFor != r.Id) return;

        _suppressCalc = true;
        if (list.Count == 0 && (r.Src.Advance ?? 0m) != 0m)
            _advances.Add(new AdvRow
            {
                Date = DateTime.TryParse(r.Src.CreatedAt, out var seeded) ? seeded.Date : DateTime.Today,
                AmountText = r.Src.Advance!.Value.ToString("0.##")
            });
        foreach (var a in list)
            _advances.Add(new AdvRow
            {
                Id = a.Id,
                Date = DateTime.TryParse(a.Date, out var d) ? d.Date : DateTime.Today,
                AmountText = a.Amount.ToString("0.##"),
                Note = a.Note ?? ""
            });
        _suppressCalc = false;
        RecalcAdvance();
    }

    private void RecalcAdvance()
    {
        var total = _advances.Sum(a => ParseAmt(a.AmountText) ?? 0m);
        txtAdvance.Text = total == 0m ? "" : total.ToString("0.##");
        txtNoAdvances.Visibility = _advances.Count == 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        UpdateFinal();
    }

    private void btnAddAdvance_Click(object sender, RoutedEventArgs e)
    {
        var amt = ParseAmt(txtAdvAmount.Text);
        if (amt is null || amt == 0m)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Enter the advance amount first.";
            return;
        }
        _advances.Add(new AdvRow { Date = dpAdvDate.SelectedDate ?? DateTime.Today, AmountText = amt.Value.ToString("0.##") });
        txtAdvAmount.Text = "";
        dpAdvDate.SelectedDate = DateTime.Today;
        txtFormStatus.Foreground = System.Windows.Media.Brushes.Gray;
        txtFormStatus.Text = "Advance added — press Submit to save it.";
        RecalcAdvance();
    }

    private void AdvAmount_Key(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) btnAddAdvance_Click(sender, new RoutedEventArgs());
    }

    private void btnRemoveAdvance_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AdvRow a) return;
        _advances.Remove(a);
        txtFormStatus.Foreground = System.Windows.Media.Brushes.Gray;
        txtFormStatus.Text = "Advance removed — press Submit to save it.";
        RecalcAdvance();
    }

    private void Advance_Edited(object sender, TextChangedEventArgs e)
    {
        if (_suppressCalc) return;
        Dispatcher.BeginInvoke(new Action(RecalcAdvance), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Calc_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressCalc) return;
        if (ReferenceEquals(sender, txtPercent))
        {
            var gross = ParseAmt(txtGross.Text);
            var pct   = ParseAmt(txtPercent.Text);
            if (gross.HasValue && pct.HasValue)
            {
                _suppressCalc = true;
                txtRepoCharges.Text = (gross.Value * pct.Value / 100m).ToString("0.##");
                _suppressCalc = false;
            }
        }
        UpdateFinal();
    }

    private void UpdateFinal()
    {
        var repo = ParseAmt(txtRepoCharges.Text) ?? 0m;
        var adv  = ParseAmt(txtAdvance.Text) ?? 0m;
        txtFinal.Text = (repo - adv).ToString("0.##");
        if (pnlCash.Visibility == System.Windows.Visibility.Visible &&
            grid.SelectedItem is Row sel)
        {
            decimal cash = sel.Src.CashAmount ?? 0m;
            decimal net = repo - cash;
            txtCashNote.Text = net < 0m
                ? $"Agent holds this cash. Against repo charges {repo:0.##}, the agent owes the agency {(-net):0.##}."
                : $"Agent holds this cash. Against repo charges {repo:0.##}, agency still owes {net:0.##}.";
        }
    }

    private string? _screenshotUrl;
    private async void LoadScreenshot(string? url)
    {
        _screenshotUrl = url;
        if (string.IsNullOrWhiteSpace(url))
        {
            lblScreenshot.Visibility = System.Windows.Visibility.Collapsed;
            pnlScreenshot.Visibility = System.Windows.Visibility.Collapsed;
            imgScreenshot.Source = null;
            return;
        }
        lblScreenshot.Visibility = System.Windows.Visibility.Visible;
        pnlScreenshot.Visibility = System.Windows.Visibility.Visible;
        try
        {
            var bytes = await App.HttpClient.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            imgScreenshot.Source = bmp;
        }
        catch { imgScreenshot.Source = null; }
    }

    private void imgScreenshot_Click(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_screenshotUrl)) return;
        try { Process.Start(new ProcessStartInfo(_screenshotUrl) { UseShellExecute = true }); } catch { }
    }

    private void btnDetails_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r) return;
        var s = r.Src;
        var veh = string.IsNullOrWhiteSpace(s.VehicleNo) ? s.ChassisNo : s.VehicleNo;
        // Same order the app sends in its OK-for-repo message.
        var rows = new (string, string)[]
        {
            ("Repo Date", s.CreatedAt),
            ("Loan No", s.LoanNo),
            ("Invoice No", s.InvoiceNo),
            ("Customer Name", s.CustomerName),
            ("Branch", s.BranchName),
            ("Vehicle No", s.VehicleNo),
            ("Model/Maker", s.Model),
            ("Chassis No", s.ChassisNo),
            ("Engine No", s.EngineNo),
            ("Agent Name", s.AgentName),
            ("Parking Yard Name", s.ParkingYardName),
            ("Parking Yard Mobile", s.ParkingYardMobile),
            ("Load Details", s.LoadDetails),
            ("Additional Charges Notes, Amount", r.AddlCharges),
            ("Confirmation By (Name, Mobile)", r.ConfirmationBy),
            ("Executive Name", s.ExecutiveName),
            ("Collection Update", s.CollectionUpdate),
            ("Remark", s.Remark),
            ("Finance", s.FinanceName),
            ("Repo Charges", s.RepoCharges?.ToString("0.##") ?? ""),
            ("Advance", s.Advance?.ToString("0.##") ?? ""),
            ("Courier", s.CourierYn),
            ("Banker Address", s.BankerAddress),
            ("POD Number", s.PodNumber),
            ("Submitted By", s.SubmittedByName),
        };
        var w = new Billing.VehicleDetailsWindow(veh + " all details", rows) { Owner = Window.GetWindow(this) };
        w.ShowDialog();
    }

    /// Fetches the bill and saves it locally rather than handing the URL to a
    /// browser, which blocks the download. The suggested name carries the
    /// vehicle, invoice and a timestamp so no two saves collide.
    private async void btnDownloadBill_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r || string.IsNullOrWhiteSpace(r.Src.BillUrl)) return;

        var ext = Path.GetExtension(new Uri(r.Src.BillUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".pdf";

        var veh = new string((string.IsNullOrWhiteSpace(r.VehicleNo) ? r.ChassisNo : r.VehicleNo)
            .Where(char.IsLetterOrDigit).ToArray());
        if (veh.Length == 0) veh = "bill";
        var inv = new string((r.InvoiceNo ?? "").Where(char.IsLetterOrDigit).ToArray());

        var name = $"RepoBill_{veh}"
                 + (inv.Length > 0 ? $"_INV{inv}" : "")
                 + $"_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";

        var dlg = new SaveFileDialog
        {
            Title = "Save bill",
            FileName = name,
            Filter = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "PDF document (*.pdf)|*.pdf|All files (*.*)|*.*"
                : "Word document (*.docx)|*.docx|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            btnDownloadBill.IsEnabled = false;
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Gray;
            txtFormStatus.Text = "Downloading bill…";

            var bytes = await App.HttpClient.GetByteArrayAsync(r.Src.BillUrl);
            await File.WriteAllBytesAsync(dlg.FileName, bytes);

            txtFormStatus.Foreground = System.Windows.Media.Brushes.Green;
            txtFormStatus.Text = "Bill saved.";
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Could not download the bill: " + ex.Message;
        }
        finally { btnDownloadBill.IsEnabled = true; }
    }

    private static decimal? ParseAmt(string s)
        => decimal.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private async void btnSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r) return;
        var courier = cmbCourier.SelectedIndex == 1 ? "Yes" : "No";

        await SaveAsync(r.Id, new
        {
            RepoCharges = ParseAmt(txtRepoCharges.Text),
            Advances = _advances
                .Where(a => (ParseAmt(a.AmountText) ?? 0m) != 0m)
                .Select(a => new
                {
                    Amount = ParseAmt(a.AmountText) ?? 0m,
                    Date = a.Date.ToString("yyyy-MM-dd"),
                    Note = a.Note
                }).ToList(),
            CourierYn = courier,
            BankerAddress = txtBankerAddress.Text.Trim(),
            PodNumber = txtPod.Text.Trim(),
            CourierPercent = ParseAmt(txtPercent.Text)
        }, "Saved.");
    }

    private async void btnClear_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r) return;
        if (MessageBox.Show("Clear this record's courier entries (Repo Charges, Advance, Courier, Banker Address, POD)?",
                "Couriers", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await SaveAsync(r.Id, new { ClearEntries = true }, "Entries cleared.");
    }

    private async System.Threading.Tasks.Task SaveAsync(long id, object dto, string okText)
    {
        try
        {
            btnSubmit.IsEnabled = false;
            btnClear.IsEnabled = false;
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Gray;
            txtFormStatus.Text = "Saving…";

            await DesktopApiClient.UpdateCourierSubmissionAsync(id, dto);

            txtFormStatus.Foreground = System.Windows.Media.Brushes.Green;
            txtFormStatus.Text = okText;
            await LoadAsync();

            // Keep the same record selected so it can be edited again right away.
            var again = _rows.FirstOrDefault(x => x.Id == id);
            if (again != null)
            {
                grid.SelectedItem = again;
                grid.ScrollIntoView(again);
            }
        }
        catch (Exception ex)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Failed: " + ex.Message;
        }
        finally { btnSubmit.IsEnabled = true; btnClear.IsEnabled = true; }
    }
}
