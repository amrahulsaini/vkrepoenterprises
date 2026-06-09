using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace VKmobileapi;

public static class SandboxKyc
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("SANDBOX_BASE_URL") ?? "https://api.sandbox.co.in").TrimEnd('/');
    private static readonly string ApiKey    = Environment.GetEnvironmentVariable("SANDBOX_API_KEY")    ?? "";
    private static readonly string ApiSecret = Environment.GetEnvironmentVariable("SANDBOX_API_SECRET") ?? "";

    public static bool Configured => ApiKey.Length > 0 && ApiSecret.Length > 0;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static string _token = "";
    private static DateTime _tokenExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    private static async Task<string> TokenAsync()
    {
        if (_token.Length > 0 && DateTime.UtcNow < _tokenExpiry) return _token;
        await _lock.WaitAsync();
        try
        {
            if (_token.Length > 0 && DateTime.UtcNow < _tokenExpiry) return _token;
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/authenticate");
            req.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
            req.Headers.TryAddWithoutValidation("x-api-secret", ApiSecret);
            req.Headers.TryAddWithoutValidation("x-api-version", "1.0");
            using var resp = await Http.SendAsync(req);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var tok = doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("access_token", out var t)
                ? t.GetString() ?? "" : "";
            if (tok.Length == 0) throw new Exception("Sandbox authenticate failed");
            _token = tok; _tokenExpiry = DateTime.UtcNow.AddHours(20);
            return _token;
        }
        finally { _lock.Release(); }
    }

    private static async Task<JsonElement> CallAsync(HttpMethod method, string path, object? body, string? apiVersion)
    {
        var token = await TokenAsync();
        using var req = new HttpRequestMessage(method, BaseUrl + path);
        req.Headers.TryAddWithoutValidation("Authorization", token);
        req.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
        if (apiVersion != null) req.Headers.TryAddWithoutValidation("x-api-version", apiVersion);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    public static string Message(JsonElement r)
    {
        if (r.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) return m.GetString() ?? "";
        if (r.TryGetProperty("data", out var d) && d.TryGetProperty("message", out var dm) && dm.ValueKind == JsonValueKind.String) return dm.GetString() ?? "";
        return "Verification failed.";
    }

    public static Task<JsonElement> AadhaarOtpAsync(string aadhaar) =>
        CallAsync(HttpMethod.Post, "/kyc/aadhaar/okyc/otp", new Dictionary<string, object?>
        {
            ["@entity"] = "in.co.sandbox.kyc.aadhaar.okyc.otp.request",
            ["aadhaar_number"] = aadhaar,
            ["consent"] = "y",
            ["reason"] = "KYC verification for recovery-agent onboarding"
        }, "1.0.0");

    public static Task<JsonElement> AadhaarVerifyAsync(object referenceId, string otp) =>
        CallAsync(HttpMethod.Post, "/kyc/aadhaar/okyc/otp/verify", new Dictionary<string, object?>
        {
            ["@entity"] = "in.co.sandbox.kyc.aadhaar.okyc.request",
            ["reference_id"] = referenceId,
            ["otp"] = otp
        }, "1.0.0");

    public static Task<JsonElement> PanVerifyAsync(string pan, string name, string dob) =>
        CallAsync(HttpMethod.Post, "/kyc/pan/verify", new Dictionary<string, object?>
        {
            ["@entity"] = "in.co.sandbox.kyc.pan_verification.request",
            ["pan"] = pan,
            ["name_as_per_pan"] = name,
            ["date_of_birth"] = dob,
            ["consent"] = "Y",
            ["reason"] = "KYC verification for recovery-agent onboarding compliance"
        }, null);

    public static Task<JsonElement> BankVerifyAsync(string ifsc, string account, string name)
    {
        var q = name.Length > 0 ? "?name=" + Uri.EscapeDataString(name) : "";
        return CallAsync(HttpMethod.Get, $"/bank/{ifsc}/accounts/{Uri.EscapeDataString(account)}/penniless-verify" + q, null, null);
    }
}
