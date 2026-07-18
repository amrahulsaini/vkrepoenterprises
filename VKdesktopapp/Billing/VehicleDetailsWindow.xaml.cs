using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CRMRSDesktopApp.Models;

namespace CRMRSDesktopApp.Billing;

public partial class VehicleDetailsWindow : Window
{
    private class Pair { public string Label { get; set; } = ""; public string Value { get; set; } = ""; }

    public VehicleDetailsWindow(string title, IEnumerable<(string Label, string Value)> rows)
    {
        InitializeComponent();
        lblTitle.Text = title;
        items.ItemsSource = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .Select(r => new Pair { Label = r.Label, Value = r.Value })
            .ToList();
    }

    public static VehicleDetailsWindow FromRecord(VehicleSearchItem r)
    {
        var title = !string.IsNullOrWhiteSpace(r.VehicleNo) ? r.VehicleNo
                  : !string.IsNullOrWhiteSpace(r.ChassisNo) ? r.ChassisNo : "Vehicle Details";
        var rows = new List<(string, string)>
        {
            ("Vehicle No", r.VehicleNo),
            ("Chassis No (VIN)", r.ChassisNo),
            ("Engine No", r.EngineNo),
            ("Model", r.Model),
            ("Customer Name", r.CustomerName),
            ("Customer Contacts", r.CustomerContactNos),
            ("Customer Address", r.CustomerAddress),
            ("Financer", r.Financer),
            ("Branch", string.IsNullOrWhiteSpace(r.BranchFromExcel) ? r.BranchName : r.BranchFromExcel),
            ("Executive", r.ExecutiveName),
            ("Address", r.Address),
            ("First Contact", r.FirstContactDetails),
            ("Second Contact", r.SecondContactDetails),
            ("Third Contact", r.ThirdContactDetails),
            ("Level 1", $"{r.Level1} {r.Level1ContactNos}".Trim()),
            ("Level 2", $"{r.Level2} {r.Level2ContactNos}".Trim()),
            ("Level 3", $"{r.Level3} {r.Level3ContactNos}".Trim()),
            ("Level 4", $"{r.Level4} {r.Level4ContactNos}".Trim()),
            ("Release Status", r.ReleaseStatus),
        };
        return new VehicleDetailsWindow(title, rows);
    }

    private void btnClose_Click(object sender, RoutedEventArgs e) => Close();
}
