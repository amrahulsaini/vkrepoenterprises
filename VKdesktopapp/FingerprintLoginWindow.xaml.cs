using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CRMRSDesktopApp.Data;

namespace CRMRSDesktopApp;

public partial class FingerprintLoginWindow : Window
{
    public string SignedInName { get; private set; } = "";
    public string SignedInMobile { get; private set; } = "";
    public string[] Modules { get; private set; } = System.Array.Empty<string>();
    public string Role { get; private set; } = "";

    private readonly string _mode;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private CancellationTokenSource _cts = new();
    private string _challengeId = "";
    private DateTime _expiresAt = DateTime.MinValue;
    private bool _busy;

    public FingerprintLoginWindow(string mode)
    {
        InitializeComponent();
        _mode = mode;
        lblAgency.Text = AgencyBranding.Name;
        imgAgency.Source = AgencyBranding.LoadLogo() ?? AgencyBranding.DefaultLogo();
        lblTitle.Text = string.IsNullOrWhiteSpace(mode) || mode == "CRMRS"
            ? "FINGERPRINT SIGN IN"
            : mode.ToUpperInvariant();
        _poll.Tick += async (_, __) => await PollAsync();
        Loaded += async (_, __) => await StartAsync();
        Closed += (_, __) => { _poll.Stop(); _cts.Cancel(); };
    }

    private async System.Threading.Tasks.Task StartAsync()
    {
        _poll.Stop();
        btnRetry.IsEnabled = false;
        lblStatus.Text = "Getting a code...";
        imgQr.Source = null;
        lblPair.Text = "--";

        try
        {
            var r = await DesktopApiClient.CreateAuthChallengeAsync(_mode, Environment.MachineName);
            _challengeId = r.Id;
            _expiresAt = DateTime.UtcNow.AddSeconds(r.ExpiresInSeconds);
            lblPair.Text = r.PairCode;
            imgQr.Source = FromDataUri(r.Qr);
            lblStatus.Text = "Scan with the CRMRS app on your phone.";
            _poll.Start();
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Could not start sign-in: " + ex.Message;
        }
        finally { btnRetry.IsEnabled = true; }
    }

    private static BitmapImage? FromDataUri(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri)) return null;
        int comma = dataUri.IndexOf(',');
        if (comma < 0) return null;
        var bytes = Convert.FromBase64String(dataUri.Substring(comma + 1));
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = new MemoryStream(bytes);
        img.EndInit();
        img.Freeze();
        return img;
    }

    private async System.Threading.Tasks.Task PollAsync()
    {
        if (_busy || _challengeId.Length == 0) return;
        _busy = true;
        try
        {
            var r = await DesktopApiClient.PollAuthChallengeAsync(_challengeId);
            switch (r.Status)
            {
                case "approved":
                    _poll.Stop();
                    SignedInName = r.Name ?? "";
                    SignedInMobile = "";
                    Modules = r.Modules ?? System.Array.Empty<string>();
                    Role = r.Role ?? "";
                    DialogResult = true;
                    Close();
                    return;

                case "scanned":
                    lblStatus.Text = "Phone connected. Confirm with your fingerprint.";
                    break;

                case "denied":
                    _poll.Stop();
                    lblStatus.Text = r.FailReason switch
                    {
                        "bad_signature" => "That fingerprint could not be verified. Get a new code and try again.",
                        "no_role"       => "No role has been assigned to this profile. Ask your administrator to set one in HRMS.",
                        _               => "That sign-in was refused. Get a new code and try again.",
                    };
                    break;

                case "expired":
                    _poll.Stop();
                    lblStatus.Text = "This code has expired. Get a new one.";
                    break;

                default:
                    var left = (int)Math.Max(0, (_expiresAt - DateTime.UtcNow).TotalSeconds);
                    lblStatus.Text = "Scan with the CRMRS app on your phone. (" + left + "s)";
                    if (left == 0)
                    {
                        _poll.Stop();
                        lblStatus.Text = "This code has expired. Get a new one.";
                    }
                    break;
            }
        }
        catch
        {
            lblStatus.Text = "Cannot reach the server. Retrying...";
        }
        finally { _busy = false; }
    }

    private async void btnRetry_Click(object sender, RoutedEventArgs e) => await StartAsync();

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
