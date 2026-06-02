// ─────────────────────────────────────────────────────────────────────────────
//  MSG91 SMS-OTP for verifying an agent's MOBILE NUMBER at registration & login.
//
//  Uses MSG91's dedicated OTP API (https://control.msg91.com/api/v5/otp):
//  MSG91 GENERATES the OTP, fills it into the OTP-type template automatically
//  (so we never need to know the template's variable name), sends the SMS, and
//  verifies the user's entry. We keep a short "recently verified" set so the
//  register/login endpoints can require a verified phone number.
//
//  The flow API (/api/v5/flow) was tried first but left the OTP blank in the
//  SMS because the template variable name didn't match — the OTP API avoids
//  that entirely for OTP templates like CRMRS_VERIFICATION.
//
//  Credentials are read ONLY from environment variables on the vkmobileapi
//  service — never hardcoded in the repo, never sent to the app:
//      MSG91_AUTHKEY      — the MSG91 auth key
//      MSG91_TEMPLATE_ID  — the OTP template id (CRMRS_VERIFICATION)
//      OTP_REQUIRED       — (optional) "0" disables server-side enforcement
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

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

    private const string Base = "https://control.msg91.com/api/v5/otp";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // Mobiles that have just passed an OTP check — register/login consult this.
    private static readonly ConcurrentDictionary<string, DateTime> _verified = new();
    // Last time we asked MSG91 to send to a number — throttles resend spam.
    private static readonly ConcurrentDictionary<string, DateTime> _lastSent = new();

    private static readonly TimeSpan VerifiedTtl  = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResendWindow = TimeSpan.FromSeconds(20);

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

    public static async Task<(bool ok, string message)> SendAsync(string? mobileRaw)
    {
        var key = Key(mobileRaw);
        if (key.Length != 10) return (false, "Enter a valid 10-digit mobile number.");
        if (!Configured)      return (false, "OTP service is not configured on the server.");

        if (_lastSent.TryGetValue(key, out var t) && DateTime.UtcNow - t < ResendWindow)
            return (false, "Please wait a few seconds before requesting another OTP.");

        var mobile = ToMsg91Mobile(key);
        // MSG91 generates the OTP and fills the template's OTP slot itself.
        var sendUrl = $"{Base}?template_id={Uri.EscapeDataString(TemplateId)}&mobile={mobile}&otp_length=6&otp_expiry=10";
        var (ok, msg) = await SendReq(HttpMethod.Post, sendUrl, withJsonBody: true);

        // If an OTP is still active for this number, MSG91 rejects a fresh send —
        // resend the existing one instead so "Resend" always works.
        if (!ok && msg.Contains("already", StringComparison.OrdinalIgnoreCase))
        {
            var retryUrl = $"{Base}/retry?mobile={mobile}&retrytype=text";
            (ok, msg) = await SendReq(HttpMethod.Get, retryUrl, withJsonBody: false);
        }

        if (ok) { _lastSent[key] = DateTime.UtcNow; return (true, "OTP sent."); }
        return (false, msg.Length > 0 ? msg : "Could not send OTP. Please try again.");
    }

    public static async Task<(bool ok, string message)> VerifyAsync(string? mobileRaw, string? otp)
    {
        var key  = Key(mobileRaw);
        var code = new string((otp ?? "").Where(char.IsDigit).ToArray());
        if (key.Length != 10) return (false, "Enter a valid 10-digit mobile number.");
        if (code.Length == 0) return (false, "Enter the OTP.");
        if (!Configured)      return (false, "OTP service is not configured on the server.");

        var verifyUrl = $"{Base}/verify?otp={code}&mobile={ToMsg91Mobile(key)}";
        var (ok, msg) = await SendReq(HttpMethod.Get, verifyUrl, withJsonBody: false);
        if (ok)
        {
            _verified[key] = DateTime.UtcNow + VerifiedTtl;
            return (true, "Verified.");
        }
        return (false, msg.Length > 0 ? msg : "Incorrect OTP. Please try again.");
    }

    private static async Task<(bool ok, string message)> SendReq(HttpMethod method, string url, bool withJsonBody)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.TryAddWithoutValidation("authkey", AuthKey);
            req.Headers.TryAddWithoutValidation("accept", "application/json");
            if (withJsonBody)
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();

            string type = "", message = "";
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("type", out var ty)) type = ty.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString() ?? "";
            }
            catch { /* non-JSON body — fall through to status-code check */ }

            bool ok = resp.IsSuccessStatusCode && type.Equals("success", StringComparison.OrdinalIgnoreCase);
            return (ok, message);
        }
        catch
        {
            return (false, "Could not reach the OTP service. Please try again.");
        }
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
