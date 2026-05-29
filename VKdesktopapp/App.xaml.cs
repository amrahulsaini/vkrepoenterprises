using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Windows;
using Syncfusion.Licensing;
using VRASDesktopApp.Models;
using VRASDesktopApp.Properties;
using VRASDesktopApp.Data;

namespace VRASDesktopApp;

public partial class App : Application
{
    public static HttpClient HttpClient = null!;

    public static string ApiBaseUrl => Settings.Default.ApiBaseUrl;

    public static SignedAppUser? SignedAppUser { get; set; }

    public static Firm Firm => new Firm
    {
        FirmName = Settings.Default.FirmName,
        ContactNos = Settings.Default.ContactNos,
        Address = Settings.Default.Address,
        FeedbackPortalFirmId = Settings.Default.FeedbackPortalFirmId
    };

    public static string ApiKey => Settings.Default.ApiKey;

    /// <summary>
    /// Display name for report / PDF exports: the signed-in agency's name when
    /// running as a tenant, otherwise the product name.
    /// </summary>
    public static string BrandName =>
        (SignedAppUser?.IsAgency == true && !string.IsNullOrWhiteSpace(SignedAppUser.AgencyName))
            ? SignedAppUser!.AgencyName
            : "CRMS";

    public App()
    {
        // Load environment variables from project .env if present (for local dev)
        try
        {
            EnvLoader.LoadDotEnv();
        }
        catch { }

        // If the stored ApiKey is the old default, reset it to the current default
        if (Settings.Default.ApiKey == "vk@kunal.admin")
        {
            Settings.Default.ApiKey = "12";
            Settings.Default.Save();
        }

        // Per-user ApiBaseUrl normalization. Old user.config files variously
        // store the current API host with OR without a trailing slash, point
        // at retired domains (characterverse, 103.247.19.45), or are empty.
        // The login code does string concatenation (`ApiBaseUrl + "api/..."`),
        // so a missing trailing slash breaks every request with a
        // SocketException ("Cannot reach the server"). Force a clean, fresh
        // value whenever the saved one doesn't byte-for-byte match.
        const string CurrentApi = "https://api.crmrecoverysoftware.com/";
        if (!string.Equals(Settings.Default.ApiBaseUrl, CurrentApi, StringComparison.Ordinal))
        {
            Settings.Default.ApiBaseUrl = CurrentApi;
            Settings.Default.Save();
        }

        HttpClient = new HttpClient();
        HttpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
        // 5-minute ceiling — generous enough for big xlsx uploads (the
        // streaming /api/mgr/records/upload call hits this client), tight
        // enough that a broken connection doesn't hang the UI for
        // 20 minutes like the old default.
        HttpClient.Timeout = TimeSpan.FromMinutes(5);

        // Pre-warm the TLS connection to the API host while the login window
        // renders. The cold first call to a new HTTPS host costs ~300-500ms
        // for DNS + handshake; doing it in the background means the user's
        // first Sign-In click already has a hot socket and doesn't get a
        // spurious "Cannot reach the server" error.
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var _ = await HttpClient.GetAsync(Settings.Default.ApiBaseUrl, cts.Token);
            }
            catch { /* warmup is best-effort, real call will surface any error */ }
        });
        // Syncfusion license
        SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1NMaF5cXmBCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWXpedXRTRWheV0VxV0RWYUE=");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global handlers so crashes are logged and surfaced instead of silently exiting.
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object? sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            LogException(e.Exception);
            // If this is a corporate security policy (AppLocker / WDAC / Smart
            // App Control) blocking one of our unsigned DLLs, show a clear,
            // non-scary explanation instead of a raw .NET stack trace that
            // makes the app look crashed/broken to the agency's staff.
            if (IsBlockedByPolicy(e.Exception))
                ShowPolicyBlockMessage(e.Exception);
            else
                MessageBox.Show($"Unhandled UI exception:\n\n{e.Exception}", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
        catch { }
    }

    // True when the exception (or any inner exception) is Windows refusing to
    // load one of our binaries because of an Application Control policy.
    //   HRESULT 0x800711C7 = ERROR_FILE_BLOCKED_BY_POLICY
    // Surfaces as a FileLoadException whose message contains
    // "Application Control policy has blocked this file".
    public static bool IsBlockedByPolicy(Exception? ex)
    {
        const int ERROR_FILE_BLOCKED_BY_POLICY = unchecked((int)0x800711C7);
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur.HResult == ERROR_FILE_BLOCKED_BY_POLICY) return true;
            var m = cur.Message ?? "";
            if (m.Contains("Application Control policy", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("blocked this file", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Friendly, actionable message for a policy block. Names the blocked file
    // so support can tell the agency's IT exactly what to whitelist.
    public static void ShowPolicyBlockMessage(Exception? ex)
    {
        string blockedFile = "";
        if (ex is System.IO.FileLoadException fle && !string.IsNullOrEmpty(fle.FileName))
            blockedFile = fle.FileName;
        else
            for (var cur = ex; cur != null; cur = cur.InnerException)
                if (cur is System.IO.FileLoadException f && !string.IsNullOrEmpty(f.FileName))
                { blockedFile = f.FileName; break; }

        var detail = string.IsNullOrEmpty(blockedFile) ? "" : $"\n\nBlocked component:\n{blockedFile.Split(',')[0]}";
        MessageBox.Show(
            "This computer's security policy (Smart App Control / AppLocker / " +
            "WDAC) is blocking part of CRMRS from loading, so this screen can't open here." +
            detail +
            "\n\nHow to fix (any one):\n" +
            "  1.  Install CRMRS using the Setup.exe installer (installs into " +
            "Program Files, which corporate policy usually trusts) instead of " +
            "running the portable folder from Downloads/Desktop.\n" +
            "  2.  Ask your IT team to allow the CRMRS application / its folder.\n" +
            "  3.  On Windows 11 Home: Settings → Privacy & security → " +
            "Windows Security → App & browser control → Smart App Control → " +
            "turn Off (or set to Evaluation).\n\n" +
            "Contact support@crmrecoverysoftware.com if you need help.",
            "Blocked by your system security policy",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            LogException(e.ExceptionObject as Exception);
        }
        catch { }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            LogException(e.Exception);
            e.SetObserved();
        }
        catch { }
    }

    private static void LogException(Exception? ex)
    {
        try
        {
            var logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            System.IO.Directory.CreateDirectory(logDir);
            var logFile = System.IO.Path.Combine(logDir, $"error_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            System.IO.File.WriteAllText(logFile, ex?.ToString() ?? "(null)");
        }
        catch { }
        // Also capture into the central diagnostics log and report to the server,
        // so every failure (not just uploads) shows up in the manage Errors tab.
        try { if (ex != null) Data.Diagnostics.LogError("Unhandled exception", ex); } catch { }
    }

    public static DateTime GetDateTime_IN()
    {
        TimeZoneInfo destinationTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, destinationTimeZone);
    }

    public static string Reverse(string str)
    {
        char[] array = str.ToCharArray();
        Array.Reverse(array);
        return new string(array);
    }

    public static string GetFormatedVehicleNo(string str)
    {
        str = Regex.Replace(str, "[^A-Za-z0-9\\-]", "").ToUpper();
        string text = "";
        string[] array = Regex.Split(str, "(?<=\\D)(?=\\d)|(?<=\\d)(?=\\D)");
        if (array.Length == 1)
        {
            text = array[0];
        }
        else if (array.Length > 1)
        {
            for (int i = 0; i < array.Length; i++)
            {
                string text2 = array[i];
                text2 = text2.Trim('-');
                if (Regex.IsMatch(text2, "\\d"))
                {
                    text2 = ((i != array.Length - 1) ? text2.PadLeft(2, '0') : text2.PadLeft(4, '0'));
                    if (text2.Length > 4)
                    {
                        string text3 = text2.Substring(0, text2.Length - 4);
                        string text4 = text2.Substring(text2.Length - 4, 4);
                        text2 = text3 + "-" + text4;
                    }
                }
                text = ((i >= array.Length - 1) ? (text + text2) : (text + text2 + "-"));
            }
        }
        return text;
    }

    // Historically this function moved any characters AFTER the first 4-digit
    // run to the front, separated by '/'. That mangled Bharat-series numbers
    // ("22-BH-2271-E" → "-E/22-BH-2271") which broke both display and the
    // mobile-app regex match. The hyphen-formatted form already sorts
    // sensibly, so the function is now an identity passthrough.
    public static string GetVehicleNoInSearchableFormated(string vehicleNo)
    {
        return vehicleNo;
    }

    /// <summary>
    /// Adds or refreshes the Bearer token on the default HttpClient.
    /// </summary>
    public static void SetAuthToken(string token)
    {
        App.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
