using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public partial class MyHrmsWindow : Window
{
    private DateTime _month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
    private DesktopApiClient.MeLeaves? _leaves;

    public sealed class DayRow
    {
        public string Date { get; init; } = "";
        public string Day { get; init; } = "";
        public string StatusText { get; init; } = "";
        public string CheckIn { get; init; } = "";
        public string CheckOut { get; init; } = "";
        public string Logins { get; init; } = "";
        public string Hours { get; init; } = "";
        public string LateText { get; init; } = "";
        public string Note { get; init; } = "";
    }

    public sealed class LeaveRow
    {
        public long Id { get; init; }
        public string Type { get; init; } = "";
        public string From { get; init; } = "";
        public string To { get; init; } = "";
        public string Days { get; init; } = "";
        public string StatusText { get; init; } = "";
        public string Reason { get; init; } = "";
        public bool CanCancel { get; init; }
    }

    public sealed class HolidayRow
    {
        public string Date { get; init; } = "";
        public string Day { get; init; } = "";
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "";
    }

    public sealed class TypeItem
    {
        public int Id { get; init; }
        public string Display { get; init; } = "";
    }

    public MyHrmsWindow()
    {
        InitializeComponent();
        dpFrom.SelectedDate = DateTime.Today;
        dpTo.SelectedDate = DateTime.Today;
        Loaded += async (_, __) =>
        {
            await LoadProfileAsync();
            await LoadMonthAsync();
            await LoadLeavesAsync();
            await LoadHolidaysAsync();
        };
    }

    private static string Or(string? v, string fallback = "Not set") =>
        string.IsNullOrWhiteSpace(v) ? fallback : v!;

    private static string PrettyDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "Not set";
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd MMM yyyy") : iso!;
    }

    private static string DayNames(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return "None";
        var names = new List<string>();
        foreach (var p in csv!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(p.Trim(), out int wd) && wd >= 0 && wd <= 6)
                names.Add(CultureInfo.CurrentCulture.DateTimeFormat.GetDayName((DayOfWeek)wd));
        return names.Count == 0 ? "None" : string.Join(", ", names);
    }

    private static string Hhmm(int minutes)
    {
        if (minutes <= 0) return "";
        return (minutes / 60) + "h " + (minutes % 60).ToString("00") + "m";
    }

    private async System.Threading.Tasks.Task LoadProfileAsync()
    {
        var p = await DesktopApiClient.MyProfileAsync();
        if (p == null)
        {
            lblName.Text = "Could not load your profile";
            lblSub.Text = "Check your connection and open this again.";
            return;
        }

        var parts = (p.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        lblInitials.Text = parts.Length == 0 ? "?"
            : (parts[0][0].ToString() + (parts.Length > 1 ? parts[^1][0].ToString() : "")).ToUpperInvariant();

        lblName.Text = Or(p.Name, "Unnamed");
        lblSub.Text = string.Join("  ·  ", new[] { Or(p.Designation, ""), Or(p.Department, ""), p.Mobile }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        lblHired.Text = PrettyDate(p.HiredOn);

        pHired.Text = PrettyDate(p.HiredOn);
        pDesig.Text = Or(p.Designation);
        pDept.Text = Or(p.Department);
        pType.Text = Or(p.EmploymentType).Replace('_', ' ');
        pRole.Text = Or(p.Role, "No role assigned");
        pShift.Text = string.IsNullOrWhiteSpace(p.ShiftStart)
            ? "Not set"
            : p.ShiftStart + " to " + p.ShiftEnd + "   (grace " + p.GraceMinutes + " min)";
        pOff.Text = DayNames(p.WeeklyOffs);

        pMobile.Text = Or(p.Mobile);
        pDob.Text = PrettyDate(string.IsNullOrWhiteSpace(p.DateOfBirth) ? p.KycDob : p.DateOfBirth);
        pBlood.Text = Or(p.BloodGroup);
        pAddr.Text = Or(p.Address) + (string.IsNullOrWhiteSpace(p.Pincode) ? "" : "  " + p.Pincode);
        pEmerg.Text = string.IsNullOrWhiteSpace(p.EmergencyName)
            ? "Not set"
            : p.EmergencyName + "  ·  " + p.EmergencyPhone;

        pKyc.Text = Or(p.KycStatus, "Not started");
        pKycName.Text = Or(p.KycName);
        pAadhaar.Text = string.IsNullOrWhiteSpace(p.KycAadhaarLast4)
            ? "Not set"
            : "XXXX XXXX " + p.KycAadhaarLast4 + (p.KycAadhaarVerified ? "   Verified" : "   Not verified");
        pPan.Text = string.IsNullOrWhiteSpace(p.KycPan)
            ? "Not set"
            : p.KycPan + (p.KycPanVerified ? "   Verified" : "   Not verified");
        pBank.Text = string.IsNullOrWhiteSpace(p.AccountNumber)
            ? "Not set"
            : p.AccountNumber + "   " + p.Ifsc + (p.KycBankVerified ? "   Verified" : "");
    }

    private void AddStat(Panel host, string number, string label, string colour)
    {
        var box = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)new BrushConverter().ConvertFrom("#FFE4E4E7")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 10, 0),
            MinWidth = 96,
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = number,
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFrom(colour)!,
        });
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFrom("#FF9A9AA3")!,
            Margin = new Thickness(0, 3, 0, 0),
        });
        box.Child = sp;
        host.Children.Add(box);
    }

    private async System.Threading.Tasks.Task LoadMonthAsync()
    {
        lblMonth.Text = _month.ToString("MMMM yyyy");
        btnNextMonth.IsEnabled = _month < new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

        var m = await DesktopApiClient.MyAttendanceAsync(_month.ToString("yyyy-MM"));
        pnlSummary.Children.Clear();
        if (m == null)
        {
            gridDays.ItemsSource = null;
            return;
        }

        lblMonth.Text = m.Label;
        var s = m.Summary;
        AddStat(pnlSummary, s.Present.ToString(), "PRESENT", "#FF0D0D0F");
        AddStat(pnlSummary, s.Halfday.ToString(), "HALF DAY", "#FF0D0D0F");
        AddStat(pnlSummary, s.Absent.ToString(), "ABSENT", "#FFB3261E");
        AddStat(pnlSummary, s.Leave.ToString(), "ON LEAVE", "#FFCC3C00");
        AddStat(pnlSummary, s.Weekoff.ToString(), "WEEK OFF", "#FF6B6B74");
        AddStat(pnlSummary, s.Holiday.ToString(), "HOLIDAY", "#FF6B6B74");
        AddStat(pnlSummary, s.Late.ToString(), "LATE", "#FFB3261E");
        AddStat(pnlSummary, Hhmm(s.WorkedMinutes), "HOURS WORKED", "#FFCC3C00");

        var rows = new List<DayRow>();
        foreach (var d in m.Days ?? Array.Empty<DesktopApiClient.MeDay>())
        {
            if (d.Status == "upcoming") continue;
            rows.Add(new DayRow
            {
                Date = PrettyDate(d.Date),
                Day = d.Day,
                StatusText = d.Status switch
                {
                    "present" => "Present",
                    "halfday" => "Half day",
                    "absent" => "Absent",
                    "leave" => "On leave",
                    "weekoff" => "Week off",
                    "holiday" => "Holiday",
                    _ => d.Status,
                },
                CheckIn = d.CheckIn,
                CheckOut = d.CheckOut,
                Logins = d.Logins > 0 ? d.Logins.ToString() : "",
                Hours = Hhmm(d.WorkedMinutes),
                LateText = d.LateMinutes > 0 ? d.LateMinutes + " min" : "",
                Note = d.Note,
            });
        }
        gridDays.ItemsSource = rows;
    }

    private async System.Threading.Tasks.Task LoadLeavesAsync()
    {
        _leaves = await DesktopApiClient.MyLeavesAsync();
        pnlBalances.Children.Clear();
        if (_leaves == null) return;

        cmbLeaveType.ItemsSource = (_leaves.Types ?? Array.Empty<DesktopApiClient.MeLeaveType>())
            .Select(t => new TypeItem { Id = t.Id, Display = t.Name + "  (" + t.Balance + " left)" })
            .ToList();
        if (cmbLeaveType.Items.Count > 0) cmbLeaveType.SelectedIndex = 0;

        foreach (var t in _leaves.Types ?? Array.Empty<DesktopApiClient.MeLeaveType>())
            AddStat(pnlBalances, t.Balance.ToString("0.#"), t.Code + " LEFT",
                    t.Balance <= 0 ? "#FFB3261E" : "#FFCC3C00");

        gridLeaves.ItemsSource = (_leaves.Requests ?? Array.Empty<DesktopApiClient.MeLeaveRequest>())
            .Select(r => new LeaveRow
            {
                Id = r.Id,
                Type = r.Type,
                From = PrettyDate(r.From),
                To = PrettyDate(r.To),
                Days = r.Days.ToString("0.#"),
                StatusText = r.Status switch
                {
                    "pending" => "Waiting",
                    "approved" => "Approved",
                    "rejected" => "Rejected",
                    "cancelled" => "Cancelled",
                    _ => r.Status,
                },
                Reason = string.IsNullOrWhiteSpace(r.DecisionNote)
                    ? r.Reason
                    : r.Reason + "   —   " + r.DecidedBy + ": " + r.DecisionNote,
                CanCancel = r.Status == "pending",
            }).ToList();
    }

    private async System.Threading.Tasks.Task LoadHolidaysAsync()
    {
        var h = await DesktopApiClient.MyHolidaysAsync(DateTime.Now.Year);
        if (h == null) return;
        gridHolidays.ItemsSource = (h.Holidays ?? Array.Empty<DesktopApiClient.MeHoliday>())
            .Select(x => new HolidayRow
            {
                Date = PrettyDate(x.Date),
                Day = x.Day,
                Name = x.Name,
                Kind = x.Optional ? "Optional" : "Public holiday",
            }).ToList();
    }

    private async void btnPrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        await LoadMonthAsync();
    }

    private async void btnNextMonth_Click(object sender, RoutedEventArgs e)
    {
        var cap = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        if (_month >= cap) return;
        _month = _month.AddMonths(1);
        await LoadMonthAsync();
    }

    private void Msg(string text, bool bad)
    {
        lblLeaveMsg.Text = text;
        lblLeaveMsg.Foreground = (Brush)new BrushConverter()
            .ConvertFrom(bad ? "#FFB3261E" : "#FFCC3C00")!;
    }

    private async void btnApply_Click(object sender, RoutedEventArgs e)
    {
        if (cmbLeaveType.SelectedItem is not TypeItem t) { Msg("Choose a leave type.", true); return; }
        if (dpFrom.SelectedDate is not DateTime from) { Msg("Choose the first day.", true); return; }
        if (dpTo.SelectedDate is not DateTime to) { Msg("Choose the last day.", true); return; }
        if (to < from) { Msg("The last day cannot be before the first.", true); return; }

        var reason = (txtReason.Text ?? "").Trim();
        if (reason.Length < 3) { Msg("Give a reason for the leave.", true); return; }

        btnApply.IsEnabled = false;
        Msg("Sending...", false);
        var (ok, err) = await DesktopApiClient.ApplyLeaveAsync(
            t.Id, from, to, chkHalf.IsChecked == true ? "first" : "none", reason);
        btnApply.IsEnabled = true;

        if (!ok) { Msg(err, true); return; }
        txtReason.Clear();
        chkHalf.IsChecked = false;
        Msg("Sent. Your administrator will see it in HRMS.", false);
        await LoadLeavesAsync();
    }

    private async void gridLeaves_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (gridLeaves.SelectedItem is not LeaveRow row || !row.CanCancel) return;
        gridLeaves.SelectedItem = null;

        var ask = MessageBox.Show(
            "Cancel this leave request?\n\n" + row.Type + "   " + row.From + " to " + row.To,
            "Leave", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;

        var (ok, err) = await DesktopApiClient.CancelLeaveAsync(row.Id);
        if (!ok) { MessageBox.Show(err, "Leave", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        await LoadLeavesAsync();
    }
}
