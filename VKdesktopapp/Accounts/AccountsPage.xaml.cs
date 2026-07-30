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
        public string FinanceName => Src.FinanceName;
        public string BranchName => Src.BranchName;
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
        public string FinalText => ((_repo ?? 0m) - (_adv ?? 0m)).ToString("0.##");

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
                : _all.Where(r => (r.AgentName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
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
        txtGrandFinal.Text = "Total Final: " + rows.Sum(x => (x.RepoCharges ?? 0m) - (x.Advance ?? 0m)).ToString("0.##");
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
        pnlPay.IsEnabled = true;
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
