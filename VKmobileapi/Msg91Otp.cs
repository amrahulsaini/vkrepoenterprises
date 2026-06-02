// ─────────────────────────────────────────────────────────────────────────────
//  MSG91 SMS-OTP for verifying an agent's MOBILE NUMBER at registration & login.
//
//  Uses the MSG91 "flow" API (https://control.msg91.com/api/v5/flow) — the only
//  path that actually DELIVERS for the CRMRS_VERIFICATION template (the dedicated
//  OTP API returned success but never sent / stored anything for this account).
//  We generate the OTP ourselves, keep it in-process, send it as a template
//  variable, and verify the user's entry locally.
//
//  The template's OTP variable name isn't exposed by any API, so we send the OTP
//  value under every common variable name at once — MSG91 ignores the keys not
//  present in the template and fills the one that matches. (Verified live: the
//  code now renders in the SMS.)
//
//  Credentials are read ONLY from environment variables on the vkmobileapi
//  service — never hardcoded in the repo, never sent to the app:
//      MSG91_AUTHKEY      — the MSG91 auth key
//      MSG91_TEMPLATE_ID  — the flow template id (CRMRS_VERIFICATION)
//      OTP_REQUIRED       — (optional) "0" disables server-side enforcement
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace VKmobileapi;

public static class Msg91Otp
{
    private static readonly string AuthKey    = Environment.GetEnvironmentVariable("MSG91_AUTHKEY") ?? "";
    private static readonly string TemplateId = Environment.GetEnvironmentVariable("MSG91_TEMPLATE_ID") ?? "";

    public static bool Configured => AuthKey.Length > 0 && TemplateId.Length > 0;

    // Server-side gate on register/login. On by default when configured; ops can
    // set OTP_REQUIRED=0 to disable without a redeploy if MSG91 ever has issues.
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
    // Mobiles that have just passed an OTP check — register/login consult this.
    private static readonly ConcurrentDictionary<string, DateTime> _verified = new();

    private static readonly TimeSpan OtpTtl       = TimeSpan.FromMinutes(10);  // matches the template wording
    private static readonly TimeSpan VerifiedTtl  = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResendWindow = TimeSpan.FromSeconds(20);

    // The OTP value is sent under all of these so whichever the template was
    // built with gets filled. MSG91 ignores keys not present in the template.
    private static readonly string[] OtpVarKeys =
    {
        "otp", "OTP", "Otp", "code", "CODE", "Code", "var", "VAR", "var1", "VAR1",
        "var2", "VAR2", "otpcode", "otp_code", "verification_code", "verificationcode",
        "value", "pin", "PIN", "number"
    };

    /// <summary>Normalize to a 10-digit Indian mobile — must match the
    /// controller's NormalizeMobile so the OTP key lines up with register/login.</summary>
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

    /// <summary>True if this mobile passed an OTP check within the verified window.</summary>
    public static bool IsRecentlyVerified(string? mobileRaw)
    {
        var key = Key(mobileRaw);
        if (_verified.TryGetValue(key, out var until))
        {
            if (DateTime.UtcNow <= until) return true;
            _verified.TryRemove(key, out _);
        }
        return false;
    }

    /// <summary>Consume the verification so it can't be reused for another action.</summary>
    public static void ClearVerified(string? mobileRaw) => _verified.TryRemove(Key(mobileRaw), out _);
}
