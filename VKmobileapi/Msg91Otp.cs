using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace VKmobileapi;

public static class Msg91Otp
{
    private static readonly string AuthKey    = Environment.GetEnvironmentVariable("MSG91_AUTHKEY") ?? "";
    private static readonly string TemplateId = Environment.GetEnvironmentVariable("MSG91_TEMPLATE_ID") ?? "";

    private static readonly string DemoKey = Key(Environment.GetEnvironmentVariable("DEMO_MOBILE"));
    private static readonly string DemoOtp = (Environment.GetEnvironmentVariable("DEMO_OTP") ?? "123456").Trim();
    public static bool IsDemo(string? mobileRaw) => DemoKey.Length == 10 && Key(mobileRaw) == DemoKey;

    public static bool Configured => AuthKey.Length > 0 && TemplateId.Length > 0;

    public static bool Required =>
        Configured && (Environment.GetEnvironmentVariable("OTP_REQUIRED") ?? "1") != "0";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private sealed class Entry
    {
        public string   Otp      = "";
        public DateTime Expiry;
        public int      Attempts;
        public DateTime LastSent;
    }

    private static readonly ConcurrentDictionary<string, Entry>    _store    = new();
    private static readonly ConcurrentDictionary<string, DateTime> _verified = new();

    private static readonly TimeSpan OtpTtl       = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan VerifiedTtl  = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResendWindow = TimeSpan.FromSeconds(30);

    private static readonly string[] OtpVarKeys =
    {
        "otp", "OTP", "Otp", "code", "CODE", "Code", "var", "VAR", "var1", "VAR1",
        "var2", "VAR2", "otpcode", "otp_code", "verification_code", "verificationcode",
        "value", "pin", "PIN", "number"
    };

    public static string Key(string? mobile)
    {
        var d = new string((mobile ?? "").Where(char.IsDigit).ToArray());
        if (d.Length == 12 && d.StartsWith("91")) d = d[2..];
        else if (d.Length == 11 && d.StartsWith("0")) d = d[1..];
        return d;
    }

    private static string ToMsg91Mobile(string key10) => "91" + key10;

    private static string NewOtp() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static async Task<(bool ok, string message)> SendAsync(string? mobileRaw)
    {
        var key = Key(mobileRaw);
        if (key.Length != 10) return (false, "Enter a valid 10-digit mobile number.");
        if (IsDemo(mobileRaw))
        {
            _store[key] = new Entry { Otp = DemoOtp, Expiry = DateTime.UtcNow + OtpTtl, Attempts = 0, LastSent = DateTime.UtcNow };
            return (true, "OTP sent.");
        }
        if (!Configured)      return (false, "OTP service is not configured on the server.");

        if (_store.TryGetValue(key, out var prev) && DateTime.UtcNow - prev.LastSent < ResendWindow)
            return (false, "Please wait a few seconds before requesting another OTP.");

        var otp = NewOtp();
        var recipient = new Dictionary<string, object?> { ["mobiles"] = ToMsg91Mobile(key) };
        foreach (var k in OtpVarKeys) recipient[k] = otp;
        var body = new Dictionary<string, object?>
        {
            ["template_id"] = TemplateId,
            ["recipients"]  = new[] { recipient },
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://control.msg91.com/api/v5/flow");
            req.Headers.TryAddWithoutValidation("authkey", AuthKey);
            req.Headers.TryAddWithoutValidation("accept", "application/json");
            req.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            bool ok = resp.IsSuccessStatusCode &&
                      text.Contains("\"type\":\"success\"", StringComparison.OrdinalIgnoreCase);
            if (!ok) return (false, "Could not send OTP. Please try again.");

            _store[key] = new Entry { Otp = otp, Expiry = DateTime.UtcNow + OtpTtl, Attempts = 0, LastSent = DateTime.UtcNow };
            return (true, "OTP sent.");
        }
        catch
        {
            return (false, "Could not reach the OTP service. Please try again.");
        }
    }

    public static (bool ok, string message) Verify(string? mobileRaw, string? otp)
    {
        var key  = Key(mobileRaw);
        var code = new string((otp ?? "").Where(char.IsDigit).ToArray());
        if (key.Length != 10) return (false, "Enter a valid 10-digit mobile number.");
        if (code.Length == 0) return (false, "Enter the OTP.");
        if (IsDemo(mobileRaw))
        {
            if (code == DemoOtp) { _verified[key] = DateTime.UtcNow + VerifiedTtl; return (true, "Verified."); }
            return (false, "Incorrect OTP. Please try again.");
        }
        if (!_store.TryGetValue(key, out var e)) return (false, "Please request an OTP first.");
        if (DateTime.UtcNow > e.Expiry) { _store.TryRemove(key, out _); return (false, "OTP expired. Please request a new one."); }
        if (e.Attempts >= 5)            { _store.TryRemove(key, out _); return (false, "Too many attempts. Please request a new OTP."); }
        e.Attempts++;

        var a = Encoding.UTF8.GetBytes(code);
        var b = Encoding.UTF8.GetBytes(e.Otp);
        if (a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b))
            return (false, "Incorrect OTP. Please try again.");

        _store.TryRemove(key, out _);
        _verified[key] = DateTime.UtcNow + VerifiedTtl;
        return (true, "Verified.");
    }

    public static bool IsRecentlyVerified(string? mobileRaw)
    {
        if (IsDemo(mobileRaw)) return true;
        var key = Key(mobileRaw);
        if (_verified.TryGetValue(key, out var until))
        {
            if (DateTime.UtcNow <= until) return true;
            _verified.TryRemove(key, out _);
        }
        return false;
    }

    public static void ClearVerified(string? mobileRaw) => _verified.TryRemove(Key(mobileRaw), out _);
}
