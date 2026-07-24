using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        public string BranchName => Src.BranchName;
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
        public string FinanceName => Src.FinanceName;
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

    public CouriersPage()
    {
        InitializeComponent();
        dpFrom.SelectedDate = DateTime.Today.AddDays(-30);
        dpTo.SelectedDate = DateTime.Today;
        Loaded += async (_, __) => await LoadAsync();
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
            grid.ItemsSource = _rows;
            txtStatus.Text = $"{_rows.Count} record(s).";
        }
        catch (Exception ex) { txtStatus.Text = "Failed: " + ex.Message; }
    }

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

        if (r.Src.BillStatus == "billed")
        {
            pnlBilled.Visibility = System.Windows.Visibility.Visible;
            txtInvoice.Text = string.IsNullOrWhiteSpace(r.Src.InvoiceNo)
                ? "Bill generated." : "Invoice No: " + r.Src.InvoiceNo;
            btnDownloadBill.IsEnabled = !string.IsNullOrWhiteSpace(r.Src.BillUrl);
        }
        else pnlBilled.Visibility = System.Windows.Visibility.Collapsed;
        txtRepoCharges.Text = r.Src.RepoCharges?.ToString("0.##") ?? "";
        txtAdvance.Text = r.Src.Advance?.ToString("0.##") ?? "";
        cmbCourier.SelectedIndex = string.Equals(r.Src.CourierYn, "Yes", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        txtBankerAddress.Text = r.Src.BankerAddress;
        txtPod.Text = r.Src.PodNumber;

        pnlForm.IsEnabled = true;
        btnSubmit.IsEnabled = true;
        btnClear.IsEnabled = true;
        txtFormStatus.Text = "";
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
            Advance = ParseAmt(txtAdvance.Text),
            CourierYn = courier,
            BankerAddress = txtBankerAddress.Text.Trim(),
            PodNumber = txtPod.Text.Trim()
        }, "Saved.");
    }

    private async void btnClear_Click(object sender, RoutedEventArgs e)
    {
        if (grid.SelectedItem is not Row r) return;
        if (MessageBox.Show("Clear this record's courier entries (Repo Charges, Advance, Courier, Banker Address, POD)?",
                "Couriers", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        await SaveAsync(r.Id, new
        {
            RepoCharges = (decimal?)null,
            Advance = (decimal?)null,
            CourierYn = (string?)null,
            BankerAddress = (string?)null,
            PodNumber = (string?)null
        }, "Entries cleared.");
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
        }
        catch (Exception ex)
        {
            txtFormStatus.Foreground = System.Windows.Media.Brushes.Firebrick;
            txtFormStatus.Text = "Failed: " + ex.Message;
        }
        finally { btnSubmit.IsEnabled = true; btnClear.IsEnabled = true; }
    }
}
