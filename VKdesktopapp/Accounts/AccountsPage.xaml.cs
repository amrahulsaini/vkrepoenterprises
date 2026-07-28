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
        public string AgentName => Src.AgentName;
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

            if (cmbAction.SelectedIndex == 5)
                data = data.Where(d => d.BillStatus == "billed").ToList();
            else
            {
                string? f = cmbAction.SelectedIndex switch
                { 1 => "immediate", 2 => "hold", 3 => "collection_done", 4 => "cancel", _ => null };
                if (f != null) data = data.Where(d => d.BillingAction == f).ToList();
            }

            _all = data.Select(AcctRow.From).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { txtStatus.Text = "Failed: " + ex.Message; }
    }

    private void ApplyFilter()
    {
        var term = (txtAgent.Text ?? "").Trim();
        var rows = string.IsNullOrEmpty(term)
            ? _all
            : _all.Where(r => (r.AgentName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

        _shown.Clear();
        foreach (var r in rows) _shown.Add(r);
        txtStatus.Text = $"{rows.Count} record(s).";
        BuildSummary(rows);
    }

    private void BuildSummary(List<AcctRow> rows)
    {
        var groups = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.AgentName) ? "(no agent)" : r.AgentName)
            .Select(g => new
            {
                Agent = g.Key,
                Count = g.Count(),
                RepoText = g.Sum(x => x.RepoCharges ?? 0m).ToString("0.##")
            })
            .OrderByDescending(x => x.Count)
            .ToList();
        gridSummary.ItemsSource = groups;

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

    private void grid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void ViewScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AcctRow r && !string.IsNullOrWhiteSpace(r.ScreenshotUrl))
            try { Process.Start(new ProcessStartInfo(r.ScreenshotUrl) { UseShellExecute = true }); } catch { }
    }

    private async void Payment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not AcctRow r) return;
        var w = new PaymentDetailsWindow(r.Src) { Owner = Window.GetWindow(this) };
        w.ShowDialog();
        if (w.Saved) await LoadAsync();
    }

    private void btnAgentBill_Click(object sender, RoutedEventArgs e)
    {
        var rows = _shown.ToList();
        if (rows.Count == 0) { txtStatus.Text = "No records to bill."; return; }

        var agents = rows.Select(r => (r.AgentName ?? "").Trim())
            .Where(a => a.Length > 0).Distinct().ToList();
        if (agents.Count != 1)
        {
            MessageBox.Show(
                "Filter to a single agent first (type the agent's name in the Agent box), " +
                "then generate the bill for all that agent's vehicles.",
                "Agent Bill", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var w = new AgentBillWindow(agents[0], rows.Select(r => r.Src).ToList())
        { Owner = Window.GetWindow(this) };
        w.ShowDialog();
    }
}
