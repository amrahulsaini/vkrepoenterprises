using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Accounts;

public partial class AccountsPage : Page
{
    private List<AcctRow> _all = new();
    private readonly ObservableCollection<AcctRow> _shown = new();
    private bool _ready;

    public AccountsPage()
    {
        InitializeComponent();
        grid.ItemsSource = _shown;
        dpFrom.DisplayDateEnd = DateTime.Today;
        dpTo.DisplayDateEnd = DateTime.Today;
        dpPayDate.DisplayDateEnd = DateTime.Today;
        dpFrom.SelectedDate = DateTime.Today.AddDays(-30);
        dpTo.SelectedDate = DateTime.Today;
        Loaded += async (_, __) => { _ready = true; await LoadAsync(); };
    }

    internal class AcctRow : INotifyPropertyChanged
    {
        internal DesktopApiClient.RepoSubmissionDto Src { get; init; } = null!;
        private decimal? _repo;
        private decimal? _adv;

        internal static AcctRow From(DesktopApiClient.RepoSubmissionDto d) =>
            new() { Src = d, _repo = d.RepoCharges, _adv = d.Advance };

        public long Id => Src.Id;
        public string RepoDate => Src.CreatedAt;
        public string AgentName
        {
            get
            {
                var a = (Src.AgentName ?? "").Trim();
                return a.Length > 0 ? a : (Src.SubmittedByName ?? "").Trim();
            }
        }
        public string VehicleNo => string.IsNullOrWhiteSpace(Src.VehicleNo) ? Src.ChassisNo : Src.VehicleNo;
        public string CustomerName => Src.CustomerName;
        public string FinanceName => (Src.FinanceName ?? "").ToUpperInvariant();
        public string BranchName => (Src.BranchName ?? "").ToUpperInvariant();
        public string ActionText => Src.BillingAction switch
        {
            "immediate"       => "OK for billing",
            "hold"            => "Hold for collection",
            "collection_done" => "Collection done",
            "cancel"          => "Cancel",
            _                 => Src.BillingAction
        };
        public string GrossText => Src.TotalGross?.ToString("0.##") ?? "";
        public string PercentText => Src.CourierPercent?.ToString("0.##") ?? "";
        public Visibility HasScreenshot =>
            string.IsNullOrWhiteSpace(Src.ScreenshotUrl) ? Visibility.Collapsed : Visibility.Visible;
        public string ScreenshotUrl => Src.ScreenshotUrl;

        public decimal? RepoCharges => _repo;
        public decimal? Advance => _adv;
        public string ChassisNo => Src.ChassisNo;
        public string LoanNo => Src.LoanNo;
        public string Model => Src.Model;
        public string ParkingYardName => Src.ParkingYardName;
        public string CollectionUpdate => Src.CollectionUpdate;
        public string Remark => Src.Remark;
        public string UtrNo => Src.UtrNo;
        public string PaymentDate => Src.PaymentDate;
        public decimal CashAmount => Src.CashAmount ?? 0m;
        public string CashText => (Src.CashAmount ?? 0m) == 0m ? "" : (Src.CashAmount ?? 0m).ToString("0.##");

        public string RepoChargesText
        {
            get => _repo?.ToString("0.##") ?? "";
            set { _repo = ParseAmt(value); Changed(nameof(RepoChargesText)); Changed(nameof(FinalText)); }
        }
        public string AdvanceText
        {
            get => _adv?.ToString("0.##") ?? "";
            set { _adv = ParseAmt(value); Changed(nameof(AdvanceText)); Changed(nameof(FinalText)); }
        }
        public string FinalText => ((_repo ?? 0m) - (_adv ?? 0m) - CashAmount).ToString("0.##");

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private static decimal? ParseAmt(string? s)
        => decimal.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private async System.Threading.Tasks.Task LoadAsync()
    {
        txtStatus.Text = "Loading…";
        try
        {
            string? from = dpFrom.SelectedDate?.ToString("yyyy-MM-dd");
            string? to   = dpTo.SelectedDate?.ToString("yyyy-MM-dd");
            var data = await DesktopApiClient.GetRepoSubmissionsAsync(from, to, new List<int>(), null);

            data = data.Where(d =>
                d.BillingAction is "hold" or "collection_done"
                || (d.BillingAction == "immediate"
                    && string.Equals(d.CourierYn, "Yes", StringComparison.OrdinalIgnoreCase))
            ).ToList();

            if (cmbAction.SelectedIndex == 4)
                data = data.Where(d => d.BillStatus == "billed").ToList();
            else
            {
                string? f = cmbAction.SelectedIndex switch
                { 1 => "immediate", 2 => "hold", 3 => "collection_done", _ => null };
                if (f != null) data = data.Where(d => d.BillingAction == f).ToList();
            }

            _all = data.Select(AcctRow.From).ToList();
            RefreshAgentList();
            ApplyFilter();
        }
        catch (Exception ex) { txtStatus.Text = "Failed: " + ex.Message; }
    }

    // Populate the agent dropdown with the distinct agent names, preserving what
    // the user has typed so the search box isn't disturbed on reload.
    private void RefreshAgentList()
    {
        var keep = cmbAgent.Text;
        var names = _all.Select(r => (r.AgentName ?? "").Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();
        cmbAgent.ItemsSource = names;
        cmbAgent.Text = keep;
    }

    private void ApplyFilter()
    {
        var term = (cmbAgent.Text ?? "").Trim();
        List<AcctRow> rows;
        if (term.Length == 0)
        {
            rows = _all;
        }
        else
        {
            // Prefer an exact agent match (so "J" doesn't also pull in "RAJA RAM");
            // fall back to contains for free-text discovery.
            var exact = _all.Where(r => string.Equals((r.AgentName ?? "").Trim(), term, StringComparison.OrdinalIgnoreCase)).ToList();
            rows = exact.Count > 0
                ? exact
                : _all.Where(r => CRMRSDesktopApp.Billing.ViewAllDetailsWindow.NameMatches(r.AgentName, term)).ToList();
        }

        _shown.Clear();
        foreach (var r in rows) _shown.Add(r);
        txtStatus.Text = $"{rows.Count} record(s).";
        BuildSummary(rows);
    }

    private void Agent_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(new Action(ApplyFilter), System.Windows.Threading.DispatcherPriority.Input);
    }
    private void Agent_Key(object sender, System.Windows.Input.KeyEventArgs e) { if (_ready) ApplyFilter(); }
    private void btnClearAgent_Click(object sender, RoutedEventArgs e) { cmbAgent.Text = ""; if (_ready) ApplyFilter(); }

    private async void btnRefresh_Click(object sender, RoutedEventArgs e)
    {
        long keepId = _selected?.Id ?? 0L;
        btnRefresh.IsEnabled = false;
        try
        {
            await LoadAsync();
            if (keepId > 0)
            {
                var again = _shown.FirstOrDefault(x => x.Id == keepId);
                if (again != null) { grid.SelectedItem = again; grid.ScrollIntoView(again); }
            }
        }
        finally { btnRefresh.IsEnabled = true; }
    }


    private void BuildSummary(List<AcctRow> rows)
    {
        txtGrandVehicles.Text = $"Vehicles: {rows.Count}";
        txtGrandRepo.Text  = "Total Repo: " + rows.Sum(x => x.RepoCharges ?? 0m).ToString("0.##");
        decimal cashTot = rows.Sum(x => x.CashAmount);
        decimal finalTot = rows.Sum(x => (x.RepoCharges ?? 0m) - (x.Advance ?? 0m) - x.CashAmount);
        txtGrandCash.Text = "Total Cash Collected: " + cashTot.ToString("0.##");
        txtGrandFinal.Text = (finalTot < 0m ? "Recoverable from agent: " : "Total Final: ") + finalTot.ToString("0.##");
        txtGrandFinal.Foreground = finalTot < 0m
            ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1565C0")!;
    }

    private async void Reload_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) await LoadAsync();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) ApplyFilter();
    }

    private async void grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not AcctRow r) return;
        // Let the binding push the new text into the VM first.
        await Dispatcher.BeginInvoke(new Action(async () => await SaveRow(r)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private async System.Threading.Tasks.Task SaveRow(AcctRow r)
    {
        try
        {
            await DesktopApiClient.UpdateCourierSubmissionAsync(r.Id, new
            {
                RepoCharges = r.RepoCharges,
                Advance = r.Advance,
                CourierYn = r.Src.CourierYn,
                BankerAddress = r.Src.BankerAddress,
                PodNumber = r.Src.PodNumber,
                CourierPercent = r.Src.CourierPercent
            });
            txtStatus.Text = "Saved.";
            BuildSummary(_shown.ToList());
        }
        catch (Exception ex) { txtStatus.Text = "Save failed: " + ex.Message; }
    }

    private AcctRow? _selected;

    private void grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = grid.SelectedItem as AcctRow;
        txtPayMsg.Text = "";
        if (_selected is not { } r)
        {
            pnlPay.IsEnabled = false;
            txtPaySel.Text = "Click a vehicle row to enter its payment details.";
            return;
        }
        var veh = string.IsNullOrWhiteSpace(r.VehicleNo) ? r.Src.ChassisNo : r.VehicleNo;
        txtPaySel.Text = $"{veh}  •  {r.CustomerName}  •  Agent: {r.AgentName}";
        txtUtr.Text = r.Src.UtrNo;
        dpPayDate.SelectedDate = DateTime.TryParse(r.Src.PaymentDate, out var d) ? d : (DateTime?)null;
        LoadCharges(r);
        pnlPay.IsEnabled = true;
    }

    private bool _suppressAcCalc;

    private void LoadCharges(AcctRow r)
    {
        var vis  = Visibility.Visible;
        var gone = Visibility.Collapsed;
        bool isOk   = r.Src.BillingAction == "immediate";
        bool isHold = r.Src.BillingAction is "hold" or "collection_done";

        txtChargesHead.Text = "CHARGES — " + r.ActionText.ToUpperInvariant();
        txtChargesMsg.Text = "";

        lblGross.Visibility    = isOk ? vis : gone;
        txtAcGross.Visibility  = isOk ? vis : gone;
        lblAcPercent.Visibility   = isOk ? vis : gone;
        txtAcPercent.Visibility   = isOk ? vis : gone;

        var addl = JoinAddl(r.Src.AddlChargesNotes, r.Src.AddlChargesAmount);
        lblAcAddl.Visibility   = isHold ? vis : gone;
        txtAcAddl.Visibility   = isHold ? vis : gone;

        _suppressAcCalc = true;
        txtAcGross.Text   = r.Src.TotalGross?.ToString("0.##") ?? "";
        txtAcAddl.Text    = addl;
        txtAcPercent.Text = r.Src.CourierPercent?.ToString("0.##") ?? "";
        txtAcRepo.Text    = r.RepoCharges?.ToString("0.##") ?? "";
        txtAcAdvance.Text = r.Advance?.ToString("0.##") ?? "";
        txtAcCash.Text    = r.CashAmount == 0m ? "" : r.CashAmount.ToString("0.##");
        _suppressAcCalc = false;

        bool showCash = r.Src.BillingAction == "collection_done" || r.CashAmount > 0m;
        lblAcCash.Visibility = showCash ? vis : gone;
        txtAcCash.Visibility = showCash ? vis : gone;

        UpdateAcFinal();
        LoadAcScreenshot(r.Src.ScreenshotUrl);
    }

    private string? _acShotUrl;

    private async void LoadAcScreenshot(string? url)
    {
        _acShotUrl = url;
        if (string.IsNullOrWhiteSpace(url))
        {
            lblAcShot.Visibility = Visibility.Collapsed;
            pnlAcShot.Visibility = Visibility.Collapsed;
            imgAcShot.Source = null;
            return;
        }
        lblAcShot.Visibility = Visibility.Visible;
        pnlAcShot.Visibility = Visibility.Visible;
        try
        {
            var bytes = await App.HttpClient.GetByteArrayAsync(url);
            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            imgAcShot.Source = bmp;
        }
        catch { imgAcShot.Source = null; }
    }

    private void imgAcShot_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_acShotUrl))
            try { Process.Start(new ProcessStartInfo(_acShotUrl) { UseShellExecute = true }); } catch { }
    }

    private static string JoinAddl(string? notes, decimal? amount)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(notes)) parts.Add(notes!.Trim());
        if (amount.HasValue && amount.Value != 0m) parts.Add(amount.Value.ToString("0.##"));
        return string.Join(", ", parts);
    }

    private void AcCalc_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressAcCalc) return;
        if (ReferenceEquals(sender, txtAcPercent))
        {
            var gross = ParseAmt(txtAcGross.Text);
            var pct   = ParseAmt(txtAcPercent.Text);
            if (gross.HasValue && pct.HasValue)
            {
                _suppressAcCalc = true;
                txtAcRepo.Text = (gross.Value * pct.Value / 100m).ToString("0.##");
                _suppressAcCalc = false;
            }
        }
        UpdateAcFinal();
    }

    private void UpdateAcFinal()
    {
        decimal repo = ParseAmt(txtAcRepo.Text) ?? 0m;
        decimal adv  = ParseAmt(txtAcAdvance.Text) ?? 0m;
        decimal cash = _selected?.CashAmount ?? 0m;
        decimal net  = repo - adv - cash;
        txtAcFinal.Text = cash > 0m
            ? $"Final: {repo:0.##} − {adv:0.##} − {cash:0.##} cash = {net:0.##}"
            : $"Final: {repo:0.##} − {adv:0.##} = {net:0.##}";
        txtAcFinal.Foreground = net < 0m
            ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1565C0")!;
    }

    private async void btnSaveCharges_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } r) return;
        btnSaveCharges.IsEnabled = false;
        txtChargesMsg.Foreground = System.Windows.Media.Brushes.Gray;
        txtChargesMsg.Text = "Saving…";
        try
        {
            await DesktopApiClient.UpdateCourierSubmissionAsync(r.Id, new
            {
                RepoCharges = ParseAmt(txtAcRepo.Text),
                Advance = ParseAmt(txtAcAdvance.Text),
                CourierYn = r.Src.CourierYn,
                BankerAddress = r.Src.BankerAddress,
                PodNumber = r.Src.PodNumber,
                CourierPercent = ParseAmt(txtAcPercent.Text)
            });
            long keepId = r.Id;
            await LoadAsync();
            var again = _shown.FirstOrDefault(x => x.Id == keepId);
            if (again != null) { grid.SelectedItem = again; grid.ScrollIntoView(again); }
            txtChargesMsg.Foreground = System.Windows.Media.Brushes.Green;
            txtChargesMsg.Text = "Charges saved.";
        }
        catch (Exception ex)
        {
            txtChargesMsg.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtChargesMsg.Text = "Save failed: " + ex.Message;
        }
        finally { btnSaveCharges.IsEnabled = true; }
    }

    private async void btnSavePay_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } r) return;
        btnSavePay.IsEnabled = false;
        txtPayMsg.Foreground = System.Windows.Media.Brushes.Gray;
        txtPayMsg.Text = "Saving…";
        try
        {
            await DesktopApiClient.UpdateAccountsPaymentAsync(r.Id, new
            {
                UtrNo = txtUtr.Text.Trim(),
                PaymentDate = dpPayDate.SelectedDate?.ToString("yyyy-MM-dd")
            });
            long keepId = r.Id;
            await LoadAsync();
            var again = _shown.FirstOrDefault(x => x.Id == keepId);
            if (again != null) { grid.SelectedItem = again; grid.ScrollIntoView(again); }
            txtPayMsg.Foreground = System.Windows.Media.Brushes.Green;
            txtPayMsg.Text = "Saved.";
        }
        catch (Exception ex)
        {
            txtPayMsg.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtPayMsg.Text = "Save failed: " + ex.Message;
        }
        finally { btnSavePay.IsEnabled = true; }
    }

    private void ViewScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AcctRow r && !string.IsNullOrWhiteSpace(r.ScreenshotUrl))
            try { Process.Start(new ProcessStartInfo(r.ScreenshotUrl) { UseShellExecute = true }); } catch { }
    }

    private void btnAgentBill_Click(object sender, RoutedEventArgs e)
    {
        var sel = grid.SelectedItem as AcctRow ?? _selected;
        string agent = (sel?.AgentName ?? "").Trim();
        if (agent.Length == 0)
        {
            var agents = _shown.Select(r => (r.AgentName ?? "").Trim())
                .Where(a => a.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (agents.Count == 1) agent = agents[0];
        }
        if (agent.Length == 0)
        {
            MessageBox.Show(
                "Click any vehicle row (or type an agent name) to choose the agent, " +
                "then Generate Agent Bill — it bills all that agent's vehicles for the selected dates.",
                "Agent Bill", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var rows = _all
            .Where(r => string.Equals((r.AgentName ?? "").Trim(), agent, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (rows.Count == 0) { txtStatus.Text = "No records to bill for " + agent + "."; return; }

        var w = new AgentBillWindow(agent, rows.Select(r => r.Src).ToList())
        { Owner = Window.GetWindow(this) };
        w.ShowDialog();
    }
}
