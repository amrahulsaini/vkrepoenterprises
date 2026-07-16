using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows;

namespace CRMRSDesktopApp.Billing;

public partial class CertPickerWindow : Window
{
    private class Row
    {
        public string Name { get; set; } = "";
        public string Issuer { get; set; } = "";
        public string Expiry { get; set; } = "";
        public string Status { get; set; } = "";
        public X509Certificate2 Cert { get; set; } = null!;
    }

    public string? SelectedThumbprint { get; private set; }
    public bool Cleared { get; private set; }

    public CertPickerWindow()
    {
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        var saved = SigningCertificates.SavedThumbprint();
        var rows = SigningCertificates.List().Select(c => new Row
        {
            Name   = SigningCertificates.DisplayName(c),
            Issuer = SigningCertificates.IssuerName(c),
            Expiry = c.NotAfter.ToString("dd MMM yyyy"),
            Status = c.NotAfter < DateTime.Now ? "EXPIRED" : "Valid",
            Cert   = c
        }).ToList();

        lst.ItemsSource = rows;
        if (rows.Count == 0)
            txtHint.Text = "No certificates found. Plug in the DSC token, install its driver, then press Refresh.";
        else
            txtHint.Text = $"{rows.Count} certificate(s) found.";

        if (!string.IsNullOrWhiteSpace(saved))
        {
            var match = rows.FirstOrDefault(r =>
                string.Equals(r.Cert.Thumbprint, saved, StringComparison.OrdinalIgnoreCase));
            if (match != null) lst.SelectedItem = match;
        }
    }

    private void btnRefresh_Click(object sender, RoutedEventArgs e) => Load();

    private void lst_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (lst.SelectedItem is Row) btnOk_Click(sender, e);
    }

    private void btnOk_Click(object sender, RoutedEventArgs e)
    {
        if (lst.SelectedItem is not Row r)
        {
            MessageBox.Show("Select a certificate first.", "Signing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (r.Cert.NotAfter < DateTime.Now &&
            MessageBox.Show("This certificate has expired. Bills signed with it will not validate.\n\nUse it anyway?",
                "Signing", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SelectedThumbprint = r.Cert.Thumbprint;
        DialogResult = true;
    }

    private void btnClear_Click(object sender, RoutedEventArgs e)
    {
        Cleared = true;
        SelectedThumbprint = null;
        DialogResult = true;
    }
}
