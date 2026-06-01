// ─────────────────────────────────────────────────────────────────────────────
//  Sandbox (api.sandbox.co.in) KYC integration — Aadhaar OKYC (OTP), PAN, Bank.
//
//  Credentials are read ONLY from server environment variables — never hardcoded
//  and never sent to the WPF/Android client:
//      SANDBOX_API_KEY     (key_live_... / key_test_...)
//      SANDBOX_API_SECRET  (secret_live_... / secret_test_...)
//      SANDBOX_BASE_URL    (optional; default https://api.sandbox.co.in,
//                           use https://test-api.sandbox.co.in for sandbox)
//
//  Auth flow (per Sandbox docs): POST /authenticate with x-api-key + x-api-secret
//  → { data.access_token } (valid 24h). The token is sent in the Authorization
//  header WITHOUT a "Bearer" prefix, plus x-api-key, on every subsequent call.
//  We cache the token in-process and refresh it ~20h.
//
//  Endpoints exposed to the desktop app (MgrAuth-gated):
//      POST /api/mgr/kyc/aadhaar/otp     { aadhaarNumber }            → { referenceId }
//      POST /api/mgr/kyc/aadhaar/verify  { referenceId, otp }         → { name, dob, ... }
//      POST /api/mgr/kyc/pan             { pan, name, dob(DD/MM/YYYY) }→ { status, matches }
//      POST /api/mgr/kyc/bank            { ifsc, accountNumber, name } → { nameAtBank }
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace VKApiServer;

internal static class SandboxKyc
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("SANDBOX_BASE_URL") ?? "https://api.sandbox.co.in").TrimEnd('/');
    private static readonly string ApiKey    = Environment.GetEnvironmentVariable("SANDBOX_API_KEY")    ?? "";
    private static readonly string ApiSecret = Environment.GetEnvironmentVariable("SANDBOX_API_SECRET") ?? "";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static string   _token = "";
    private static DateTime _tokenExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    private static bool Configured => ApiKey.Length > 0 && ApiSecret.Length > 0;

    // ── access-token cache ───────────────────────────────────────────────────
    private static async Task<string> TokenAsync()
    {
        if (_token.Length > 0 && DateTime.UtcNow < _tokenExpiry) return _token;
        await _tokenLock.WaitAsync();
        try
        {
            if (_token.Length > 0 && DateTime.UtcNow < _tokenExpiry) return _token;
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/authenticate");
            req.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
            req.Headers.TryAddWithoutValidation("x-api-secret", ApiSecret);
            req.Headers.TryAddWithoutValidation("x-api-version", "1.0");
            using var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var tok = doc.RootElement.TryGetProperty("data", out var d)
                      && d.TryGetProperty("access_token", out var t) ? t.GetString() ?? "" : "";
            if (tok.Length == 0) throw new Exception("Sandbox authenticate failed: " + json);
            _token = tok;
            _tokenExpiry = DateTime.UtcNow.AddHours(20); // token lives 24h; refresh early
            return _token;
        }
        finally { _tokenLock.Release(); }
    }

    // ── generic authenticated call ─────────────────────────────────────────────
    private static async Task<JsonElement> CallAsync(HttpMethod method, string path, object? body, string? apiVersion)
    {
        var token = await TokenAsync();
        using var req = new HttpRequestMessage(method, BaseUrl + path);
        req.Headers.TryAddWithoutValidation("Authorization", token); // NO "Bearer" prefix (per docs)
        req.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
        if (apiVersion != null) req.Headers.TryAddWithoutValidation("x-api-version", apiVersion);
        if (body != null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    public static void Map(WebApplication app, Func<HttpContext, bool> auth)
    {
        // ── Aadhaar OKYC — step 1: send OTP ────────────────────────────────────
        app.MapPost("/api/mgr/kyc/aadhaar/otp", async (HttpContext ctx) =>
        {
            if (!auth(ctx)) return Results.Unauthorized();
            if (!Configured) return Results.Problem("KYC not configured on server (set SANDBOX_API_KEY / SANDBOX_API_SECRET).");
            var dto = await ReadJson(ctx);
            var aadhaar = (dto.GetValueOrDefault("aadhaarNumber") ?? "").Replace(" ", "").Trim();
            if (aadhaar.Length != 12 || !aadhaar.IsAllDigits())
                return Results.BadRequest(new { ok = false, message = "Enter a valid 12-digit Aadhaar number." });
            try
            {
                var r = await CallAsync(HttpMethod.Post, "/kyc/aadhaar/okyc/otp", new Dictionary<string, object?>
                {
                    ["@entity"] = "in.co.sandbox.kyc.aadhaar.okyc.otp.request",
                    ["aadhaar_number"] = aadhaar,
                    ["consent"] = "y",
                    ["reason"] = "KYC verification for recovery-agent onboarding"
                }, "1.0.0");
                if (r.TryGetProperty("data", out var d) && d.TryGetProperty("reference_id", out var refId))
                    return Results.Ok(new { ok = true, referenceId = refId.ToString() });
                return Results.BadRequest(new { ok = false, message = Msg(r) });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        // ── Aadhaar OKYC — step 2: verify OTP → identity details ───────────────
        app.MapPost("/api/mgr/kyc/aadhaar/verify", async (HttpContext ctx) =>
        {
            if (!auth(ctx)) return Results.Unauthorized();
            if (!Configured) return Results.Problem("KYC not configured on server.");
            var dto = await ReadJson(ctx);
            var refId = (dto.GetValueOrDefault("referenceId") ?? "").Trim();
            var otp   = (dto.GetValueOrDefault("otp") ?? "").Trim();
            if (refId.Length == 0 || otp.Length < 4)
                return Results.BadRequest(new { ok = false, message = "Reference id and the 6-digit OTP are required." });
            try
            {
                // Sandbox requires reference_id as a STRING — sending a JSON
                // number is rejected with "Invalid request body".
                var r = await CallAsync(HttpMethod.Post, "/kyc/aadhaar/okyc/otp/verify", new Dictionary<string, object?>
                {
                    ["@entity"] = "in.co.sandbox.kyc.aadhaar.okyc.request",
                    ["reference_id"] = refId,
                    ["otp"] = otp
                }, "1.0.0");
                if (r.TryGetProperty("data", out var d))
                {
                    string S(string k) => d.TryGetProperty(k, out var v)
                        ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
                    // Sandbox returns 200 + data even on a wrong OTP (just a
                    // "message", no identity). Don't report verified in that case.
                    if (S("name").Length == 0 && S("date_of_birth").Length == 0)
                    {
                        var dm = S("message");
                        return Results.BadRequest(new { ok = false,
                            message = dm.Length > 0 ? dm : "OTP verification failed. Please check the OTP and try again." });
                    }
                    string addr = S("full_address");
                    if (addr.Length == 0 && d.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object)
                        addr = a.ToString();
                    return Results.Ok(new
                    {
                        ok = true,
                        verified = true,
                        name    = S("name"),
                        dob     = S("date_of_birth"),
                        gender  = S("gender"),
                        address = addr,
                        careOf  = S("care_of"),
                        photo   = S("photo"),          // base64 (UIDAI photo)
                        status  = S("status")
                    });
                }
                return Results.BadRequest(new { ok = false, message = Msg(r) });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        // ── PAN verification ───────────────────────────────────────────────────
        app.MapPost("/api/mgr/kyc/pan", async (HttpContext ctx) =>
        {
            if (!auth(ctx)) return Results.Unauthorized();
            if (!Configured) return Results.Problem("KYC not configured on server.");
            var dto = await ReadJson(ctx);
            var pan  = (dto.GetValueOrDefault("pan") ?? "").Trim().ToUpper();
            var name = (dto.GetValueOrDefault("name") ?? "").Trim();
            var dob  = (dto.GetValueOrDefault("dob") ?? "").Trim(); // DD/MM/YYYY
            if (pan.Length != 10)
                return Results.BadRequest(new { ok = false, message = "Enter a valid 10-character PAN." });
            try
            {
                var r = await CallAsync(HttpMethod.Post, "/kyc/pan/verify", new Dictionary<string, object?>
                {
                    ["@entity"] = "in.co.sandbox.kyc.pan_verification.request",
                    ["pan"] = pan,
                    ["name_as_per_pan"] = name,
                    ["date_of_birth"] = dob,
                    ["consent"] = "Y",
                    ["reason"] = "KYC verification for recovery-agent onboarding compliance"
                }, null);
                if (r.TryGetProperty("data", out var d))
                {
                    string S(string k) => d.TryGetProperty(k, out var v)
                        ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
                    bool B(string k) => d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
                    var status = S("status");
                    return Results.Ok(new
                    {
                        ok = true,
                        verified = status.Equals("valid", StringComparison.OrdinalIgnoreCase),
                        status = status,
                        category = S("category"),
                        nameMatch = B("name_as_per_pan_match"),
                        dobMatch = B("date_of_birth_match"),
                        aadhaarSeeding = S("aadhaar_seeding_status"),
                        pan = S("pan")
                    });
                }
                return Results.BadRequest(new { ok = false, message = Msg(r) });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        // ── Bank account verification (penny-less) ─────────────────────────────
        app.MapPost("/api/mgr/kyc/bank", async (HttpContext ctx) =>
        {
            if (!auth(ctx)) return Results.Unauthorized();
            if (!Configured) return Results.Problem("KYC not configured on server.");
            var dto = await ReadJson(ctx);
            var ifsc = (dto.GetValueOrDefault("ifsc") ?? "").Trim().ToUpper();
            var acct = (dto.GetValueOrDefault("accountNumber") ?? "").Trim();
            var name = (dto.GetValueOrDefault("name") ?? "").Trim();
            if (ifsc.Length != 11 || acct.Length == 0)
                return Results.BadRequest(new { ok = false, message = "Enter a valid account number and 11-character IFSC." });
            try
            {
                var q = name.Length > 0 ? "?name=" + Uri.EscapeDataString(name) : "";
                var r = await CallAsync(HttpMethod.Get,
                    $"/bank/{ifsc}/accounts/{Uri.EscapeDataString(acct)}/penniless-verify" + q, null, null);
                if (r.TryGetProperty("data", out var d))
                {
                    bool exists = d.TryGetProperty("account_exists", out var e) && e.ValueKind == JsonValueKind.True;
                    string nameAtBank = d.TryGetProperty("name_at_bank", out var n) ? (n.GetString() ?? "") : "";
                    return Results.Ok(new { ok = true, verified = exists, accountExists = exists, nameAtBank });
                }
                return Results.BadRequest(new { ok = false, message = Msg(r) });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });
    }

    private static string Msg(JsonElement r)
    {
        if (r.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
            return m.GetString() ?? "Verification failed.";
        if (r.TryGetProperty("data", out var d) && d.TryGetProperty("message", out var dm) && dm.ValueKind == JsonValueKind.String)
            return dm.GetString() ?? "Verification failed.";
        return "Verification failed — please re-check the details and try again.";
    }

    private static async Task<Dictionary<string, string>> ReadJson(HttpContext ctx)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var dict = new Dictionary<string, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
            return dict;
        }
        catch { return new(); }
    }

    private static bool IsAllDigits(this string s)
    {
        foreach (var c in s) if (c < '0' || c > '9') return false;
        return s.Length > 0;
    }
}
