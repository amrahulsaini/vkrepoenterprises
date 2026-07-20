using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows;

namespace CRMRSDesktopApp.Billing;

public partial class SignatureWindow : Window
{
    private class Row
    {
        public string Name { get; set; } = "";
        public string Issuer { get; set; } = "";
        public string Expiry { get; set; } = "";
        public string Status { get; set; } = "";
        public X509Certificate2 Cert { get; set; } = null!;
    }

    private readonly string _identity;

    public SignatureWindow(string identity)
    {
        InitializeComponent();
        _identity = identity;
        chkSign.IsChecked = SigningCertificates.LoadSigningEnabled();
        LoadLayout();
        LoadCerts();
    }

    private void LoadLayout()
    {
        var (x, y, w, h) = SigningCertificates.LoadLayout();
        txtSigX.Text = x.ToString("0.##");
        txtSigY.Text = y.ToString("0.##");
        txtSigW.Text = w.ToString("0.##");
        txtSigH.Text = h.ToString("0.##");
    }

    private void LoadCerts()
    {
        var saved = SigningCertificates.SavedThumbprint(_identity);
        var rows = SigningCertificates.List().Select(c => new Row
        {
            Name   = SigningCertificates.DisplayName(c),
            Issuer = SigningCertificates.IssuerName(c),
            Expiry = c.NotAfter.ToString("dd MMM yyyy"),
            Status = !SigningCertificates.CanSign(c) ? "Cannot sign"
                   : c.NotAfter < DateTime.Now ? "Expired" : "Ready to sign",
            Cert   = c
        }).ToList();

        lst.ItemsSource = rows;
        txtHint.Text = rows.Count == 0
            ? "Nothing found. Plug in your signature token and press Refresh."
            : "Plug in your signature token and pick your name below.";

        if (!string.IsNullOrWhiteSpace(saved))
        {
            var match = rows.FirstOrDefault(r =>
                string.Equals(r.Cert.Thumbprint, saved, StringComparison.OrdinalIgnoreCase));
            if (match != null) lst.SelectedItem = match;
        }
    }

    private void btnRefresh_Click(object sender, RoutedEventArgs e) => LoadCerts();

    private void btnReset_Click(object sender, RoutedEventArgs e)
    {
        txtSigX.Text = SigningCertificates.DefaultX.ToString("0.##");
        txtSigY.Text = SigningCertificates.DefaultY.ToString("0.##");
        txtSigW.Text = SigningCertificates.DefaultW.ToString("0.##");
        txtSigH.Text = SigningCertificates.DefaultH.ToString("0.##");
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void btnSave_Click(object sender, RoutedEventArgs e)
    {
        var on = chkSign.IsChecked == true;
        var picked = lst.SelectedItem as Row;

        if (on && picked == null)
        {
            MessageBox.Show("Pick your name from the list first, or untick the box at the top.",
                "Digital Signature", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (on && picked!.Cert.NotAfter < DateTime.Now &&
            MessageBox.Show("This certificate has expired. Bills signed with it will not be accepted.\n\nUse it anyway?",
                "Digital Signature", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        float P(string s, float fallback) => float.TryParse(s?.Trim(), out var v) ? v : fallback;
        SigningCertificates.SaveLayout(
            P(txtSigX.Text, SigningCertificates.DefaultX),
            P(txtSigY.Text, SigningCertificates.DefaultY),
            Math.Max(40f, P(txtSigW.Text, SigningCertificates.DefaultW)),
            Math.Max(24f, P(txtSigH.Text, SigningCertificates.DefaultH)));

        SigningCertificates.SaveThumbprint(_identity, picked?.Cert.Thumbprint);
        SigningCertificates.SaveSigningEnabled(on);
        DialogResult = true;
    }
}
