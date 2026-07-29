using System;
using System.Globalization;
using System.Windows;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp.Accounts;

public partial class PaymentDetailsWindow : Window
{
    private readonly long _id;
    public bool Saved { get; private set; }

    internal PaymentDetailsWindow(DesktopApiClient.RepoSubmissionDto s)
    {
        InitializeComponent();
        dpDate.DisplayDateEnd = DateTime.Today;
        _id = s.Id;
        var veh = string.IsNullOrWhiteSpace(s.VehicleNo) ? s.ChassisNo : s.VehicleNo;
        txtSub.Text = $"{veh}  •  {s.CustomerName}  •  Agent: {s.AgentName}";

        txtUtr.Text        = s.UtrNo;
        txtBank.Text       = s.BankName;
        txtHolder.Text     = s.AcctHolderName;
        txtAccountNo.Text  = s.BankAccountNo;
        txtIfsc.Text       = s.IfscCode;
        txtAppCharges.Text = s.ApplicationCharges?.ToString("0.##") ?? "";
        if (DateTime.TryParse(s.PaymentDate, out var d)) dpDate.SelectedDate = d;
    }

    private static decimal? ParseAmt(string s)
        => decimal.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private void btnCancel_Click(object sender, RoutedEventArgs e) => Close();

    private async void btnSave_Click(object sender, RoutedEventArgs e)
    {
        btnSave.IsEnabled = false;
        txtErr.Text = "";
        try
        {
            await DesktopApiClient.UpdateAccountsPaymentAsync(_id, new
            {
                AcctHolderName = txtHolder.Text.Trim(),
                BankName = txtBank.Text.Trim(),
                BankAccountNo = txtAccountNo.Text.Trim(),
                IfscCode = txtIfsc.Text.Trim(),
                UtrNo = txtUtr.Text.Trim(),
                PaymentDate = dpDate.SelectedDate?.ToString("yyyy-MM-dd"),
                ApplicationCharges = ParseAmt(txtAppCharges.Text)
            });
            Saved = true;
            Close();
        }
        catch (Exception ex)
        {
            txtErr.Text = "Save failed: " + ex.Message;
            btnSave.IsEnabled = true;
        }
    }
}
