using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Windows;
using Syncfusion.Licensing;
using VRASDesktopApp.Models;
using VRASDesktopApp.Properties;

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

    public App()
    {
        HttpClient = new HttpClient();
        HttpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
        HttpClient.Timeout = TimeSpan.FromMinutes(20.0);
        SyncfusionLicenseProvider.RegisterLicense("Mjc2NjM2NUAzMjMzMmUzMDJlMzBSTjlpNWtQSTFMQ1d3QTdLL2xCaDBnenUvK2tRV2VBSVZpKzRmOHg2THJvPQ==");
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

    /// <summary>
    /// Adds or refreshes the Bearer token on the default HttpClient.
    /// </summary>
    public static void SetAuthToken(string token)
    {
        App.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
