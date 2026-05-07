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
    public static Task WarmUpTask { get; private set; } = Task.CompletedTask;

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

    public App()
    {
        // Load environment variables from project .env if present (for local dev)
        try
        {
            EnvLoader.LoadDotEnv();
        }
        catch { }

        HttpClient = new HttpClient();
        HttpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
        HttpClient.Timeout = TimeSpan.FromMinutes(20.0);
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

        // Pre-warm the MySQL connection pool. Stored so finance page can chain
        // its preload after warmup completes, reusing the warm socket instantly.
        WarmUpTask = Data.MySqlFactory.WarmUpAsync();
    }

    private void App_DispatcherUnhandledException(object? sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            LogException(e.Exception);
            MessageBox.Show($"Unhandled UI exception:\n\n{e.Exception}", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
        catch { }
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

    public static string GetVehicleNoInSearchableFormated(string vehicleNo)
    {
        var match = Regex.Match(vehicleNo, "\\d{4}");
        if (match.Success)
        {
            var index = match.Index;
            if (index + 4 < vehicleNo.Length)
            {
                var suffix = vehicleNo.Substring(index + 4);
                vehicleNo = suffix + "/" + vehicleNo.Substring(0, index + 4);
            }
        }
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
