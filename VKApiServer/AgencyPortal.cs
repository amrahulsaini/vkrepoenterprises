using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MySqlConnector;

namespace VKApiServer;

internal static class AgencyPortal
{
    private static readonly string MANAGE_PASSWORD = RequiredEnv.Get("MANAGE_PASSWORD");

    private const string LOGO_DIR = "/opt/vkapi/agency-uploads";

    private static readonly string TenantDbSecret = RequiredEnv.Get("TENANT_DB_SECRET");

    public static string MasterConn { get; private set; } = "";

    private static readonly (string Label, string Col)[] IntegRecordCols =
    {
        ("Vehicle No",       "vehicle_no"),
        ("Chassis No",       "chassis_no"),
        ("Engine No",        "engine_no"),
        ("Model",            "model"),
        ("Agreement No",     "agreement_no"),
        ("Bucket",           "bucket"),
        ("Customer Name",    "customer_name"),
        ("Customer Address", "customer_address"),
        ("Customer Contact", "customer_contact"),
        ("Region",           "region"),
        ("Area",             "area"),
        ("Branch",           "branch_name_raw"),
        ("Executive",        "executive_name"),
        ("POS",              "pos"),
        ("TOSS",             "toss"),
        ("Remark",           "remark"),
    };

    private static string IntegNormKey(string s) =>
        Regex.Replace(s ?? "", "[^A-Za-z0-9]", "").ToLowerInvariant();

    private static readonly (string Label, string Col)[] IntegFullCols =
    {
        ("Vehicle No","vehicle_no"), ("Chassis No","chassis_no"), ("Engine No","engine_no"),
        ("Model","model"), ("Agreement No","agreement_no"), ("Bucket","bucket"),
        ("GV","gv"), ("OD","od"), ("Seasoning","seasoning"), ("TBR","tbr_flag"),
        ("Sec 9","sec9_available"), ("Sec 17","sec17_available"),
        ("Customer Name","customer_name"), ("Customer Address","customer_address"), ("Customer Contact","customer_contact"),
        ("Owner Name","owner_name"), ("Mobile No","mobile_no"),
        ("Region","region"), ("Area","area"), ("Branch (from Excel)","branch_name_raw"),
        ("Level 1","level1"), ("Level 1 Contact","level1_contact"),
        ("Level 2","level2"), ("Level 2 Contact","level2_contact"),
        ("Level 3","level3"), ("Level 3 Contact","level3_contact"),
        ("Level 4","level4"), ("Level 4 Contact","level4_contact"),
        ("Sender Mail 1","sender_mail1"), ("Sender Mail 2","sender_mail2"),
        ("Executive Name","executive_name"), ("POS","pos"), ("TOSS","toss"), ("Remark","remark"),
        ("Created","created_at"),
    };

    private static string IntegColExpr(string col) =>
        col == "created_at"
            ? "COALESCE(DATE_FORMAT(vr.`created_at`,'%d %b %Y %h:%i %p'),'')"
            : "vr.`" + col + "`";

    private static readonly System.Collections.Generic.Dictionary<string, string> IntegImportCols = new()
    {
        ["vehicleno"] = "vehicle_no", ["chassisno"] = "chassis_no", ["engineno"] = "engine_no",
        ["model"] = "model", ["agreementno"] = "agreement_no", ["bucket"] = "bucket",
        ["gv"] = "gv", ["od"] = "od", ["seasoning"] = "seasoning", ["tbr"] = "tbr_flag",
        ["sec9"] = "sec9_available", ["sec17"] = "sec17_available",
        ["customername"] = "customer_name", ["customeraddress"] = "customer_address", ["customercontact"] = "customer_contact",
        ["ownername"] = "owner_name", ["mobileno"] = "mobile_no",
        ["region"] = "region", ["area"] = "area", ["branch"] = "branch_name_raw",
        ["level1"] = "level1", ["level1contact"] = "level1_contact",
        ["level2"] = "level2", ["level2contact"] = "level2_contact",
        ["level3"] = "level3", ["level3contact"] = "level3_contact",
        ["level4"] = "level4", ["level4contact"] = "level4_contact",
        ["sendermail1"] = "sender_mail1", ["sendermail2"] = "sender_mail2",
        ["executivename"] = "executive_name", ["pos"] = "pos", ["toss"] = "toss", ["remark"] = "remark",
    };

    private static string IntegCap(string? s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));

    private static readonly TimeZoneInfo IstZone = ResolveIst();
    private static TimeZoneInfo ResolveIst()
    {
        foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "IST", "IST");
    }

    private static string BuiltAtIst(string path)
    {
        if (!File.Exists(path)) return "";
        var ist = TimeZoneInfo.ConvertTimeFromUtc(File.GetLastWriteTimeUtc(path), IstZone);
        return ist.ToString("yyyy-MM-dd HH:mm 'IST'");
    }

    private static async Task<List<Dictionary<string, object>>> ReadTicketHeaders(
        MySqlConnection conn, string whereOrder, bool withAgency, params (string, object)[] ps)
    {
        const string baseUrl = "https://api.crmrecoverysoftware.com";
        var cols = withAgency
            ? "id, subject, message, COALESCE(screenshot_path,''), status, DATE_FORMAT(created_at,'%d %b %Y %H:%i'), DATE_FORMAT(updated_at,'%d %b %Y %H:%i'), agency_name, agency_slug"
            : "id, subject, message, COALESCE(screenshot_path,''), status, DATE_FORMAT(created_at,'%d %b %Y %H:%i'), DATE_FORMAT(updated_at,'%d %b %Y %H:%i')";
        await using var cmd = new MySqlCommand($"SELECT {cols} FROM support_tickets {whereOrder}", conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);

        var list = new List<Dictionary<string, object>>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            var shot = rdr.GetString(3);
            var d = new Dictionary<string, object>
            {
                ["id"]            = rdr.GetInt32(0),
                ["subject"]       = rdr.GetString(1),
                ["message"]       = rdr.GetString(2),
                ["screenshotUrl"] = string.IsNullOrEmpty(shot) ? "" : $"{baseUrl}/agency-uploads/{shot}",
                ["status"]        = rdr.GetString(4),
                ["createdAt"]     = rdr.GetString(5),
                ["updatedAt"]     = rdr.GetString(6),
                ["agencyName"]    = withAgency ? rdr.GetString(7) : "",
                ["agencySlug"]    = withAgency ? rdr.GetString(8) : "",
                ["messages"]      = new List<object>(),
            };
            list.Add(d);
        }
        return list;
    }

    private static async Task<List<object>> LoadMessages(MySqlConnection conn, int ticketId)
    {
        await using var cmd = new MySqlCommand(@"
            SELECT id, sender, body, DATE_FORMAT(created_at,'%d %b %Y %H:%i')
              FROM support_ticket_messages WHERE ticket_id=@t ORDER BY id ASC", conn);
        cmd.Parameters.AddWithValue("@t", ticketId);
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id        = rdr.GetInt32(0),
                sender    = rdr.GetString(1),
                body      = rdr.GetString(2),
                createdAt = rdr.GetString(3),
            });
        return list;
    }

    private static async Task AddMessage(MySqlConnection conn, int ticketId, string sender, string body)
    {
        await using var cmd = new MySqlCommand(
            "INSERT INTO support_ticket_messages (ticket_id, sender, body) VALUES (@t,@s,@b); " +
            "UPDATE support_tickets SET updated_at=UTC_TIMESTAMP() WHERE id=@t;", conn);
        cmd.Parameters.AddWithValue("@t", ticketId);
        cmd.Parameters.AddWithValue("@s", sender);
        cmd.Parameters.AddWithValue("@b", body);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureClientErrorTable(MySqlConnection conn)
    {
        await using var cmd = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS client_error_log (
                id           BIGINT AUTO_INCREMENT PRIMARY KEY,
                agency_id    INT NULL,
                agency_slug  VARCHAR(64)   NOT NULL,
                agency_name  VARCHAR(255)  NULL,
                operation    VARCHAR(120)  NOT NULL,
                summary      VARCHAR(500)  NULL,
                detail       MEDIUMTEXT    NULL,
                context      VARCHAR(1000) NULL,
                app_version  VARCHAR(40)   NULL,
                machine_name VARCHAR(120)  NULL,
                os           VARCHAR(160)  NULL,
                source_ip    VARCHAR(64)   NULL,
                client_time  VARCHAR(40)   NULL,
                created_at   DATETIME      NOT NULL DEFAULT UTC_TIMESTAMP(),
                INDEX idx_slug_id (agency_slug, id),
                INDEX idx_created (created_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadBody(HttpRequest req)
    {
        try
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var r = doc.RootElement;
            string? s = (r.TryGetProperty("body", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.String) ? b.GetString()
                      : (r.TryGetProperty("message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String) ? m.GetString()
                      : null;
            return (s ?? "").Trim();
        }
        catch { return ""; }
    }

    private static (int id, string slug)? VerifyAgencyBearer(HttpContext ctx)
    {
        var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(auth) ||
            !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return AgencyToken.Verify(auth.Substring(7).Trim());
    }

    public static void Map(WebApplication app, string mysqlHost, int mysqlPort)
    {
        string masterConn =
            $"server={mysqlHost};port={mysqlPort};database=crm_master;" +
            $"uid={RequiredEnv.Get("MASTER_DB_USER")};" +
            $"pwd={RequiredEnv.Get("MASTER_DB_PASSWORD")};" +
             "Pooling=true;DefaultCommandTimeout=30;";
        MasterConn = masterConn;
        string provConn =
            $"server={mysqlHost};port={mysqlPort};database=mysql;" +
            $"uid={Env("PROVISIONER_DB_USER",     "crm_provisioner")};" +
            $"pwd={Env("PROVISIONER_DB_PASSWORD", "SET_VIA_ENV")};" +
             "Pooling=false;DefaultCommandTimeout=60;AllowUserVariables=true;";

        var smtp = new SmtpConfig {
            Host     = Env("SMTP_HOST",      "127.0.0.1"),
            Port     = int.Parse(Env("SMTP_PORT", "25")),
            User     = Env("SMTP_USER",      ""),
            Pass     = Env("SMTP_PASS",      ""),
            Ssl      = Env("SMTP_SSL", "false").Trim().ToLowerInvariant() is "true" or "1" or "yes",
            FromAddr = Env("SMTP_FROM",      "team@crmrecoverysoftware.com"),
            FromName = Env("SMTP_FROM_NAME", "CRMRS TEAM"),
        };

        try { Directory.CreateDirectory(LOGO_DIR); } catch { }

        app.MapPost("/api/agency/otp/send", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string email = (dto.GetValueOrDefault("email") ?? "").Trim().ToLowerInvariant();
            if (!IsValidEmail(email))
                return Results.BadRequest(new { message = "Please provide a valid email address." });

            string code = GenerateOtp();
            var expiresAt = DateTime.UtcNow.AddMinutes(10);

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            var rThrottle = await OtpThrottle(conn, email, "register");
            if (rThrottle.HourlyCapHit)
                return Results.Json(new { message = "Too many codes requested. Try again later." }, statusCode: 429);
            if (rThrottle.RetrySeconds > 0)
                return Results.Json(new { retryAfter = rThrottle.RetrySeconds,
                    message = "A code was just sent. Wait " + rThrottle.RetrySeconds + "s before asking for another." }, statusCode: 429);

            await using (var cmd = new MySqlCommand(
                "INSERT INTO agency_otps (email, code, purpose, expires_at) VALUES (@e, @c, 'register', @x)", conn))
            {
                cmd.Parameters.AddWithValue("@e", email);
                cmd.Parameters.AddWithValue("@c", code);
                cmd.Parameters.AddWithValue("@x", expiresAt);
                await cmd.ExecuteNonQueryAsync();
            }

            try
            {
                await SendOtpEmail(smtp, email, code);
            }
            catch (Exception ex)
            {
                return Results.Problem("Failed to send email: " + ex.Message);
            }
            return Results.Ok(new { sent = true });
        });

        app.MapPost("/api/agency/otp/verify", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string email = (dto.GetValueOrDefault("email") ?? "").Trim().ToLowerInvariant();
            string code  = (dto.GetValueOrDefault("code") ?? "").Trim();
            if (string.IsNullOrEmpty(email) || code.Length != 6)
                return Results.BadRequest(new { message = "Email and 6-digit code required." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                UPDATE agency_otps
                   SET consumed = 1
                 WHERE email = @e AND code = @c AND purpose = 'register'
                   AND consumed = 0 AND expires_at > UTC_TIMESTAMP()
                 ORDER BY id DESC LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("@e", email);
            cmd.Parameters.AddWithValue("@c", code);
            int n = await cmd.ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Invalid or expired code." });
            return Results.Ok(new { verified = true });
        });

        app.MapGet("/api/agency/hrms/status", async (string? slug) =>
        {
            string s2 = (slug ?? "").Trim().ToLowerInvariant();
            if (s2.Length == 0) return Results.BadRequest(new { message = "Agency not specified." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT name, email1, status, COALESCE(hrms_enabled,0), COALESCE(logo_path,'') FROM agencies WHERE slug=@s LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@s", s2);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync())
                return Results.Json(new { code = "not_found", message = "No agency matches that address." }, statusCode: 404);

            string name = rdr.GetString(0), email = rdr.GetString(1), st = rdr.GetString(2);
            bool hrms = rdr.GetInt32(3) == 1;
            if (st != "approved")
                return Results.Json(new { code = "not_active", agencyName = name, message = "This agency account is not active." }, statusCode: 403);
            if (!hrms)
                return Results.Json(new { code = "not_enabled", agencyName = name, message = "HRMS is not switched on for this agency." }, statusCode: 403);

            return Results.Ok(new { agencyName = name, email = MaskEmail(email), logoPath = rdr.GetString(4), hrmsEnabled = true });
        });

        app.MapPost("/api/agency/hrms/otp/request", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string slug = (dto.GetValueOrDefault("slug") ?? "").Trim().ToLowerInvariant();
            if (slug.Length == 0) return Results.BadRequest(new { message = "Agency not specified." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            string email = "", name = "", status = "";
            bool hrms = false;
            await using (var cmd = new MySqlCommand(
                "SELECT email1, name, status, COALESCE(hrms_enabled,0) FROM agencies WHERE slug=@s LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@s", slug);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync())
                    return Results.NotFound(new { message = "Agency not found." });
                email  = rdr.GetString(0);
                name   = rdr.GetString(1);
                status = rdr.GetString(2);
                hrms   = rdr.GetInt32(3) == 1;
            }

            if (status != "approved")
                return Results.Json(new { message = "This agency is not active." }, statusCode: 403);
            if (!hrms)
                return Results.Json(new { message = "HRMS is not enabled for this agency. Contact CRMRS to enable it." }, statusCode: 403);

            var throttle = await OtpThrottle(conn, email, "hrms");
            if (throttle.HourlyCapHit)
                return Results.Json(new { code = "rate_limited", message = "Too many codes requested. Try again later." }, statusCode: 429);
            if (throttle.RetrySeconds > 0)
                return Results.Json(new { code = "cooldown", retryAfter = throttle.RetrySeconds,
                    message = "A code was just sent. Wait " + throttle.RetrySeconds + "s before asking for another." }, statusCode: 429);

            string code = GenerateOtp();
            await using (var ins = new MySqlCommand(
                "INSERT INTO agency_otps (email, code, purpose, expires_at) VALUES (@e, @c, 'hrms', @x)", conn))
            {
                ins.Parameters.AddWithValue("@e", email);
                ins.Parameters.AddWithValue("@c", code);
                ins.Parameters.AddWithValue("@x", DateTime.UtcNow.AddMinutes(10));
                await ins.ExecuteNonQueryAsync();
            }

            try { await SendOtpEmail(smtp, email, code); }
            catch (Exception ex) { return Results.Problem("Could not send the code: " + ex.Message); }

            return Results.Ok(new { sent = true, agencyName = name, email = MaskEmail(email) });
        });

        app.MapPost("/api/agency/hrms/otp/verify", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string slug = (dto.GetValueOrDefault("slug") ?? "").Trim().ToLowerInvariant();
            string code = (dto.GetValueOrDefault("code") ?? "").Trim();
            if (slug.Length == 0 || code.Length != 6)
                return Results.BadRequest(new { message = "Enter the 6-digit code." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            int agencyId = 0; string email = "", name = "", logo = "";
            await using (var cmd = new MySqlCommand(
                "SELECT id, email1, name, COALESCE(logo_path,'') FROM agencies WHERE slug=@s AND status='approved' AND COALESCE(hrms_enabled,0)=1 LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@s", slug);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync())
                    return Results.Json(new { message = "HRMS is not available for this agency." }, statusCode: 403);
                agencyId = rdr.GetInt32(0); email = rdr.GetString(1);
                name = rdr.GetString(2); logo = rdr.GetString(3);
            }

            int n;
            await using (var upd = new MySqlCommand(@"
                UPDATE agency_otps SET consumed = 1
                 WHERE email = @e AND code = @c AND purpose = 'hrms'
                   AND consumed = 0 AND expires_at > UTC_TIMESTAMP()
                 ORDER BY id DESC LIMIT 1;", conn))
            {
                upd.Parameters.AddWithValue("@e", email);
                upd.Parameters.AddWithValue("@c", code);
                n = await upd.ExecuteNonQueryAsync();
            }
            if (n == 0) return Results.BadRequest(new { message = "That code is invalid or has expired." });

            string token = NewToken();
            await using (var ins = new MySqlCommand(
                "INSERT INTO hrms_sessions (agency_id, token_hash, expires_at) VALUES (@a, @t, DATE_ADD(UTC_TIMESTAMP(), INTERVAL 12 HOUR))", conn))
            {
                ins.Parameters.AddWithValue("@a", agencyId);
                ins.Parameters.AddWithValue("@t", Sha256Hex(token));
                await ins.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { token, agencyId, agencyName = name, slug, logoPath = logo });
        });

        app.MapPost("/api/agency/desktop/auth-challenge", async (HttpContext ctx, HttpRequest req) =>
        {
            var agency = VerifyAgencyBearer(ctx);
            if (agency is not { } ag) return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            string id = NewChallengeId(), nonce = NewNonce(), pair = NewPairCode();
            var now = DateTime.UtcNow;

            string claimMobile = new string((dto.GetValueOrDefault("mobile") ?? "").Where(char.IsDigit).ToArray());
            if (claimMobile.Length > 10) claimMobile = claimMobile.Substring(claimMobile.Length - 10);
            long claimUserId = 0;
            if (claimMobile.Length == 10)
            {
                try
                {
                    await using var tconn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, ag.slug));
                    await tconn.OpenAsync();
                    await using var find = new MySqlCommand(
                        "SELECT id FROM app_users WHERE RIGHT(REPLACE(COALESCE(mobile,''),' ',''),10) = @m LIMIT 1;", tconn)
                    { CommandTimeout = 10 };
                    find.Parameters.AddWithValue("@m", claimMobile);
                    if (await find.ExecuteScalarAsync() is { } got) claimUserId = Convert.ToInt64(got);
                }
                catch { claimUserId = 0; }
            }

            await using (var conn = new MySqlConnection(masterConn))
            {
                await conn.OpenAsync();
                await using var ins = new MySqlCommand(
                    "INSERT INTO auth_challenges (id, agency_id, slug, nonce, pair_code, mode, device_label, " +
                    "claim_user_id, claim_mobile, desktop_ip, created_at, expires_at) " +
                    "VALUES (@i, @a, @s, @n, @p, @m, @d, @cu, @cm, @ip, @c, @x);", conn);
                ins.Parameters.AddWithValue("@i", id);
                ins.Parameters.AddWithValue("@a", ag.id);
                ins.Parameters.AddWithValue("@s", ag.slug);
                ins.Parameters.AddWithValue("@n", nonce);
                ins.Parameters.AddWithValue("@p", pair);
                ins.Parameters.AddWithValue("@m", (dto.GetValueOrDefault("mode") ?? "").Trim());
                ins.Parameters.AddWithValue("@d", (dto.GetValueOrDefault("deviceLabel") ?? "").Trim());
                ins.Parameters.AddWithValue("@cu", claimUserId == 0 ? (object)DBNull.Value : claimUserId);
                ins.Parameters.AddWithValue("@cm", claimMobile);
                ins.Parameters.AddWithValue("@ip", ClientIp(ctx));
                ins.Parameters.AddWithValue("@c", now);
                ins.Parameters.AddWithValue("@x", now.AddSeconds(AuthChallengeSeconds));
                await ins.ExecuteNonQueryAsync();
            }

            string payload = "crmrs://auth?c=" + id + "&s=" + Uri.EscapeDataString(ag.slug);
            return Results.Ok(new
            {
                id,
                pairCode = pair,
                qr = QrDataUri(payload),
                expiresInSeconds = AuthChallengeSeconds
            });
        });

        app.MapGet("/api/agency/desktop/auth-challenge/{id}", async (HttpContext ctx, string id) =>
        {
            var agency = VerifyAgencyBearer(ctx);
            if (agency is not { } ag) return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT status, COALESCE(approved_name,''), COALESCE(approved_user_id,0), expires_at, COALESCE(fail_reason,''), slug " +
                "FROM auth_challenges WHERE id=@i AND agency_id=@a LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("@i", id);
            cmd.Parameters.AddWithValue("@a", ag.id);
            string status, approvedName, chalSlug, fail;
            long approvedId;
            await using (var rdr = await cmd.ExecuteReaderAsync())
            {
                if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Unknown request." });

                status       = rdr.GetString(0);
                approvedName = rdr.GetString(1);
                approvedId   = rdr.GetInt64(2);
                fail         = rdr.GetString(4);
                chalSlug     = rdr.GetString(5);

                if ((status == "pending" || status == "scanned") && rdr.GetDateTime(3) < DateTime.UtcNow)
                    status = "expired";
            }

            string[] mods = Array.Empty<string>();
            string roleName = "";

            if (status == "approved" && approvedId > 0)
            {
                await using var tconn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, chalSlug));
                await tconn.OpenAsync();
                await using var rc = new MySqlCommand(
                    "SELECT COALESCE(r.is_superadmin,0), COALESCE(r.modules,''), COALESCE(r.name,''), u.modules_override " +
                    "FROM app_users u LEFT JOIN roles r ON r.id = u.role_id WHERE u.id=@u LIMIT 1;", tconn);
                rc.Parameters.AddWithValue("@u", approvedId);
                await using var rr = await rc.ExecuteReaderAsync();
                if (await rr.ReadAsync())
                {
                    var ov = rr.IsDBNull(3) ? null : rr.GetString(3);
                    mods = Modules.Effective(rr.GetInt32(0) == 1, ov ?? rr.GetString(1));
                    roleName = rr.GetString(2);
                }
            }

            if (status == "approved" && mods.Length == 0)
            {
                status = "denied";
                fail = "no_role";
            }

            if (status == "approved" && approvedId > 0)
            {
                bool mine;
                await using (var claim = new MySqlCommand(
                    "UPDATE auth_challenges SET login_recorded=1 WHERE id=@i AND login_recorded=0;", conn))
                {
                    claim.Parameters.AddWithValue("@i", id);
                    mine = await claim.ExecuteNonQueryAsync() == 1;
                }
                if (mine)
                {
                    await using var tc = new MySqlConnection(
                        TenantContext.BuildTenantConn(mysqlHost, mysqlPort, chalSlug));
                    await tc.OpenAsync();
                    await RecordDesktopLoginAsync(tc, approvedId, "fingerprint", "");
                }
            }

            return Results.Ok(new
            {
                status,
                name = approvedName,
                userId = approvedId,
                failReason = fail,
                role = roleName,
                modules = mods,
                profileToken = status == "approved" && approvedId > 0
                    ? ProfileToken.Issue(chalSlug, approvedId) : ""
            });
        });

        app.MapGet("/api/agency/desktop/profile-login/required", async (HttpContext ctx) =>
        {
            var agency = VerifyAgencyBearer(ctx);
            if (agency is not { } ag) return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, ag.slug));
            await conn.OpenAsync();
            long logins = 0, roles = 0;
            await using (var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM app_users WHERE (profile_password_hash IS NOT NULL AND profile_password_hash <> '') " +
                "OR COALESCE(fingerprint_required,0)=1", conn) { CommandTimeout = 15 })
            {
                logins = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            try
            {
                await using var rc = new MySqlCommand(
                    "SELECT COUNT(*) FROM roles WHERE is_superadmin=0", conn) { CommandTimeout = 15 };
                roles = Convert.ToInt64(await rc.ExecuteScalarAsync());
            }
            catch { roles = 0; }

            return Results.Ok(new
            {
                required = logins > 0,
                profiles = logins,
                moduleGating = roles > 0,
                roles
            });
        });

        app.MapGet("/api/agency/desktop/profile-login/methods", async (HttpContext ctx, string? mobile) =>
        {
            var agency = VerifyAgencyBearer(ctx);
            if (agency is not { } ag) return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            string m = new string((mobile ?? "").Where(char.IsDigit).ToArray());
            if (m.Length < 10) return Results.BadRequest(new { message = "Enter a 10-digit mobile number." });
            m = m.Substring(m.Length - 10);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, ag.slug));
            await conn.OpenAsync();

            long id = 0; string name = "";
            bool active = false, stopped = false, blacklisted = false, fpRequired = false, hasPw = false;
            await using (var cmd = new MySqlCommand(@"
                SELECT id, COALESCE(name,''),
                       (COALESCE(profile_password_hash,'') <> ''),
                       COALESCE(is_active,0), COALESCE(is_stopped,0), COALESCE(is_blacklisted,0),
                       COALESCE(fingerprint_required,0)
                  FROM app_users WHERE RIGHT(REPLACE(COALESCE(mobile,''),' ',''),10) = @m LIMIT 1;", conn)
            { CommandTimeout = 15 })
            {
                cmd.Parameters.AddWithValue("@m", m);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    id = rdr.GetInt64(0); name = rdr.GetString(1); hasPw = rdr.GetInt32(2) == 1;
                    active = rdr.GetInt32(3) == 1; stopped = rdr.GetInt32(4) == 1;
                    blacklisted = rdr.GetInt32(5) == 1; fpRequired = rdr.GetInt32(6) == 1;
                }
            }

            if (id == 0)
                return Results.Ok(new { found = false });

            bool enrolled = false;
            try
            {
                await using var kc = new MySqlCommand(
                    "SELECT COUNT(*) FROM device_keys WHERE user_id=@u AND revoked=0;", conn)
                { CommandTimeout = 15 };
                kc.Parameters.AddWithValue("@u", id);
                enrolled = Convert.ToInt64(await kc.ExecuteScalarAsync()) > 0;
            }
            catch { enrolled = false; }

            string block = blacklisted ? "This profile has been blacklisted."
                         : stopped     ? "This profile has been stopped."
                         : !active     ? "This profile is not active."
                         : "";

            return Results.Ok(new
            {
                found = true,
                name,
                allowed = block.Length == 0,
                blockReason = block,
                hasPassword = hasPw,
                fingerprintRequired = fpRequired,
                fingerprintEnrolled = enrolled,
            });
        });

        app.MapPost("/api/agency/desktop/profile-login", async (HttpContext ctx, HttpRequest req) =>
        {
            var agency = VerifyAgencyBearer(ctx);
            if (agency is not { } ag) return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            string mobile = new string((dto.GetValueOrDefault("mobile") ?? "").Where(char.IsDigit).ToArray());
            string pw     = dto.GetValueOrDefault("password") ?? "";
            if (mobile.Length < 10 || pw.Length == 0)
                return Results.BadRequest(new { message = "Enter your mobile number and password." });

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, ag.slug));
            await conn.OpenAsync();

            long id = 0; string name = "", hash = "";
            bool active = false, stopped = false, blacklisted = false, fpRequired = false, isSuper = false;
            string modulesCsv = "", roleName = "";
            string? overrideCsv = null;
            await using (var cmd = new MySqlCommand(@"
                SELECT id, COALESCE(name,''), COALESCE(profile_password_hash,''),
                       COALESCE(is_active,0), COALESCE(is_stopped,0), COALESCE(is_blacklisted,0),
                       COALESCE(fingerprint_required,0),
                       (SELECT r.is_superadmin FROM roles r WHERE r.id = app_users.role_id),
                       (SELECT COALESCE(r.modules,'') FROM roles r WHERE r.id = app_users.role_id),
                       (SELECT r.name FROM roles r WHERE r.id = app_users.role_id),
                       modules_override
                  FROM app_users WHERE RIGHT(REPLACE(COALESCE(mobile,''),' ',''),10) = @m LIMIT 1;", conn)
            { CommandTimeout = 15 })
            {
                cmd.Parameters.AddWithValue("@m", mobile.Substring(mobile.Length - 10));
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    id = rdr.GetInt64(0); name = rdr.GetString(1); hash = rdr.GetString(2);
                    active = rdr.GetInt32(3) == 1; stopped = rdr.GetInt32(4) == 1; blacklisted = rdr.GetInt32(5) == 1;
                    fpRequired = rdr.GetInt32(6) == 1;
                    isSuper = !rdr.IsDBNull(7) && rdr.GetInt32(7) == 1;
                    modulesCsv = rdr.IsDBNull(8) ? "" : rdr.GetString(8);
                    roleName = rdr.IsDBNull(9) ? "" : rdr.GetString(9);
                    overrideCsv = rdr.IsDBNull(10) ? null : rdr.GetString(10);
                }
            }

            if (id == 0 || hash.Length == 0 || !VerifyPassword(pw, hash))
                return Results.Json(new { message = "Wrong mobile number or password." }, statusCode: 401);
            if (blacklisted || stopped || !active)
                return Results.Json(new { message = "This profile is not allowed to sign in." }, statusCode: 403);
            if (fpRequired)
                return Results.Json(new { code = "fingerprint_required",
                    message = "This profile signs in with a fingerprint." }, statusCode: 409);

            var effective = Modules.Effective(isSuper, overrideCsv ?? modulesCsv);

            if (effective.Length > 0)
                await RecordDesktopLoginAsync(conn, id, "password", "");

            if (effective.Length == 0)
                return Results.Json(new
                {
                    code = "no_role",
                    message = overrideCsv is null && roleName.Length == 0
                        ? "No role has been assigned to this profile. Ask your administrator to set one in HRMS."
                        : "This profile has no modules enabled. Ask your administrator to update it in HRMS."
                }, statusCode: 403);

            return Results.Ok(new
            {
                ok = true, userId = id, name, role = roleName, modules = effective,
                profileToken = ProfileToken.Issue(ag.slug, id)
            });
        });

        app.MapGet("/api/agency/hrms/profiles/{id:long}/employment", async (HttpContext ctx, long id) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await EnsureEmploymentRow(conn, id);

            await using var cmd = new MySqlCommand(
                "SELECT hired_on, confirmed_on, exit_on, designation, department, employment_type, " +
                "COALESCE(reports_to,0), shift_start, shift_end, COALESCE(weekly_offs,''), " +
                "emergency_name, emergency_phone, blood_group, date_of_birth " +
                "FROM hrms_employment WHERE user_id=@u LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("@u", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Results.NotFound(new { message = "Not found." });

            string? D(int i) => r.IsDBNull(i) ? null : r.GetDateTime(i).ToString("yyyy-MM-dd");
            string T(int i) => r.IsDBNull(i) ? "" : r.GetTimeSpan(i).ToString(@"hh\:mm");

            return Results.Ok(new
            {
                hiredOn = D(0), confirmedOn = D(1), exitOn = D(2),
                designation = r.GetString(3), department = r.GetString(4),
                employmentType = r.GetString(5), reportsTo = r.GetInt64(6),
                shiftStart = T(7), shiftEnd = T(8), weeklyOffs = r.GetString(9),
                emergencyName = r.GetString(10), emergencyPhone = r.GetString(11),
                bloodGroup = r.GetString(12), dateOfBirth = D(13),
            });
        });

        app.MapPut("/api/agency/hrms/profiles/{id:long}/employment", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            string type = (dto.GetValueOrDefault("employmentType") ?? "full_time").Trim();
            if (type is not ("full_time" or "part_time" or "contract" or "probation")) type = "full_time";

            object Dt(string k)
            {
                var v = ParseIstDate(dto.GetValueOrDefault(k));
                return v.HasValue ? v.Value : (object)DBNull.Value;
            }
            object Tm(string k)
            {
                var raw = (dto.GetValueOrDefault(k) ?? "").Trim();
                return TimeSpan.TryParse(raw, out var t) ? t : (object)DBNull.Value;
            }
            string Str(string k, int max)
            {
                var v = (dto.GetValueOrDefault(k) ?? "").Trim();
                return v.Length > max ? v.Substring(0, max) : v;
            }

            var offs = new List<int>();
            foreach (var part in (dto.GetValueOrDefault("weeklyOffs") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(part.Trim(), out int wd) && wd >= 0 && wd <= 6 && !offs.Contains(wd)) offs.Add(wd);
            long.TryParse(dto.GetValueOrDefault("reportsTo") ?? "0", out long boss);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await EnsureEmploymentRow(conn, id);

            await using var cmd = new MySqlCommand(@"
                UPDATE hrms_employment SET
                    hired_on=@h, confirmed_on=@c, exit_on=@x, designation=@dg, department=@dp,
                    employment_type=@et, reports_to=@rt, shift_start=@ss, shift_end=@se,
                    weekly_offs=@wo, emergency_name=@en, emergency_phone=@ep,
                    blood_group=@bg, date_of_birth=@db
                 WHERE user_id=@u;", conn);
            cmd.Parameters.AddWithValue("@h", Dt("hiredOn"));
            cmd.Parameters.AddWithValue("@c", Dt("confirmedOn"));
            cmd.Parameters.AddWithValue("@x", Dt("exitOn"));
            cmd.Parameters.AddWithValue("@dg", Str("designation", 120));
            cmd.Parameters.AddWithValue("@dp", Str("department", 120));
            cmd.Parameters.AddWithValue("@et", type);
            cmd.Parameters.AddWithValue("@rt", boss > 0 ? boss : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ss", Tm("shiftStart"));
            cmd.Parameters.AddWithValue("@se", Tm("shiftEnd"));
            cmd.Parameters.AddWithValue("@wo", offs.Count > 0 ? string.Join(",", offs) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@en", Str("emergencyName", 190));
            cmd.Parameters.AddWithValue("@ep", Str("emergencyPhone", 20));
            cmd.Parameters.AddWithValue("@bg", Str("bloodGroup", 8));
            cmd.Parameters.AddWithValue("@db", Dt("dateOfBirth"));
            cmd.Parameters.AddWithValue("@u", id);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/agency/hrms/profiles/{id:long}/documents", async (HttpContext ctx, long id) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT id, title, kind, file_path, file_name, size_bytes, expires_on, uploaded_at, uploaded_by " +
                "FROM hrms_documents WHERE user_id=@u ORDER BY uploaded_at DESC;", conn);
            cmd.Parameters.AddWithValue("@u", id);
            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var exp = r.IsDBNull(6) ? (DateTime?)null : r.GetDateTime(6);
                list.Add(new
                {
                    id = r.GetInt64(0), title = r.GetString(1), kind = r.GetString(2),
                    url = $"https://api.crmrecoverysoftware.com/agency-uploads/{r.GetString(3).TrimStart('/')}",
                    fileName = r.GetString(4), sizeBytes = r.GetInt64(5),
                    expiresOn = exp?.ToString("yyyy-MM-dd"),
                    expired = exp.HasValue && exp.Value.Date < IstToday(),
                    uploadedAt = r.GetDateTime(7).ToString("dd MMM yyyy"),
                    uploadedBy = r.GetString(8),
                });
            }
            return Results.Ok(list);
        });

        app.MapPost("/api/agency/hrms/profiles/{id:long}/documents", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);
            if (!req.HasFormContentType)
                return Results.BadRequest(new { message = "Attach a file." });

            var form = await req.ReadFormAsync();
            var file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { message = "Attach a file." });
            if (file.Length > 15 * 1024 * 1024)
                return Results.BadRequest(new { message = "That file is larger than 15 MB." });

            string title = (form["title"].ToString() ?? "").Trim();
            if (title.Length < 2) title = Path.GetFileNameWithoutExtension(file.FileName);
            if (title.Length > 190) title = title.Substring(0, 190);
            string kind = (form["kind"].ToString() ?? "other").Trim().ToLowerInvariant();
            if (kind.Length == 0 || kind.Length > 40) kind = "other";

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext.Length > 8 || ext.Length == 0) ext = ".bin";
            if (ext is not (".pdf" or ".jpg" or ".jpeg" or ".png" or ".webp" or ".doc" or ".docx" or ".xls" or ".xlsx"))
                return Results.BadRequest(new { message = "Only PDF, image, Word or Excel files." });

            var relDir = Path.Combine("hrms-docs", slug);
            var absDir = Path.Combine(LOGO_DIR, relDir);
            Directory.CreateDirectory(absDir);
            var fname = $"{id}-{Guid.NewGuid():N}{ext}";
            await using (var fs = File.Create(Path.Combine(absDir, fname)))
                await file.CopyToAsync(fs);

            DateTime? expires = ParseIstDate(form["expiresOn"].ToString());

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "INSERT INTO hrms_documents (user_id, title, kind, file_path, file_name, size_bytes, expires_on, uploaded_at, uploaded_by) " +
                "VALUES (@u, @t, @k, @p, @n, @s, @e, @a, 'HRMS');", conn);
            cmd.Parameters.AddWithValue("@u", id);
            cmd.Parameters.AddWithValue("@t", title);
            cmd.Parameters.AddWithValue("@k", kind);
            cmd.Parameters.AddWithValue("@p", (relDir + "/" + fname).Replace('\\', '/'));
            cmd.Parameters.AddWithValue("@n", file.FileName.Length > 190 ? file.FileName.Substring(0, 190) : file.FileName);
            cmd.Parameters.AddWithValue("@s", file.Length);
            cmd.Parameters.AddWithValue("@e", expires.HasValue ? expires.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@a", IstNow());
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapDelete("/api/agency/hrms/documents/{docId:long}", async (HttpContext ctx, long docId) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            string rel = "";
            await using (var find = new MySqlCommand("SELECT file_path FROM hrms_documents WHERE id=@i LIMIT 1;", conn))
            {
                find.Parameters.AddWithValue("@i", docId);
                if (await find.ExecuteScalarAsync() is string fp) rel = fp;
            }
            await using (var del = new MySqlCommand("DELETE FROM hrms_documents WHERE id=@i;", conn))
            {
                del.Parameters.AddWithValue("@i", docId);
                await del.ExecuteNonQueryAsync();
            }
            if (rel.Length > 0)
            {
                try { File.Delete(Path.Combine(LOGO_DIR, rel.Replace('/', Path.DirectorySeparatorChar))); }
                catch { }
            }
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/agency/me/documents", async (HttpContext ctx) =>
        {
            var me = MeFromToken(ctx);
            if (me is not { } who) return Results.Json(new { message = "Sign in again." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, who.slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT id, title, kind, file_path, file_name, expires_on, uploaded_at " +
                "FROM hrms_documents WHERE user_id=@u ORDER BY uploaded_at DESC;", conn);
            cmd.Parameters.AddWithValue("@u", who.userId);
            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.GetInt64(0), title = r.GetString(1), kind = r.GetString(2),
                    url = $"https://api.crmrecoverysoftware.com/agency-uploads/{r.GetString(3).TrimStart('/')}",
                    fileName = r.GetString(4),
                    expiresOn = r.IsDBNull(5) ? "" : r.GetDateTime(5).ToString("yyyy-MM-dd"),
                    uploadedAt = r.GetDateTime(6).ToString("dd MMM yyyy"),
                });
            return Results.Ok(list);
        });

        app.MapGet("/api/agency/hrms/profiles/{id:long}/salary", async (HttpContext ctx, long id) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            var history = new List<object>();
            await using (var cmd = new MySqlCommand(
                "SELECT id, effective_from, basic, hra, conveyance, medical, special_allowance, " +
                "other_allowance, pf_applicable, esic_applicable, pt_applicable " +
                "FROM hrms_salary_structure WHERE user_id=@u ORDER BY effective_from DESC;", conn))
            {
                cmd.Parameters.AddWithValue("@u", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    decimal b = r.GetDecimal(2), h = r.GetDecimal(3), c = r.GetDecimal(4),
                            m = r.GetDecimal(5), sp = r.GetDecimal(6), o = r.GetDecimal(7);
                    history.Add(new
                    {
                        id = r.GetInt64(0),
                        effectiveFrom = r.GetDateTime(1).ToString("yyyy-MM-dd"),
                        basic = b, hra = h, conveyance = c, medical = m,
                        specialAllowance = sp, otherAllowance = o,
                        gross = b + h + c + m + sp + o,
                        pf = r.GetInt32(8) == 1, esic = r.GetInt32(9) == 1, pt = r.GetInt32(10) == 1,
                    });
                }
            }

            var advances = new List<object>();
            await using (var cmd = new MySqlCommand(
                "SELECT id, amount, recovered, per_month, given_on, reason, status " +
                "FROM hrms_advances WHERE user_id=@u ORDER BY given_on DESC;", conn))
            {
                cmd.Parameters.AddWithValue("@u", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    advances.Add(new
                    {
                        id = r.GetInt64(0), amount = r.GetDecimal(1), recovered = r.GetDecimal(2),
                        outstanding = r.GetDecimal(1) - r.GetDecimal(2),
                        perMonth = r.GetDecimal(3),
                        givenOn = r.GetDateTime(4).ToString("yyyy-MM-dd"),
                        reason = r.GetString(5), status = r.GetString(6),
                    });
            }

            return Results.Ok(new { history, advances });
        });

        app.MapPost("/api/agency/hrms/profiles/{id:long}/salary", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            var from = ParseIstDate(dto.GetValueOrDefault("effectiveFrom"));
            if (from is null) return Results.BadRequest(new { message = "Choose when this pay starts." });

            decimal N(string k)
            {
                decimal.TryParse(dto.GetValueOrDefault(k) ?? "0",
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v);
                return v < 0 ? 0 : Math.Round(v, 2);
            }
            bool B(string k) => (dto.GetValueOrDefault(k) ?? "") != "false";

            decimal basic = N("basic");
            if (basic <= 0) return Results.BadRequest(new { message = "Basic pay must be more than zero." });

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                INSERT INTO hrms_salary_structure
                    (user_id, effective_from, basic, hra, conveyance, medical, special_allowance,
                     other_allowance, pf_applicable, esic_applicable, pt_applicable, created_by)
                VALUES (@u, @f, @b, @h, @c, @m, @s, @o, @pf, @es, @pt, 'HRMS')
                ON DUPLICATE KEY UPDATE basic=VALUES(basic), hra=VALUES(hra),
                    conveyance=VALUES(conveyance), medical=VALUES(medical),
                    special_allowance=VALUES(special_allowance), other_allowance=VALUES(other_allowance),
                    pf_applicable=VALUES(pf_applicable), esic_applicable=VALUES(esic_applicable),
                    pt_applicable=VALUES(pt_applicable);", conn);
            cmd.Parameters.AddWithValue("@u", id);
            cmd.Parameters.AddWithValue("@f", from.Value);
            cmd.Parameters.AddWithValue("@b", basic);
            cmd.Parameters.AddWithValue("@h", N("hra"));
            cmd.Parameters.AddWithValue("@c", N("conveyance"));
            cmd.Parameters.AddWithValue("@m", N("medical"));
            cmd.Parameters.AddWithValue("@s", N("specialAllowance"));
            cmd.Parameters.AddWithValue("@o", N("otherAllowance"));
            cmd.Parameters.AddWithValue("@pf", B("pf") ? 1 : 0);
            cmd.Parameters.AddWithValue("@es", B("esic") ? 1 : 0);
            cmd.Parameters.AddWithValue("@pt", B("pt") ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/agency/hrms/profiles/{id:long}/advances", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            decimal.TryParse(dto.GetValueOrDefault("amount") ?? "0",
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal amount);
            decimal.TryParse(dto.GetValueOrDefault("perMonth") ?? "0",
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal per);
            if (amount <= 0) return Results.BadRequest(new { message = "Enter the advance amount." });
            if (per <= 0) per = amount;

            var given = ParseIstDate(dto.GetValueOrDefault("givenOn")) ?? IstToday();
            string reason = (dto.GetValueOrDefault("reason") ?? "").Trim();
            if (reason.Length > 400) reason = reason.Substring(0, 400);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "INSERT INTO hrms_advances (user_id, amount, per_month, given_on, reason, created_by) " +
                "VALUES (@u, @a, @p, @g, @r, 'HRMS');", conn);
            cmd.Parameters.AddWithValue("@u", id);
            cmd.Parameters.AddWithValue("@a", Math.Round(amount, 2));
            cmd.Parameters.AddWithValue("@p", Math.Round(per, 2));
            cmd.Parameters.AddWithValue("@g", given);
            cmd.Parameters.AddWithValue("@r", reason);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/agency/hrms/profiles/{id:long}/incentive", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            if (!int.TryParse(dto.GetValueOrDefault("year"), out int year) ||
                !int.TryParse(dto.GetValueOrDefault("month"), out int month) ||
                month < 1 || month > 12)
                return Results.BadRequest(new { message = "Pick the month." });
            decimal.TryParse(dto.GetValueOrDefault("amount") ?? "0",
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal amount);
            string note = (dto.GetValueOrDefault("note") ?? "").Trim();
            if (note.Length > 400) note = note.Substring(0, 400);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "INSERT INTO hrms_incentives (user_id, year, month, amount, note, added_by) " +
                "VALUES (@u, @y, @m, @a, @n, 'HRMS') " +
                "ON DUPLICATE KEY UPDATE amount=VALUES(amount), note=VALUES(note);", conn);
            cmd.Parameters.AddWithValue("@u", id);
            cmd.Parameters.AddWithValue("@y", year);
            cmd.Parameters.AddWithValue("@m", month);
            cmd.Parameters.AddWithValue("@a", Math.Round(amount, 2));
            cmd.Parameters.AddWithValue("@n", note);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/agency/hrms/payroll", async (HttpContext ctx, int? year, int? month) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            int y = year ?? IstToday().Year, m = month ?? IstToday().Month;
            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            long runId = 0; string status = "none", genAt = "";
            await using (var cmd = new MySqlCommand(
                "SELECT id, status, generated_at FROM hrms_payroll_runs WHERE year=@y AND month=@m LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@y", y);
                cmd.Parameters.AddWithValue("@m", m);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    runId = r.GetInt64(0); status = r.GetString(1);
                    genAt = r.GetDateTime(2).ToString("dd MMM yyyy, HH:mm");
                }
            }

            var slips = new List<object>();
            decimal totGross = 0, totNet = 0, totDed = 0;
            if (runId > 0)
            {
                await using var cmd = new MySqlCommand(@"
                    SELECT p.id, p.user_id, COALESCE(u.name,''), COALESCE(u.mobile,''),
                           p.working_days, p.present_days, p.paid_leave_days, p.lop_days, p.paid_days,
                           p.gross, p.pf_employee, p.esic_employee, p.professional_tax,
                           p.advance_recovered, p.incentive, p.total_deduction, p.net_pay
                      FROM hrms_payslips p JOIN app_users u ON u.id = p.user_id
                     WHERE p.run_id=@r ORDER BY COALESCE(u.name,'');", conn);
                cmd.Parameters.AddWithValue("@r", runId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    totGross += r.GetDecimal(9); totDed += r.GetDecimal(15); totNet += r.GetDecimal(16);
                    slips.Add(new
                    {
                        id = r.GetInt64(0), userId = r.GetInt64(1),
                        name = r.GetString(2), mobile = r.GetString(3),
                        workingDays = r.GetDecimal(4), presentDays = r.GetDecimal(5),
                        paidLeaveDays = r.GetDecimal(6), lopDays = r.GetDecimal(7),
                        paidDays = r.GetDecimal(8), gross = r.GetDecimal(9),
                        pf = r.GetDecimal(10), esic = r.GetDecimal(11), pt = r.GetDecimal(12),
                        advance = r.GetDecimal(13), incentive = r.GetDecimal(14),
                        deductions = r.GetDecimal(15), netPay = r.GetDecimal(16),
                    });
                }
            }

            return Results.Ok(new
            {
                year = y, month = m,
                label = new DateTime(y, m, 1).ToString("MMMM yyyy"),
                runId, status, generatedAt = genAt, slips,
                totals = new { gross = totGross, deductions = totDed, net = totNet, staff = slips.Count },
            });
        });

        app.MapPost("/api/agency/hrms/payroll/generate", async (HttpContext ctx, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            if (!int.TryParse(dto.GetValueOrDefault("year"), out int y) ||
                !int.TryParse(dto.GetValueOrDefault("month"), out int m) || m < 1 || m > 12)
                return Results.BadRequest(new { message = "Pick the month." });

            var first = new DateTime(y, m, 1);
            var last = first.AddMonths(1).AddDays(-1);
            if (first > IstToday()) return Results.BadRequest(new { message = "That month has not started." });

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            await using (var chk = new MySqlCommand(
                "SELECT status FROM hrms_payroll_runs WHERE year=@y AND month=@m LIMIT 1;", conn))
            {
                chk.Parameters.AddWithValue("@y", y);
                chk.Parameters.AddWithValue("@m", m);
                if (await chk.ExecuteScalarAsync() is string st && st == "finalised")
                    return Results.BadRequest(new { message = "That month is finalised and cannot be run again." });
            }

            string defOffs = "0";
            decimal pfEmpPct = 12m, pfErPct = 13m, esicEmpPct = 0.75m, esicErPct = 3.25m, ptAmt = 200m;
            int pfCeil = 15000, esicCeil = 21000;
            bool pfLimit = true, ptOn = true;
            await using (var cmd = new MySqlCommand(
                "SELECT weekly_offs, pf_employee_pct, pf_employer_pct, pf_wage_ceiling, pf_limit_to_ceiling, " +
                "esic_employee_pct, esic_employer_pct, esic_wage_ceiling, pt_amount, pt_enabled " +
                "FROM hrms_settings WHERE id=1 LIMIT 1;", conn))
            {
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    defOffs = r.GetString(0);
                    pfEmpPct = r.GetDecimal(1); pfErPct = r.GetDecimal(2);
                    pfCeil = r.GetInt32(3); pfLimit = r.GetInt32(4) == 1;
                    esicEmpPct = r.GetDecimal(5); esicErPct = r.GetDecimal(6);
                    esicCeil = r.GetInt32(7); ptAmt = r.GetDecimal(8); ptOn = r.GetInt32(9) == 1;
                }
            }

            var holidays = new HashSet<DateTime>();
            await using (var cmd = new MySqlCommand(
                "SELECT holiday_date FROM hrms_holidays WHERE holiday_date BETWEEN @a AND @b;", conn))
            {
                cmd.Parameters.AddWithValue("@a", first);
                cmd.Parameters.AddWithValue("@b", last);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) holidays.Add(r.GetDateTime(0).Date);
            }

            long runId;
            await using (var cmd = new MySqlCommand(
                "INSERT INTO hrms_payroll_runs (year, month, status, generated_at, generated_by) " +
                "VALUES (@y, @m, 'draft', @t, 'HRMS') " +
                "ON DUPLICATE KEY UPDATE status='draft', generated_at=VALUES(generated_at); " +
                "SELECT id FROM hrms_payroll_runs WHERE year=@y AND month=@m LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@y", y);
                cmd.Parameters.AddWithValue("@m", m);
                cmd.Parameters.AddWithValue("@t", IstNow());
                runId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            await using (var wipe = new MySqlCommand("DELETE FROM hrms_payslips WHERE run_id=@r;", conn))
            {
                wipe.Parameters.AddWithValue("@r", runId);
                await wipe.ExecuteNonQueryAsync();
            }

            var staff = new List<(long id, string offs)>();
            await using (var cmd = new MySqlCommand(
                "SELECT u.id, COALESCE(e.weekly_offs,'') FROM app_users u " +
                "LEFT JOIN hrms_employment e ON e.user_id=u.id " +
                "WHERE COALESCE(u.is_blacklisted,0)=0 AND (e.exit_on IS NULL OR e.exit_on >= @a);", conn))
            {
                cmd.Parameters.AddWithValue("@a", first);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) staff.Add((r.GetInt64(0), r.GetString(1)));
            }

            int made = 0, skipped = 0;
            foreach (var (uid, ownOffs) in staff)
            {
                decimal basic = 0, hra = 0, conv = 0, med = 0, spec = 0, oth = 0;
                bool pfOn = true, esicOn = true, ptApplies = true;
                bool hasStructure = false;
                await using (var cmd = new MySqlCommand(
                    "SELECT basic, hra, conveyance, medical, special_allowance, other_allowance, " +
                    "pf_applicable, esic_applicable, pt_applicable FROM hrms_salary_structure " +
                    "WHERE user_id=@u AND effective_from <= @d ORDER BY effective_from DESC LIMIT 1;", conn))
                {
                    cmd.Parameters.AddWithValue("@u", uid);
                    cmd.Parameters.AddWithValue("@d", last);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        hasStructure = true;
                        basic = r.GetDecimal(0); hra = r.GetDecimal(1); conv = r.GetDecimal(2);
                        med = r.GetDecimal(3); spec = r.GetDecimal(4); oth = r.GetDecimal(5);
                        pfOn = r.GetInt32(6) == 1; esicOn = r.GetInt32(7) == 1; ptApplies = r.GetInt32(8) == 1;
                    }
                }
                if (!hasStructure) { skipped++; continue; }

                var offs = new HashSet<int>();
                foreach (var part in (ownOffs.Length > 0 ? ownOffs : defOffs)
                                     .Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(part.Trim(), out int wd)) offs.Add(wd);

                var present = new Dictionary<DateTime, string>();
                await using (var cmd = new MySqlCommand(
                    "SELECT work_date, COALESCE(status,'present') FROM attendance " +
                    "WHERE user_id=@u AND work_date BETWEEN @a AND @b;", conn))
                {
                    cmd.Parameters.AddWithValue("@u", uid);
                    cmd.Parameters.AddWithValue("@a", first);
                    cmd.Parameters.AddWithValue("@b", last);
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync()) present[r.GetDateTime(0).Date] = r.GetString(1);
                }

                var paidLeave = new HashSet<DateTime>();
                var unpaidLeave = new HashSet<DateTime>();
                await using (var cmd = new MySqlCommand(
                    "SELECT r.from_date, r.to_date, t.is_paid FROM hrms_leave_requests r " +
                    "JOIN hrms_leave_types t ON t.id=r.leave_type_id " +
                    "WHERE r.user_id=@u AND r.status='approved' AND r.to_date >= @a AND r.from_date <= @b;", conn))
                {
                    cmd.Parameters.AddWithValue("@u", uid);
                    cmd.Parameters.AddWithValue("@a", first);
                    cmd.Parameters.AddWithValue("@b", last);
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        var f = r.GetDateTime(0).Date; var t2 = r.GetDateTime(1).Date; bool paid = r.GetInt32(2) == 1;
                        for (var d = f; d <= t2; d = d.AddDays(1))
                        {
                            if (d < first || d > last) continue;
                            if (paid) paidLeave.Add(d); else unpaidLeave.Add(d);
                        }
                    }
                }

                decimal working = 0, presentDays = 0, paidLeaveDays = 0, weekoffDays = 0, holidayDays = 0;
                var upTo = last > IstToday() ? IstToday() : last;
                for (var d = first; d <= last; d = d.AddDays(1))
                {
                    if (holidays.Contains(d)) { holidayDays++; continue; }
                    if (offs.Contains((int)d.DayOfWeek)) { weekoffDays++; continue; }
                    working++;
                    if (d > upTo) continue;
                    if (paidLeave.Contains(d)) { paidLeaveDays++; continue; }
                    if (unpaidLeave.Contains(d)) continue;
                    if (present.TryGetValue(d, out var st))
                        presentDays += st == "halfday" ? 0.5m : 1m;
                }

                decimal lop = Math.Max(0, working - presentDays - paidLeaveDays);
                decimal paidDays = Math.Max(0, working - lop);
                decimal ratio = working > 0 ? paidDays / working : 0;

                decimal R(decimal v) => Math.Round(v * ratio, 2);
                decimal pBasic = R(basic), pHra = R(hra), pConv = R(conv),
                        pMed = R(med), pSpec = R(spec), pOth = R(oth);

                decimal incentive = 0;
                await using (var cmd = new MySqlCommand(
                    "SELECT amount FROM hrms_incentives WHERE user_id=@u AND year=@y AND month=@m LIMIT 1;", conn))
                {
                    cmd.Parameters.AddWithValue("@u", uid);
                    cmd.Parameters.AddWithValue("@y", y);
                    cmd.Parameters.AddWithValue("@m", m);
                    if (await cmd.ExecuteScalarAsync() is decimal inc) incentive = inc;
                }

                decimal gross = pBasic + pHra + pConv + pMed + pSpec + pOth + incentive;

                decimal pfBase = pfLimit ? Math.Min(pBasic, pfCeil * ratio) : pBasic;
                decimal pfEmp = pfOn ? Math.Round(pfBase * pfEmpPct / 100m, 2) : 0;
                decimal pfEr = pfOn ? Math.Round(pfBase * pfErPct / 100m, 2) : 0;

                decimal esicEmp = 0, esicEr = 0;
                if (esicOn && gross > 0 && gross <= esicCeil)
                {
                    esicEmp = Math.Round(gross * esicEmpPct / 100m, 2);
                    esicEr = Math.Round(gross * esicErPct / 100m, 2);
                }

                decimal pt = (ptOn && ptApplies && paidDays > 0) ? ptAmt : 0;

                decimal advance = 0;
                var advIds = new List<(long id, decimal take, decimal recovered, decimal amount)>();
                await using (var cmd = new MySqlCommand(
                    "SELECT id, amount, recovered, per_month FROM hrms_advances " +
                    "WHERE user_id=@u AND status='open' ORDER BY given_on;", conn))
                {
                    cmd.Parameters.AddWithValue("@u", uid);
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        decimal amt = r.GetDecimal(1), rec = r.GetDecimal(2), per = r.GetDecimal(3);
                        decimal take = Math.Min(per <= 0 ? amt : per, amt - rec);
                        if (take <= 0) continue;
                        advance += take;
                        advIds.Add((r.GetInt64(0), take, rec, amt));
                    }
                }

                decimal totalDed = pfEmp + esicEmp + pt + advance;
                decimal net = Math.Round(gross - totalDed, 2);

                await using (var cmd = new MySqlCommand(@"
                    INSERT INTO hrms_payslips
                        (run_id, user_id, working_days, present_days, paid_leave_days, weekoff_days,
                         holiday_days, lop_days, paid_days, basic, hra, conveyance, medical,
                         special_allowance, other_allowance, incentive, gross, pf_employee, pf_employer,
                         esic_employee, esic_employer, professional_tax, advance_recovered,
                         total_deduction, net_pay)
                    VALUES (@r, @u, @wd, @pd, @pl, @wo, @hd, @lop, @paid, @b, @h, @c, @m, @s, @o,
                            @inc, @g, @pfe, @pfr, @ese, @esr, @pt, @adv, @td, @net);", conn))
                {
                    cmd.Parameters.AddWithValue("@r", runId);
                    cmd.Parameters.AddWithValue("@u", uid);
                    cmd.Parameters.AddWithValue("@wd", working);
                    cmd.Parameters.AddWithValue("@pd", presentDays);
                    cmd.Parameters.AddWithValue("@pl", paidLeaveDays);
                    cmd.Parameters.AddWithValue("@wo", weekoffDays);
                    cmd.Parameters.AddWithValue("@hd", holidayDays);
                    cmd.Parameters.AddWithValue("@lop", lop);
                    cmd.Parameters.AddWithValue("@paid", paidDays);
                    cmd.Parameters.AddWithValue("@b", pBasic);
                    cmd.Parameters.AddWithValue("@h", pHra);
                    cmd.Parameters.AddWithValue("@c", pConv);
                    cmd.Parameters.AddWithValue("@m", pMed);
                    cmd.Parameters.AddWithValue("@s", pSpec);
                    cmd.Parameters.AddWithValue("@o", pOth);
                    cmd.Parameters.AddWithValue("@inc", incentive);
                    cmd.Parameters.AddWithValue("@g", gross);
                    cmd.Parameters.AddWithValue("@pfe", pfEmp);
                    cmd.Parameters.AddWithValue("@pfr", pfEr);
                    cmd.Parameters.AddWithValue("@ese", esicEmp);
                    cmd.Parameters.AddWithValue("@esr", esicEr);
                    cmd.Parameters.AddWithValue("@pt", pt);
                    cmd.Parameters.AddWithValue("@adv", advance);
                    cmd.Parameters.AddWithValue("@td", totalDed);
                    cmd.Parameters.AddWithValue("@net", net);
                    await cmd.ExecuteNonQueryAsync();
                }
                made++;
            }

            return Results.Ok(new { ok = true, runId, generated = made, skipped });
        });

        app.MapPost("/api/agency/hrms/payroll/{runId:long}/finalise", async (HttpContext ctx, long runId) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            await using (var upd = new MySqlCommand(
                "UPDATE hrms_payroll_runs SET status='finalised', finalised_at=@t " +
                "WHERE id=@r AND status='draft';", conn))
            {
                upd.Parameters.AddWithValue("@t", IstNow());
                upd.Parameters.AddWithValue("@r", runId);
                if (await upd.ExecuteNonQueryAsync() == 0)
                    return Results.BadRequest(new { message = "That run is already finalised." });
            }

            await using (var adv = new MySqlCommand(@"
                UPDATE hrms_advances a
                  JOIN hrms_payslips p ON p.user_id = a.user_id AND p.run_id = @r
                   SET a.recovered = LEAST(a.amount, a.recovered + LEAST(
                         CASE WHEN a.per_month <= 0 THEN a.amount ELSE a.per_month END,
                         a.amount - a.recovered))
                 WHERE a.status='open' AND p.advance_recovered > 0;", conn))
            {
                adv.Parameters.AddWithValue("@r", runId);
                await adv.ExecuteNonQueryAsync();
            }

            await using (var close = new MySqlCommand(
                "UPDATE hrms_advances SET status='closed' WHERE status='open' AND recovered >= amount;", conn))
                await close.ExecuteNonQueryAsync();

            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/agency/me/payslips", async (HttpContext ctx) =>
        {
            var me = MeFromToken(ctx);
            if (me is not { } who) return Results.Json(new { message = "Sign in again." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, who.slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT r.year, r.month, p.working_days, p.present_days, p.paid_leave_days, p.lop_days,
                       p.paid_days, p.basic, p.hra, p.conveyance, p.medical, p.special_allowance,
                       p.other_allowance, p.incentive, p.gross, p.pf_employee, p.esic_employee,
                       p.professional_tax, p.advance_recovered, p.total_deduction, p.net_pay
                  FROM hrms_payslips p JOIN hrms_payroll_runs r ON r.id = p.run_id
                 WHERE p.user_id=@u AND r.status='finalised'
                 ORDER BY r.year DESC, r.month DESC LIMIT 24;", conn);
            cmd.Parameters.AddWithValue("@u", who.userId);
            var list = new List<object>();
            await using var r2 = await cmd.ExecuteReaderAsync();
            while (await r2.ReadAsync())
                list.Add(new
                {
                    label = new DateTime(r2.GetInt32(0), r2.GetInt32(1), 1).ToString("MMMM yyyy"),
                    workingDays = r2.GetDecimal(2), presentDays = r2.GetDecimal(3),
                    paidLeaveDays = r2.GetDecimal(4), lopDays = r2.GetDecimal(5),
                    paidDays = r2.GetDecimal(6), basic = r2.GetDecimal(7), hra = r2.GetDecimal(8),
                    conveyance = r2.GetDecimal(9), medical = r2.GetDecimal(10),
                    specialAllowance = r2.GetDecimal(11), otherAllowance = r2.GetDecimal(12),
                    incentive = r2.GetDecimal(13), gross = r2.GetDecimal(14),
                    pf = r2.GetDecimal(15), esic = r2.GetDecimal(16), pt = r2.GetDecimal(17),
                    advance = r2.GetDecimal(18), deductions = r2.GetDecimal(19), netPay = r2.GetDecimal(20),
                });
            return Results.Ok(list);
        });

        app.MapGet("/api/agency/hrms/reports/attendance", async (HttpContext ctx, string? month) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var first = ParseIstMonth(month) ?? new DateTime(IstToday().Year, IstToday().Month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            var upTo = last > IstToday() ? IstToday() : last;

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            string defOffs = "0";
            await using (var cmd = new MySqlCommand("SELECT weekly_offs FROM hrms_settings WHERE id=1;", conn))
                if (await cmd.ExecuteScalarAsync() is string w) defOffs = w;

            var holidays = new HashSet<DateTime>();
            await using (var cmd = new MySqlCommand(
                "SELECT holiday_date FROM hrms_holidays WHERE holiday_date BETWEEN @a AND @b;", conn))
            {
                cmd.Parameters.AddWithValue("@a", first);
                cmd.Parameters.AddWithValue("@b", last);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) holidays.Add(r.GetDateTime(0).Date);
            }

            var rows = new List<object>();
            await using var main = new MySqlCommand(@"
                SELECT u.id, COALESCE(u.name,''), COALESCE(u.mobile,''), COALESCE(e.weekly_offs,''),
                       COALESCE(e.designation,'')
                  FROM app_users u LEFT JOIN hrms_employment e ON e.user_id=u.id
                 WHERE COALESCE(u.is_blacklisted,0)=0
                 ORDER BY COALESCE(u.name,'');", conn);
            var staff = new List<(long id, string name, string mobile, string offs, string desig)>();
            await using (var r = await main.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    staff.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));

            foreach (var st in staff)
            {
                var offs = new HashSet<int>();
                foreach (var part in (st.offs.Length > 0 ? st.offs : defOffs)
                                     .Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(part.Trim(), out int wd)) offs.Add(wd);

                int present = 0, half = 0, late = 0, worked = 0;
                await using (var cmd = new MySqlCommand(
                    "SELECT COALESCE(status,'present'), COALESCE(late_minutes,0), COALESCE(worked_minutes,0) " +
                    "FROM attendance WHERE user_id=@u AND work_date BETWEEN @a AND @b;", conn))
                {
                    cmd.Parameters.AddWithValue("@u", st.id);
                    cmd.Parameters.AddWithValue("@a", first);
                    cmd.Parameters.AddWithValue("@b", last);
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        if (r.GetString(0) == "halfday") half++; else present++;
                        if (r.GetInt32(1) > 0) late++;
                        worked += r.GetInt32(2);
                    }
                }

                int leaveDays = 0;
                await using (var cmd = new MySqlCommand(
                    "SELECT r.from_date, r.to_date FROM hrms_leave_requests r " +
                    "WHERE r.user_id=@u AND r.status='approved' AND r.to_date >= @a AND r.from_date <= @b;", conn))
                {
                    cmd.Parameters.AddWithValue("@u", st.id);
                    cmd.Parameters.AddWithValue("@a", first);
                    cmd.Parameters.AddWithValue("@b", last);
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        var f = r.GetDateTime(0).Date; var t = r.GetDateTime(1).Date;
                        for (var d = f; d <= t; d = d.AddDays(1))
                            if (d >= first && d <= last && !holidays.Contains(d) && !offs.Contains((int)d.DayOfWeek))
                                leaveDays++;
                    }
                }

                int working = 0;
                for (var d = first; d <= last; d = d.AddDays(1))
                    if (!holidays.Contains(d) && !offs.Contains((int)d.DayOfWeek)) working++;

                int elapsed = 0;
                for (var d = first; d <= upTo; d = d.AddDays(1))
                    if (!holidays.Contains(d) && !offs.Contains((int)d.DayOfWeek)) elapsed++;

                int absent = Math.Max(0, elapsed - present - half - leaveDays);

                rows.Add(new
                {
                    id = st.id, name = st.name, mobile = st.mobile, designation = st.desig,
                    workingDays = working, present, halfday = half, leave = leaveDays,
                    absent, late, workedHours = Math.Round(worked / 60.0, 1),
                });
            }

            return Results.Ok(new
            {
                month = first.ToString("yyyy-MM"),
                label = first.ToString("MMMM yyyy"),
                rows,
            });
        });

        app.MapGet("/api/agency/hrms/reports/leave", async (HttpContext ctx, int? year) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            int y = year ?? IstToday().Year;
            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            var types = new List<string>();
            await using (var cmd = new MySqlCommand(
                "SELECT name FROM hrms_leave_types WHERE active=1 ORDER BY id;", conn))
            {
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) types.Add(r.GetString(0));
            }

            await using var main = new MySqlCommand(@"
                SELECT u.id, COALESCE(u.name,''), COALESCE(u.mobile,''), t.name,
                       SUM(CASE WHEN r.status='approved' THEN r.days ELSE 0 END),
                       SUM(CASE WHEN r.status='pending'  THEN r.days ELSE 0 END),
                       SUM(CASE WHEN r.status='rejected' THEN r.days ELSE 0 END)
                  FROM hrms_leave_requests r
                  JOIN app_users u ON u.id = r.user_id
                  JOIN hrms_leave_types t ON t.id = r.leave_type_id
                 WHERE YEAR(r.from_date) = @y
                 GROUP BY u.id, t.name ORDER BY COALESCE(u.name,'');", conn);
            main.Parameters.AddWithValue("@y", y);

            var byUser = new Dictionary<long, Dictionary<string, object>>();
            await using (var r = await main.ExecuteReaderAsync())
                while (await r.ReadAsync())
                {
                    long uid = r.GetInt64(0);
                    if (!byUser.TryGetValue(uid, out var row))
                    {
                        row = new Dictionary<string, object>
                        {
                            ["id"] = uid, ["name"] = r.GetString(1), ["mobile"] = r.GetString(2),
                            ["approved"] = 0m, ["pending"] = 0m, ["rejected"] = 0m,
                        };
                        foreach (var t in types) row["t_" + t] = 0m;
                        byUser[uid] = row;
                    }
                    decimal ap = r.GetDecimal(4), pe = r.GetDecimal(5), re = r.GetDecimal(6);
                    row["t_" + r.GetString(3)] = ap;
                    row["approved"] = (decimal)row["approved"] + ap;
                    row["pending"] = (decimal)row["pending"] + pe;
                    row["rejected"] = (decimal)row["rejected"] + re;
                }

            return Results.Ok(new { year = y, types, rows = byUser.Values });
        });

        app.MapGet("/api/agency/hrms/leave-requests", async (HttpContext ctx, string? status) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            string want = (status ?? "pending").Trim().ToLowerInvariant();
            bool all = want == "all";

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT r.id, r.user_id, COALESCE(u.name,''), COALESCE(u.mobile,''), COALESCE(u.pfp,''),
                       t.name, t.is_paid, r.from_date, r.to_date, r.days, r.half_day, r.reason,
                       r.status, r.applied_at, COALESCE(r.decided_by,''), r.decided_at,
                       COALESCE(r.decision_note,'')
                  FROM hrms_leave_requests r
                  JOIN app_users u ON u.id = r.user_id
                  JOIN hrms_leave_types t ON t.id = r.leave_type_id
                 WHERE (@all = 1 OR r.status = @s)
                 ORDER BY r.status='pending' DESC, r.from_date DESC LIMIT 200;", conn) { CommandTimeout = 20 };
            cmd.Parameters.AddWithValue("@all", all ? 1 : 0);
            cmd.Parameters.AddWithValue("@s", want);

            var list = new List<object>();
            await using var r2 = await cmd.ExecuteReaderAsync();
            while (await r2.ReadAsync())
                list.Add(new
                {
                    id = r2.GetInt64(0), userId = r2.GetInt64(1),
                    name = r2.GetString(2), mobile = r2.GetString(3),
                    pfpUrl = PfpUrl(r2.GetString(4)),
                    type = r2.GetString(5), isPaid = r2.GetInt32(6) == 1,
                    from = r2.GetDateTime(7).ToString("yyyy-MM-dd"),
                    to = r2.GetDateTime(8).ToString("yyyy-MM-dd"),
                    days = r2.GetDecimal(9), halfDay = r2.GetString(10),
                    reason = r2.GetString(11), status = r2.GetString(12),
                    appliedAt = r2.GetDateTime(13).ToString("dd MMM, HH:mm"),
                    decidedBy = r2.GetString(14),
                    decidedAt = r2.IsDBNull(15) ? null : r2.GetDateTime(15).ToString("dd MMM, HH:mm"),
                    decisionNote = r2.GetString(16),
                });
            return Results.Ok(list);
        });

        app.MapPost("/api/agency/hrms/leave-requests/{id:long}", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            string decision = (dto.GetValueOrDefault("decision") ?? "").Trim().ToLowerInvariant();
            if (decision is not ("approved" or "rejected"))
                return Results.BadRequest(new { message = "Approve or reject." });
            string note = (dto.GetValueOrDefault("note") ?? "").Trim();
            if (note.Length > 500) note = note.Substring(0, 500);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            long userId = 0; int typeId = 0; decimal days = 0;
            await using (var find = new MySqlCommand(
                "SELECT user_id, leave_type_id, days FROM hrms_leave_requests WHERE id=@i AND status='pending' LIMIT 1;", conn))
            {
                find.Parameters.AddWithValue("@i", id);
                await using var r = await find.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return Results.BadRequest(new { message = "That request has already been decided." });
                userId = r.GetInt64(0); typeId = r.GetInt32(1); days = r.GetDecimal(2);
            }

            await using (var upd = new MySqlCommand(
                "UPDATE hrms_leave_requests SET status=@s, decided_by='HRMS', decided_at=@t, decision_note=@n " +
                "WHERE id=@i AND status='pending';", conn))
            {
                upd.Parameters.AddWithValue("@s", decision);
                upd.Parameters.AddWithValue("@t", IstNow());
                upd.Parameters.AddWithValue("@n", note);
                upd.Parameters.AddWithValue("@i", id);
                if (await upd.ExecuteNonQueryAsync() == 0)
                    return Results.BadRequest(new { message = "That request has already been decided." });
            }

            if (decision == "approved")
            {
                int year = IstToday().Year;
                await using var bal = new MySqlCommand(
                    "INSERT INTO hrms_leave_balances (user_id, leave_type_id, year, accrued, used) " +
                    "SELECT @u, @t, @y, t.annual_quota, @d FROM hrms_leave_types t WHERE t.id=@t " +
                    "ON DUPLICATE KEY UPDATE used = used + @d;", conn);
                bal.Parameters.AddWithValue("@u", userId);
                bal.Parameters.AddWithValue("@t", typeId);
                bal.Parameters.AddWithValue("@y", year);
                bal.Parameters.AddWithValue("@d", days);
                await bal.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { ok = true, decision });
        });

        app.MapGet("/api/agency/hrms/holidays", async (HttpContext ctx, int? year) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            int y = year ?? IstToday().Year;
            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT id, holiday_date, name, is_optional FROM hrms_holidays " +
                "WHERE YEAR(holiday_date)=@y ORDER BY holiday_date;", conn);
            cmd.Parameters.AddWithValue("@y", y);
            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.GetInt32(0),
                    date = r.GetDateTime(1).ToString("yyyy-MM-dd"),
                    day = r.GetDateTime(1).ToString("ddd"),
                    name = r.GetString(2),
                    optional = r.GetInt32(3) == 1,
                });
            return Results.Ok(new { year = y, holidays = list });
        });

        app.MapPost("/api/agency/hrms/holidays", async (HttpContext ctx, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            var date = ParseIstDate(dto.GetValueOrDefault("date"));
            string name = (dto.GetValueOrDefault("name") ?? "").Trim();
            if (date is null) return Results.BadRequest(new { message = "Pick a date." });
            if (name.Length < 2) return Results.BadRequest(new { message = "Name the holiday." });
            if (name.Length > 190) name = name.Substring(0, 190);
            bool optional = (dto.GetValueOrDefault("optional") ?? "") == "true";

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "INSERT INTO hrms_holidays (holiday_date, name, is_optional) VALUES (@d, @n, @o) " +
                "ON DUPLICATE KEY UPDATE name=VALUES(name), is_optional=VALUES(is_optional);", conn);
            cmd.Parameters.AddWithValue("@d", date.Value);
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@o", optional ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapDelete("/api/agency/hrms/holidays/{id:int}", async (HttpContext ctx, int id) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);
            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("DELETE FROM hrms_holidays WHERE id=@i;", conn);
            cmd.Parameters.AddWithValue("@i", id);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/agency/hrms/work-policy", async (HttpContext ctx) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT shift_start, shift_end, grace_minutes, half_day_minutes, full_day_minutes, weekly_offs " +
                "FROM hrms_settings WHERE id=1 LIMIT 1;", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Results.Ok(new { });
            return Results.Ok(new
            {
                shiftStart = r.GetTimeSpan(0).ToString(@"hh\:mm"),
                shiftEnd = r.GetTimeSpan(1).ToString(@"hh\:mm"),
                graceMinutes = r.GetInt32(2),
                halfDayMinutes = r.GetInt32(3),
                fullDayMinutes = r.GetInt32(4),
                weeklyOffs = r.GetString(5),
            });
        });

        app.MapPut("/api/agency/hrms/work-policy", async (HttpContext ctx, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            if (!TimeSpan.TryParse(dto.GetValueOrDefault("shiftStart") ?? "", out var start) ||
                !TimeSpan.TryParse(dto.GetValueOrDefault("shiftEnd") ?? "", out var end))
                return Results.BadRequest(new { message = "Enter the shift times as HH:MM." });
            if (!int.TryParse(dto.GetValueOrDefault("graceMinutes"), out int grace)) grace = 15;
            if (!int.TryParse(dto.GetValueOrDefault("halfDayMinutes"), out int halfMin)) halfMin = 240;
            if (!int.TryParse(dto.GetValueOrDefault("fullDayMinutes"), out int fullMin)) fullMin = 480;
            grace = Math.Clamp(grace, 0, 180);
            halfMin = Math.Clamp(halfMin, 30, 720);
            fullMin = Math.Clamp(fullMin, 60, 900);

            var offs = new List<int>();
            foreach (var part in (dto.GetValueOrDefault("weeklyOffs") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(part.Trim(), out int wd) && wd >= 0 && wd <= 6 && !offs.Contains(wd)) offs.Add(wd);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "UPDATE hrms_settings SET shift_start=@a, shift_end=@b, grace_minutes=@g, " +
                "half_day_minutes=@h, full_day_minutes=@f, weekly_offs=@w WHERE id=1;", conn);
            cmd.Parameters.AddWithValue("@a", start);
            cmd.Parameters.AddWithValue("@b", end);
            cmd.Parameters.AddWithValue("@g", grace);
            cmd.Parameters.AddWithValue("@h", halfMin);
            cmd.Parameters.AddWithValue("@f", fullMin);
            cmd.Parameters.AddWithValue("@w", string.Join(",", offs));
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/agency/me/profile", async (HttpContext ctx) =>
        {
            var me = MeFromToken(ctx);
            if (me is not { } who) return Results.Json(new { message = "Sign in again." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, who.slug));
            await conn.OpenAsync();
            await EnsureEmploymentRow(conn, who.userId);

            await using var cmd = new MySqlCommand(@"
                SELECT u.id, COALESCE(u.name,''), COALESCE(u.mobile,''), COALESCE(u.address,''),
                       COALESCE(u.pincode,''), COALESCE(u.pfp,''), COALESCE(u.is_active,0),
                       COALESCE(u.account_number,''), COALESCE(u.ifsc_code,''), COALESCE(u.balance,0),
                       COALESCE(u.kyc_status,''), COALESCE(u.kyc_aadhaar_name,''),
                       COALESCE(u.kyc_aadhaar_last4,''), COALESCE(u.kyc_aadhaar_dob,''),
                       COALESCE(u.kyc_aadhaar_gender,''), COALESCE(u.kyc_pan,''),
                       COALESCE(u.kyc_bank_holder,''), COALESCE(u.kyc_aadhaar_verified,0),
                       COALESCE(u.kyc_pan_verified,0), COALESCE(u.kyc_bank_verified,0),
                       u.created_at, COALESCE(r.name,''),
                       e.hired_on, e.confirmed_on, e.date_of_birth,
                       COALESCE(e.designation,''), COALESCE(e.department,''),
                       COALESCE(e.employment_type,'full_time'),
                       COALESCE(e.emergency_name,''), COALESCE(e.emergency_phone,''),
                       COALESCE(e.blood_group,''),
                       COALESCE(e.shift_start, s.shift_start), COALESCE(e.shift_end, s.shift_end),
                       COALESCE(e.weekly_offs, s.weekly_offs), s.grace_minutes,
                       COALESCE(m.name,'')
                  FROM app_users u
             LEFT JOIN roles r ON r.id = u.role_id
             LEFT JOIN hrms_employment e ON e.user_id = u.id
             LEFT JOIN hrms_employment e2 ON e2.user_id = u.id
             LEFT JOIN app_users m ON m.id = e2.reports_to
             CROSS JOIN hrms_settings s
                 WHERE u.id = @u LIMIT 1;", conn) { CommandTimeout = 20 };
            cmd.Parameters.AddWithValue("@u", who.userId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Profile not found." });

            string? D(int i) => rdr.IsDBNull(i) ? null : rdr.GetDateTime(i).ToString("yyyy-MM-dd");

            return Results.Ok(new
            {
                id = rdr.GetInt64(0),
                name = rdr.GetString(1),
                mobile = rdr.GetString(2),
                address = rdr.GetString(3),
                pincode = rdr.GetString(4),
                pfpUrl = PfpUrl(rdr.GetString(5)),
                isActive = rdr.GetInt32(6) == 1,
                accountNumber = rdr.GetString(7),
                ifsc = rdr.GetString(8),
                balance = rdr.GetDecimal(9),
                kycStatus = rdr.GetString(10),
                kycName = rdr.GetString(11),
                kycAadhaarLast4 = rdr.GetString(12),
                kycDob = rdr.GetString(13),
                kycGender = rdr.GetString(14),
                kycPan = rdr.GetString(15),
                kycBankHolder = rdr.GetString(16),
                kycAadhaarVerified = rdr.GetInt32(17) == 1,
                kycPanVerified = rdr.GetInt32(18) == 1,
                kycBankVerified = rdr.GetInt32(19) == 1,
                joinedApp = D(20),
                role = rdr.GetString(21),
                hiredOn = D(22),
                confirmedOn = D(23),
                dateOfBirth = D(24),
                designation = rdr.GetString(25),
                department = rdr.GetString(26),
                employmentType = rdr.GetString(27),
                emergencyName = rdr.GetString(28),
                emergencyPhone = rdr.GetString(29),
                bloodGroup = rdr.GetString(30),
                shiftStart = rdr.IsDBNull(31) ? "" : rdr.GetTimeSpan(31).ToString(@"hh\:mm"),
                shiftEnd = rdr.IsDBNull(32) ? "" : rdr.GetTimeSpan(32).ToString(@"hh\:mm"),
                weeklyOffs = rdr.IsDBNull(33) ? "" : rdr.GetString(33),
                graceMinutes = rdr.GetInt32(34),
                reportsTo = rdr.GetString(35),
            });
        });

        app.MapGet("/api/agency/me/attendance", async (HttpContext ctx, string? month) =>
        {
            var me = MeFromToken(ctx);
            if (me is not { } who) return Results.Json(new { message = "Sign in again." }, statusCode: 401);

            var first = ParseIstMonth(month) ?? new DateTime(IstToday().Year, IstToday().Month, 1);
            var last = first.AddMonths(1).AddDays(-1);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, who.slug));
            await conn.OpenAsync();

            string weeklyOffs = "0";
            int graceMinutes = 15, fullDay = 480, halfDay = 240;
            TimeSpan shiftStart = new TimeSpan(9, 30, 0);
            await using (var cs = new MySqlCommand(
                "SELECT COALESCE(e.weekly_offs, s.weekly_offs), s.grace_minutes, s.full_day_minutes, " +
                "s.half_day_minutes, COALESCE(e.shift_start, s.shift_start) " +
                "FROM hrms_settings s LEFT JOIN hrms_employment e ON e.user_id=@u LIMIT 1;", conn))
            {
                cs.Parameters.AddWithValue("@u", who.userId);
                await using var r = await cs.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    weeklyOffs = r.IsDBNull(0) ? "0" : r.GetString(0);
                    graceMinutes = r.GetInt32(1);
                    fullDay = r.GetInt32(2);
                    halfDay = r.GetInt32(3);
                    if (!r.IsDBNull(4)) shiftStart = r.GetTimeSpan(4);
                }
            }

            var holidays = new Dictionary<DateTime, string>();
            await using (var hc = new MySqlCommand(
                "SELECT holiday_date, name FROM hrms_holidays WHERE holiday_date BETWEEN @a AND @b;", conn))
            {
                hc.Parameters.AddWithValue("@a", first);
                hc.Parameters.AddWithValue("@b", last);
                await using var r = await hc.ExecuteReaderAsync();
                while (await r.ReadAsync()) holidays[r.GetDateTime(0)] = r.GetString(1);
            }

            var marks = new Dictionary<DateTime, (string status, DateTime? inAt, DateTime? outAt, int worked, int late, string src)>();
            await using (var ac = new MySqlCommand(
                "SELECT work_date, COALESCE(status,'present'), check_in, check_out, " +
                "COALESCE(worked_minutes,0), COALESCE(late_minutes,0), COALESCE(source,'') " +
                "FROM attendance WHERE user_id=@u AND work_date BETWEEN @a AND @b;", conn))
            {
                ac.Parameters.AddWithValue("@u", who.userId);
                ac.Parameters.AddWithValue("@a", first);
                ac.Parameters.AddWithValue("@b", last);
                await using var r = await ac.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    marks[r.GetDateTime(0)] = (
                        r.GetString(1),
                        r.IsDBNull(2) ? null : r.GetDateTime(2),
                        r.IsDBNull(3) ? null : r.GetDateTime(3),
                        r.GetInt32(4), r.GetInt32(5), r.GetString(6));
            }

            var logins = new Dictionary<DateTime, (DateTime firstAt, DateTime lastAt, int count)>();
            await using (var lc = new MySqlCommand(
                "SELECT work_date, MIN(at), MAX(at), COUNT(*) FROM desktop_logins " +
                "WHERE user_id=@u AND work_date BETWEEN @a AND @b GROUP BY work_date;", conn))
            {
                lc.Parameters.AddWithValue("@u", who.userId);
                lc.Parameters.AddWithValue("@a", first);
                lc.Parameters.AddWithValue("@b", last);
                await using var r = await lc.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    logins[r.GetDateTime(0)] = (r.GetDateTime(1), r.GetDateTime(2), r.GetInt32(3));
            }

            var leaveDays = new Dictionary<DateTime, string>();
            await using (var lv = new MySqlCommand(
                "SELECT r.from_date, r.to_date, t.name FROM hrms_leave_requests r " +
                "JOIN hrms_leave_types t ON t.id = r.leave_type_id " +
                "WHERE r.user_id=@u AND r.status='approved' AND r.to_date >= @a AND r.from_date <= @b;", conn))
            {
                lv.Parameters.AddWithValue("@u", who.userId);
                lv.Parameters.AddWithValue("@a", first);
                lv.Parameters.AddWithValue("@b", last);
                await using var r = await lv.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var f = r.GetDateTime(0); var t = r.GetDateTime(1); var nm = r.GetString(2);
                    for (var d = f; d <= t; d = d.AddDays(1)) leaveDays[d] = nm;
                }
            }

            var offs = new HashSet<int>();
            foreach (var part in (weeklyOffs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(part.Trim(), out int wd)) offs.Add(wd);

            var days = new List<object>();
            int present = 0, absent = 0, half = 0, onLeave = 0, offCount = 0, holCount = 0, lateCount = 0, workedTotal = 0;
            var today = IstToday();

            for (var d = first; d <= last; d = d.AddDays(1))
            {
                bool future = d > today;
                string status;
                string note = "";
                int worked = 0, late = 0;
                string inAt = "", outAt = "";
                int loginCount = 0;

                if (logins.TryGetValue(d, out var lg))
                {
                    inAt = lg.firstAt.ToString("HH:mm");
                    outAt = lg.lastAt.ToString("HH:mm");
                    loginCount = lg.count;
                    worked = (int)(lg.lastAt - lg.firstAt).TotalMinutes;
                    var sched = d.Add(shiftStart);
                    late = (int)Math.Max(0, (lg.firstAt - sched).TotalMinutes - graceMinutes);
                }

                if (marks.TryGetValue(d, out var mk))
                {
                    if (mk.inAt.HasValue) inAt = mk.inAt.Value.ToString("HH:mm");
                    if (mk.outAt.HasValue) outAt = mk.outAt.Value.ToString("HH:mm");
                    if (mk.worked > 0) worked = mk.worked;
                    if (mk.late > 0) late = mk.late;
                }

                if (holidays.TryGetValue(d, out var hname)) { status = "holiday"; note = hname; }
                else if (leaveDays.TryGetValue(d, out var lname)) { status = "leave"; note = lname; }
                else if (marks.TryGetValue(d, out var m2) && m2.status == "halfday") status = "halfday";
                else if (marks.ContainsKey(d)) status = "present";
                else if (offs.Contains((int)d.DayOfWeek)) status = "weekoff";
                else if (future) status = "upcoming";
                else status = "absent";

                if (status == "present" && worked > 0 && worked < halfDay) status = "halfday";

                switch (status)
                {
                    case "present": present++; workedTotal += worked; if (late > 0) lateCount++; break;
                    case "halfday": half++; workedTotal += worked; if (late > 0) lateCount++; break;
                    case "absent": absent++; break;
                    case "leave": onLeave++; break;
                    case "weekoff": offCount++; break;
                    case "holiday": holCount++; break;
                }

                days.Add(new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    day = d.ToString("ddd"),
                    status,
                    note,
                    checkIn = inAt,
                    checkOut = outAt,
                    logins = loginCount,
                    workedMinutes = worked,
                    lateMinutes = late,
                });
            }

            return Results.Ok(new
            {
                month = first.ToString("yyyy-MM"),
                label = first.ToString("MMMM yyyy"),
                days,
                summary = new
                {
                    present, absent, halfday = half, leave = onLeave,
                    weekoff = offCount, holiday = holCount, late = lateCount,
                    workedMinutes = workedTotal,
                    fullDayMinutes = fullDay,
                }
            });
        });

        app.MapGet("/api/agency/me/leaves", async (HttpContext ctx) =>
        {
            var me = MeFromToken(ctx);
            if (me is not { } who) return Results.Json(new { message = "Sign in again." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, who.slug));
            await conn.OpenAsync();

            int year = IstToday().Year;
            var types = new List<object>();
            await using (var tc = new MySqlCommand(@"
                SELECT t.id, t.code, t.name, t.annual_quota, t.is_paid,
                       COALESCE(b.opening,0) + COALESCE(b.accrued, t.annual_quota) AS entitled,
                       COALESCE(b.used,0)
                  FROM hrms_leave_types t
             LEFT JOIN hrms_leave_balances b ON b.leave_type_id = t.id AND b.user_id=@u AND b.year=@y
                 WHERE t.active = 1 ORDER BY t.id;", conn))
            {
                tc.Parameters.AddWithValue("@u", who.userId);
                tc.Parameters.AddWithValue("@y", year);
                await using var r = await tc.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    decimal entitled = r.GetDecimal(5), used = r.GetDecimal(6);
                    types.Add(new
                    {
                        id = r.GetInt32(0), code = r.GetString(1), name = r.GetString(2),
                        quota = r.GetDecimal(3), isPaid = r.GetInt32(4) == 1,
                        entitled, used, balance = entitled - used,
                    });
                }
            }

            var reqs = new List<object>();
            await using (var rc = new MySqlCommand(@"
                SELECT r.id, t.name, r.from_date, r.to_date, r.days, r.half_day, r.reason,
                       r.status, r.applied_at, COALESCE(r.decided_by,''), r.decided_at,
                       COALESCE(r.decision_note,'')
                  FROM hrms_leave_requests r
                  JOIN hrms_leave_types t ON t.id = r.leave_type_id
                 WHERE r.user_id=@u ORDER BY r.applied_at DESC LIMIT 60;", conn))
            {
                rc.Parameters.AddWithValue("@u", who.userId);
                await using var r = await rc.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    reqs.Add(new
                    {
                        id = r.GetInt64(0), type = r.GetString(1),
                        from = r.GetDateTime(2).ToString("yyyy-MM-dd"),
                        to = r.GetDateTime(3).ToString("yyyy-MM-dd"),
                        days = r.GetDecimal(4), halfDay = r.GetString(5),
                        reason = r.GetString(6), status = r.GetString(7),
                        appliedAt = r.GetDateTime(8).ToString("dd MMM yyyy, HH:mm"),
                        decidedBy = r.GetString(9),
                        decidedAt = r.IsDBNull(10) ? null : r.GetDateTime(10).ToString("dd MMM yyyy, HH:mm"),
                        decisionNote = r.GetString(11),
                    });
            }

            return Results.Ok(new { year, types, requests = reqs });
        });

        app.MapPost("/api/agency/me/leaves", async (HttpContext ctx, HttpRequest req) =>
        {
            var me = MeFromToken(ctx);
            if (me is not { } who) return Results.Json(new { message = "Sign in again." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            if (!int.TryParse(dto.GetValueOrDefault("leaveTypeId"), out int typeId) || typeId <= 0)
                return Results.BadRequest(new { message = "Choose a leave type." });

            var from = ParseIstDate(dto.GetValueOrDefault("from"));
            var to = ParseIstDate(dto.GetValueOrDefault("to"));
            if (from is null || to is null) return Results.BadRequest(new { message = "Choose the dates." });
            if (to < from) return Results.BadRequest(new { message = "The last day cannot be before the first." });
            if ((to.Value - from.Value).TotalDays > 90)
                return Results.BadRequest(new { message = "That is longer than 90 days." });

            string half = (dto.GetValueOrDefault("halfDay") ?? "none").Trim().ToLowerInvariant();
            if (half is not ("none" or "first" or "second")) half = "none";

            string reason = (dto.GetValueOrDefault("reason") ?? "").Trim();
            if (reason.Length < 3) return Results.BadRequest(new { message = "Give a reason for the leave." });
            if (reason.Length > 500) reason = reason.Substring(0, 500);

            decimal days = (decimal)(to.Value - from.Value).TotalDays + 1;
            if (half != "none" && days == 1) days = 0.5m;

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, who.slug));
            await conn.OpenAsync();

            await using (var clash = new MySqlCommand(
                "SELECT COUNT(*) FROM hrms_leave_requests WHERE user_id=@u AND status IN ('pending','approved') " +
                "AND to_date >= @f AND from_date <= @t;", conn))
            {
                clash.Parameters.AddWithValue("@u", who.userId);
                clash.Parameters.AddWithValue("@f", from.Value);
                clash.Parameters.AddWithValue("@t", to.Value);
                if (Convert.ToInt64(await clash.ExecuteScalarAsync()) > 0)
                    return Results.Conflict(new { message = "You already have a leave request covering those dates." });
            }

            await using var ins = new MySqlCommand(
                "INSERT INTO hrms_leave_requests (user_id, leave_type_id, from_date, to_date, days, half_day, reason, applied_at) " +
                "VALUES (@u, @t, @f, @o, @d, @h, @r, @a);", conn);
            ins.Parameters.AddWithValue("@u", who.userId);
            ins.Parameters.AddWithValue("@t", typeId);
            ins.Parameters.AddWithValue("@f", from.Value);
            ins.Parameters.AddWithValue("@o", to.Value);
            ins.Parameters.AddWithValue("@d", days);
            ins.Parameters.AddWithValue("@h", half);
            ins.Parameters.AddWithValue("@r", reason);
            ins.Parameters.AddWithValue("@a", IstNow());
            await ins.ExecuteNonQueryAsync();

            return Results.Ok(new { ok = true, days });
        });

        app.MapPost("/api/agency/me/leaves/{id:long}/cancel", async (HttpContext ctx, long id) =>
        {
            var me = MeFromToken(ctx);
            if (me is not { } who) return Results.Json(new { message = "Sign in again." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, who.slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "UPDATE hrms_leave_requests SET status='cancelled' " +
                "WHERE id=@i AND user_id=@u AND status='pending';", conn);
            cmd.Parameters.AddWithValue("@i", id);
            cmd.Parameters.AddWithValue("@u", who.userId);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                return Results.BadRequest(new { message = "That request can no longer be cancelled." });
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/agency/me/holidays", async (HttpContext ctx, int? year) =>
        {
            var me = MeFromToken(ctx);
            if (me is not { } who) return Results.Json(new { message = "Sign in again." }, statusCode: 401);

            int y = year ?? IstToday().Year;
            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, who.slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT holiday_date, name, is_optional FROM hrms_holidays " +
                "WHERE YEAR(holiday_date)=@y ORDER BY holiday_date;", conn);
            cmd.Parameters.AddWithValue("@y", y);
            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    date = r.GetDateTime(0).ToString("yyyy-MM-dd"),
                    day = r.GetDateTime(0).ToString("ddd"),
                    name = r.GetString(1),
                    optional = r.GetInt32(2) == 1,
                    past = r.GetDateTime(0) < IstToday(),
                });
            return Results.Ok(new { year = y, holidays = list });
        });

        app.MapGet("/api/agency/hrms/attendance", async (HttpContext ctx, string? date) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var day = ParseIstDate(date) ?? IstToday();
            if (day > IstToday())
                return Results.BadRequest(new { message = "That date has not started yet." });

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT u.id, COALESCE(u.name,''), COALESCE(u.mobile,''), COALESCE(u.pfp,''),
                       COALESCE(u.is_active,0), COALESCE(u.is_blacklisted,0),
                       a.id, a.marked_at, COALESCE(a.status,''), COALESCE(a.source,''),
                       COALESCE(a.location,''), COALESCE(a.marked_by,''),
                       (SELECT COUNT(*)  FROM desktop_logins l WHERE l.user_id=u.id AND l.work_date=@d),
                       (SELECT MIN(l.at) FROM desktop_logins l WHERE l.user_id=u.id AND l.work_date=@d),
                       (SELECT MAX(l.at) FROM desktop_logins l WHERE l.user_id=u.id AND l.work_date=@d)
                  FROM app_users u
             LEFT JOIN attendance a ON a.user_id = u.id AND a.work_date = @d
              ORDER BY COALESCE(u.name,'') ASC, u.id ASC LIMIT 1000;", conn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@d", day);

            var list = new List<object>();
            int present = 0;
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                bool marked = !rdr.IsDBNull(6);
                if (marked) present++;
                list.Add(new
                {
                    id = rdr.GetInt64(0),
                    name = rdr.GetString(1),
                    mobile = rdr.GetString(2),
                    pfpUrl = PfpUrl(rdr.GetString(3)),
                    isActive = rdr.GetInt32(4) == 1,
                    isBlacklisted = rdr.GetInt32(5) == 1,
                    marked,
                    markedAt = rdr.IsDBNull(7) ? null : rdr.GetDateTime(7).ToString("HH:mm"),
                    status = marked ? rdr.GetString(8) : "",
                    source = marked ? rdr.GetString(9) : "",
                    location = marked ? rdr.GetString(10) : "",
                    markedBy = marked ? rdr.GetString(11) : "",
                    logins = rdr.GetInt64(12),
                    firstLogin = rdr.IsDBNull(13) ? null : rdr.GetDateTime(13).ToString("HH:mm"),
                    lastLogin = rdr.IsDBNull(14) ? null : rdr.GetDateTime(14).ToString("HH:mm"),
                });
            }

            return Results.Ok(new
            {
                date = day.ToString("yyyy-MM-dd"),
                isToday = day == IstToday(),
                total = list.Count,
                present,
                absent = list.Count - present,
                staff = list,
            });
        });

        app.MapPost("/api/agency/hrms/attendance/{userId:long}", async (HttpContext ctx, long userId, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            var day = ParseIstDate(dto.GetValueOrDefault("date")) ?? IstToday();
            if (day > IstToday())
                return Results.BadRequest(new { message = "That date has not started yet." });

            string status = (dto.GetValueOrDefault("status") ?? "present").Trim().ToLowerInvariant();
            if (status is not ("present" or "halfday" or "leave"))
                return Results.BadRequest(new { message = "Unknown attendance status." });

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            await using (var chk = new MySqlCommand("SELECT COALESCE(is_blacklisted,0) FROM app_users WHERE id=@u LIMIT 1", conn))
            {
                chk.Parameters.AddWithValue("@u", userId);
                var v = await chk.ExecuteScalarAsync();
                if (v is null) return Results.NotFound(new { message = "Staff member not found." });
                if (Convert.ToInt32(v) == 1)
                    return Results.Json(new { message = "This staff member is blacklisted." }, statusCode: 403);
            }

            await using var cmd = new MySqlCommand(@"
                INSERT INTO attendance (user_id, work_date, marked_at, status, source, note, marked_by)
                VALUES (@u, @d, @t, @s, 'hrms', @n, 'HRMS')
                ON DUPLICATE KEY UPDATE status = VALUES(status), note = VALUES(note);", conn);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@d", day);
            cmd.Parameters.AddWithValue("@t", IstNow());
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@n", (object?)dto.GetValueOrDefault("note") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok(new { ok = true, date = day.ToString("yyyy-MM-dd"), status });
        });

        app.MapDelete("/api/agency/hrms/attendance/{userId:long}", async (HttpContext ctx, long userId, string? date) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var day = ParseIstDate(date) ?? IstToday();
            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("DELETE FROM attendance WHERE user_id=@u AND work_date=@d", conn);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@d", day);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/agency/hrms/modules", async (HttpContext ctx) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);
            return Results.Ok(Modules.All.Select(m => new { key = m.Key, label = m.Label, group = m.Group }));
        });

        app.MapGet("/api/agency/hrms/roles", async (HttpContext ctx) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT r.id, r.name, r.is_superadmin, COALESCE(r.modules,''), " +
                "(SELECT COUNT(*) FROM app_users u WHERE u.role_id = r.id) " +
                "FROM roles r ORDER BY r.is_superadmin DESC, r.name ASC;", conn);
            var list = new List<object>();
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                bool su = rdr.GetInt32(2) == 1;
                list.Add(new
                {
                    id = rdr.GetInt32(0),
                    name = rdr.GetString(1),
                    isSuperadmin = su,
                    modules = Modules.Effective(su, rdr.GetString(3)),
                    staff = rdr.GetInt64(4)
                });
            }
            return Results.Ok(list);
        });

        app.MapPost("/api/agency/hrms/roles", async (HttpContext ctx, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            int id = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var i) ? i : 0;
            string name = root.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "").Trim() : "";
            if (name.Length is < 2 or > 80)
                return Results.BadRequest(new { message = "Give the role a name of 2 to 80 characters." });

            var keys = new List<string>();
            if (root.TryGetProperty("modules", out var mEl) && mEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                keys.AddRange(mEl.EnumerateArray()
                    .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(e => e.GetString() ?? ""));
            string modules = Modules.Normalise(keys);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            if (id > 0)
            {
                await using var chk = new MySqlCommand("SELECT is_superadmin FROM roles WHERE id=@i LIMIT 1", conn);
                chk.Parameters.AddWithValue("@i", id);
                var v = await chk.ExecuteScalarAsync();
                if (v is null) return Results.NotFound(new { message = "Role not found." });
                if (Convert.ToInt32(v) == 1)
                    return Results.Json(new { message = "The Super Admin role always has every module and cannot be edited." },
                        statusCode: 409);

                await using var upd = new MySqlCommand(
                    "UPDATE roles SET name=@n, modules=@m, updated_at=@t WHERE id=@i AND is_superadmin=0", conn);
                upd.Parameters.AddWithValue("@n", name);
                upd.Parameters.AddWithValue("@m", modules);
                upd.Parameters.AddWithValue("@t", IstNow());
                upd.Parameters.AddWithValue("@i", id);
                try { await upd.ExecuteNonQueryAsync(); }
                catch (MySqlException e) when (e.Number == 1062)
                { return Results.Conflict(new { message = "Another role already uses that name." }); }
                return Results.Ok(new { ok = true, id });
            }

            await using var ins = new MySqlCommand(
                "INSERT INTO roles (name, is_superadmin, modules, created_at) VALUES (@n, 0, @m, @t); SELECT LAST_INSERT_ID();", conn);
            ins.Parameters.AddWithValue("@n", name);
            ins.Parameters.AddWithValue("@m", modules);
            ins.Parameters.AddWithValue("@t", IstNow());
            try
            {
                var newId = Convert.ToInt32(await ins.ExecuteScalarAsync());
                return Results.Ok(new { ok = true, id = newId });
            }
            catch (MySqlException e) when (e.Number == 1062)
            { return Results.Conflict(new { message = "A role with that name already exists." }); }
        });

        app.MapDelete("/api/agency/hrms/roles/{id:int}", async (HttpContext ctx, int id) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            await using (var chk = new MySqlCommand(
                "SELECT is_superadmin, (SELECT COUNT(*) FROM app_users u WHERE u.role_id=@i) FROM roles WHERE id=@i LIMIT 1", conn))
            {
                chk.Parameters.AddWithValue("@i", id);
                await using var rdr = await chk.ExecuteReaderAsync();
                if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Role not found." });
                if (rdr.GetInt32(0) == 1)
                    return Results.Json(new { message = "The Super Admin role cannot be deleted." }, statusCode: 409);
                if (rdr.GetInt64(1) > 0)
                    return Results.Json(new { message = "Move that role's staff to another role first." }, statusCode: 409);
            }

            await using var del = new MySqlCommand("DELETE FROM roles WHERE id=@i AND is_superadmin=0", conn);
            del.Parameters.AddWithValue("@i", id);
            await del.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/agency/hrms/profiles/{id:long}/modules", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;

            bool useRole = root.TryGetProperty("useRole", out var uEl) &&
                           uEl.ValueKind == System.Text.Json.JsonValueKind.True;

            string? csv = null;
            if (!useRole)
            {
                var keys = new List<string>();
                if (root.TryGetProperty("modules", out var mEl) && mEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    keys.AddRange(mEl.EnumerateArray()
                        .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                        .Select(e => e.GetString() ?? ""));
                csv = Modules.Normalise(keys);
            }

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("UPDATE app_users SET modules_override=@m WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@m", csv is null ? (object)DBNull.Value : csv);
            cmd.Parameters.AddWithValue("@id", id);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                return Results.NotFound(new { message = "Profile not found." });

            return Results.Ok(new { ok = true, hasOverride = csv is not null, modules = Modules.Split(csv) });
        });

        app.MapPost("/api/agency/hrms/profiles/{id:long}/role", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            int roleId = doc.RootElement.TryGetProperty("roleId", out var rEl) && rEl.TryGetInt32(out var r) ? r : 0;

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            if (roleId > 0)
            {
                await using var chk = new MySqlCommand("SELECT COUNT(*) FROM roles WHERE id=@r", conn);
                chk.Parameters.AddWithValue("@r", roleId);
                if (Convert.ToInt64(await chk.ExecuteScalarAsync()) == 0)
                    return Results.NotFound(new { message = "That role no longer exists." });
            }

            await using var cmd = new MySqlCommand("UPDATE app_users SET role_id=@r WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@r", roleId > 0 ? roleId : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                return Results.NotFound(new { message = "Profile not found." });

            return Results.Ok(new { ok = true, roleId });
        });

        app.MapGet("/api/agency/hrms/profiles", async (HttpContext ctx) =>
        {
            var a = await HrmsSessionSlug(masterConn, ctx);
            if (a is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, a));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT id, COALESCE(name,''), COALESCE(mobile,''), COALESCE(is_active,0),
                       COALESCE(is_admin,0), COALESCE(is_blacklisted,0), COALESCE(kyc_status,''),
                       last_seen, (profile_password_hash IS NOT NULL AND profile_password_hash <> '') AS has_pw,
                       profile_password_set_at, COALESCE(pfp,''),
                       COALESCE(fingerprint_required,0),
                       (SELECT COUNT(*) FROM device_keys k WHERE k.user_id=app_users.id AND k.revoked=0)
                  FROM app_users ORDER BY COALESCE(name,'') ASC, id ASC LIMIT 1000;", conn)
            { CommandTimeout = 30 };
            var list = new List<object>();
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new
                {
                    id = rdr.GetInt64(0),
                    name = rdr.GetString(1),
                    mobile = rdr.GetString(2),
                    isActive = rdr.GetInt32(3) == 1,
                    isAdmin = rdr.GetInt32(4) == 1,
                    isBlacklisted = rdr.GetInt32(5) == 1,
                    kycStatus = rdr.GetString(6),
                    lastSeen = rdr.IsDBNull(7) ? null : rdr.GetDateTime(7).ToString("yyyy-MM-dd HH:mm"),
                    hasPassword = rdr.GetInt64(8) == 1,
                    passwordSetAt = rdr.IsDBNull(9) ? null : rdr.GetDateTime(9).ToString("yyyy-MM-dd HH:mm"),
                    pfpUrl = PfpUrl(rdr.GetString(10)),
                    fingerprintRequired = rdr.GetInt32(11) == 1,
                    fingerprintEnrolled = rdr.GetInt64(12) > 0,
                });
            }
            return Results.Ok(list);
        });

        app.MapGet("/api/agency/hrms/profiles/{id:long}", async (HttpContext ctx, long id) =>
        {
            var a = await HrmsSessionSlug(masterConn, ctx);
            if (a is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, a));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT id, COALESCE(name,''), COALESCE(mobile,''), COALESCE(address,''), COALESCE(pincode,''),
                       COALESCE(is_active,0), COALESCE(is_admin,0), COALESCE(is_stopped,0), COALESCE(is_blacklisted,0),
                       COALESCE(balance,0), COALESCE(account_number,''), COALESCE(ifsc_code,''),
                       COALESCE(kyc_status,''), COALESCE(kyc_aadhaar_name,''), COALESCE(kyc_aadhaar_last4,''),
                       COALESCE(kyc_pan,''), COALESCE(kyc_bank_holder,''),
                       COALESCE(kyc_aadhaar_verified,0), COALESCE(kyc_pan_verified,0), COALESCE(kyc_bank_verified,0),
                       created_at, last_seen, COALESCE(kyc_reg_location,''),
                       (profile_password_hash IS NOT NULL AND profile_password_hash <> '') AS has_pw,
                       profile_password_set_at, COALESCE(profile_password_by,''), COALESCE(device_id,''),
                       COALESCE(pfp,''), COALESCE(fingerprint_required,0),
                       COALESCE(role_id,0),
                       (SELECT r.name FROM roles r WHERE r.id = app_users.role_id),
                       modules_override,
                       (SELECT r.is_superadmin FROM roles r WHERE r.id = app_users.role_id)
                  FROM app_users WHERE id=@id LIMIT 1;", conn) { CommandTimeout = 20 };
            cmd.Parameters.AddWithValue("@id", id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Profile not found." });

            string? D(int i) => rdr.IsDBNull(i) ? null : rdr.GetDateTime(i).ToString("yyyy-MM-dd HH:mm");
            return Results.Ok(new
            {
                id = rdr.GetInt64(0), name = rdr.GetString(1), mobile = rdr.GetString(2),
                address = rdr.GetString(3), pincode = rdr.GetString(4),
                isActive = rdr.GetInt32(5) == 1, isAdmin = rdr.GetInt32(6) == 1,
                isStopped = rdr.GetInt32(7) == 1, isBlacklisted = rdr.GetInt32(8) == 1,
                balance = rdr.GetDecimal(9), accountNumber = rdr.GetString(10), ifsc = rdr.GetString(11),
                kycStatus = rdr.GetString(12), kycName = rdr.GetString(13), kycAadhaarLast4 = rdr.GetString(14),
                kycPan = rdr.GetString(15), kycBankHolder = rdr.GetString(16),
                kycAadhaarVerified = rdr.GetInt32(17) == 1, kycPanVerified = rdr.GetInt32(18) == 1,
                kycBankVerified = rdr.GetInt32(19) == 1,
                createdAt = D(20), lastSeen = D(21), regLocation = rdr.GetString(22),
                hasPassword = rdr.GetInt64(23) == 1, passwordSetAt = D(24),
                passwordBy = rdr.GetString(25), hasDevice = rdr.GetString(26).Length > 0,
                pfpUrl = PfpUrl(rdr.GetString(27)),
                fingerprintRequired = rdr.GetInt32(28) == 1,
                roleId = rdr.GetInt32(29),
                roleName = rdr.IsDBNull(30) ? "" : rdr.GetString(30),
                hasOverride = !rdr.IsDBNull(31),
                modules = Modules.Effective(
                    !rdr.IsDBNull(32) && rdr.GetInt32(32) == 1,
                    rdr.IsDBNull(31) ? null : rdr.GetString(31)),
                overrideModules = rdr.IsDBNull(31) ? Array.Empty<string>() : Modules.Split(rdr.GetString(31)),
            });
        });

        app.MapGet("/api/agency/hrms/profiles/{id:long}/fingerprint", async (HttpContext ctx, long id) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT key_id, device_label, enrolled_at, last_used_at FROM device_keys " +
                "WHERE user_id=@u AND revoked=0 ORDER BY id DESC LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("@u", id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync())
                return Results.Ok(new { enrolled = false });

            return Results.Ok(new
            {
                enrolled = true,
                keyId = rdr.GetString(0),
                device = rdr.GetString(1),
                enrolledAt = rdr.GetDateTime(2).ToString("yyyy-MM-dd HH:mm"),
                lastUsedAt = rdr.IsDBNull(3) ? null : rdr.GetDateTime(3).ToString("yyyy-MM-dd HH:mm")
            });
        });

        app.MapPost("/api/agency/hrms/profiles/{id:long}/fingerprint", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            bool required = (dto.GetValueOrDefault("required") ?? "").Trim().ToLowerInvariant() is "1" or "true";

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();

            if (required)
            {
                await using var chk = new MySqlCommand(
                    "SELECT COUNT(*) FROM device_keys WHERE user_id=@u AND revoked=0", conn);
                chk.Parameters.AddWithValue("@u", id);
                if (Convert.ToInt64(await chk.ExecuteScalarAsync()) == 0)
                    return Results.Json(new { code = "not_enrolled",
                        message = "This person has not set up a fingerprint on their phone yet." }, statusCode: 409);
            }

            await using var cmd = new MySqlCommand(
                "UPDATE app_users SET fingerprint_required=@r WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@r", required ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", id);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                return Results.NotFound(new { message = "Profile not found." });

            return Results.Ok(new { ok = true, fingerprintRequired = required });
        });

        app.MapDelete("/api/agency/hrms/profiles/{id:long}/fingerprint", async (HttpContext ctx, long id) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "UPDATE device_keys SET revoked=1, revoked_at=@t WHERE user_id=@u AND revoked=0; " +
                "UPDATE app_users SET fingerprint_required=0 WHERE id=@u;", conn);
            cmd.Parameters.AddWithValue("@t", IstNow());
            cmd.Parameters.AddWithValue("@u", id);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true, enrolled = false, fingerprintRequired = false });
        });

        app.MapPost("/api/agency/hrms/profiles/{id:long}/password", async (HttpContext ctx, long id, HttpRequest req) =>
        {
            var a = await HrmsSessionSlug(masterConn, ctx);
            if (a is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            string pw = (dto.GetValueOrDefault("password") ?? "").Trim();
            bool clear = (dto.GetValueOrDefault("clear") ?? "").Trim().ToLowerInvariant() is "1" or "true";

            if (!clear && pw.Length < 4)
                return Results.BadRequest(new { message = "Use at least 4 characters." });
            if (!clear && pw.Length > 64)
                return Results.BadRequest(new { message = "That password is too long." });

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, a));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "UPDATE app_users SET profile_password_hash=@h, profile_password_set_at=@t, profile_password_by=@b WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@h", clear ? (object)DBNull.Value : HashPassword(pw));
            cmd.Parameters.AddWithValue("@t", clear ? (object)DBNull.Value : DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@b", clear ? (object)DBNull.Value : "HRMS");
            cmd.Parameters.AddWithValue("@id", id);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                return Results.NotFound(new { message = "Profile not found." });

            return Results.Ok(new { ok = true, hasPassword = !clear });
        });

        app.MapGet("/api/agency/hrms/me", async (HttpContext ctx) =>
        {
            var a = await HrmsSessionAgency(masterConn, ctx);
            if (a is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);
            return Results.Ok(a);
        });

        app.MapPost("/api/agency/hrms/qr-proximity", async (HttpContext ctx, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            string mode = (dto.GetValueOrDefault("mode") ?? "").Trim().ToLowerInvariant();
            if (mode is not ("off" or "warn" or "block"))
                return Results.BadRequest(new { message = "Unknown setting." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "UPDATE agencies SET qr_proximity=@m WHERE slug=@s;", conn);
            cmd.Parameters.AddWithValue("@m", mode);
            cmd.Parameters.AddWithValue("@s", slug);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true, mode });
        });

        app.MapPost("/api/agency/hrms/geofence", async (HttpContext ctx, HttpRequest req) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            var dto = await ReadJsonAsync(req);

            if ((dto.GetValueOrDefault("clear") ?? "") == "true")
            {
                await using var wipe = new MySqlConnection(masterConn);
                await wipe.OpenAsync();
                await using var wc = new MySqlCommand(
                    "UPDATE agencies SET geo_lat=NULL, geo_lng=NULL, geo_label='' WHERE slug=@s;", wipe);
                wc.Parameters.AddWithValue("@s", slug);
                await wc.ExecuteNonQueryAsync();
                return Results.Ok(new { ok = true, cleared = true });
            }

            if (!double.TryParse(dto.GetValueOrDefault("lat"), System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double lat) ||
                !double.TryParse(dto.GetValueOrDefault("lng"), System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double lng) ||
                lat < -90 || lat > 90 || lng < -180 || lng > 180 || (lat == 0 && lng == 0))
                return Results.BadRequest(new { message = "That is not a valid location." });

            if (!int.TryParse(dto.GetValueOrDefault("radius"), out int radius)) radius = 200;
            if (radius < 10) radius = 10;
            if (radius > 5000) radius = 5000;

            string label = (dto.GetValueOrDefault("label") ?? "").Trim();
            if (label.Length > 190) label = label.Substring(0, 190);

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "UPDATE agencies SET geo_lat=@la, geo_lng=@lo, geo_radius_m=@r, geo_label=@lb WHERE slug=@s;", conn);
            cmd.Parameters.AddWithValue("@la", lat);
            cmd.Parameters.AddWithValue("@lo", lng);
            cmd.Parameters.AddWithValue("@r", radius);
            cmd.Parameters.AddWithValue("@lb", label);
            cmd.Parameters.AddWithValue("@s", slug);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true, lat, lng, radius, label });
        });

        app.MapGet("/api/agency/hrms/staff-locations", async (HttpContext ctx) =>
        {
            var slug = await HrmsSessionSlug(masterConn, ctx);
            if (slug is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT id, COALESCE(name,''), COALESCE(mobile,''), last_lat, last_lng, last_seen
                  FROM app_users
                 WHERE last_lat IS NOT NULL AND last_lng IS NOT NULL
                   AND last_seen IS NOT NULL
                   AND last_seen > DATE_SUB(NOW(), INTERVAL 14 DAY)
                 ORDER BY last_seen DESC
                 LIMIT 60;", conn) { CommandTimeout = 15 };

            var rows = new List<object>();
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var seen = rdr.GetDateTime(5);
                var age = DateTime.Now - seen;
                string ago = age.TotalMinutes < 2   ? "just now"
                           : age.TotalMinutes < 60  ? (int)age.TotalMinutes + " min ago"
                           : age.TotalHours   < 24  ? (int)age.TotalHours + " h ago"
                                                    : (int)age.TotalDays + " d ago";
                rows.Add(new
                {
                    id     = rdr.GetInt64(0),
                    name   = rdr.GetString(1),
                    mobile = rdr.GetString(2),
                    lat    = rdr.GetDouble(3),
                    lng    = rdr.GetDouble(4),
                    ago,
                    seen   = seen.ToString("dd MMM, HH:mm"),
                });
            }
            return Results.Ok(rows);
        });

        app.MapGet("/api/agency/hrms/qr-attempts", async (HttpContext ctx) =>
        {
            var a = await HrmsSessionAgencyId(masterConn, ctx);
            if (a is null) return Results.Json(new { message = "Session expired." }, statusCode: 401);

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT COALESCE(approved_name,''), COALESCE(claim_mobile,''), status,
                       COALESCE(fail_reason,''), COALESCE(proximity,'unknown'),
                       COALESCE(device_label,''), created_at, distance_m
                  FROM auth_challenges
                 WHERE agency_id = @a
                 ORDER BY created_at DESC LIMIT 25;", conn);
            cmd.Parameters.AddWithValue("@a", a.Value);
            var rows = new List<object>();
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                rows.Add(new
                {
                    name       = rdr.GetString(0),
                    mobile     = rdr.GetString(1),
                    status     = rdr.GetString(2),
                    failReason = rdr.GetString(3),
                    proximity  = rdr.GetString(4),
                    device     = rdr.GetString(5),
                    at         = rdr.GetDateTime(6).AddMinutes(330).ToString("dd MMM, HH:mm"),
                    distanceM  = rdr.IsDBNull(7) ? (int?)null : rdr.GetInt32(7),
                });
            return Results.Ok(rows);
        });

        app.MapPost("/api/agency/hrms/logout", async (HttpContext ctx) =>
        {
            string token = ctx.Request.Headers["X-Hrms-Token"].FirstOrDefault() ?? "";
            if (token.Length > 0)
            {
                await using var conn = new MySqlConnection(masterConn);
                await conn.OpenAsync();
                await using var cmd = new MySqlCommand("UPDATE hrms_sessions SET revoked=1 WHERE token_hash=@t", conn);
                cmd.Parameters.AddWithValue("@t", Sha256Hex(token));
                await cmd.ExecuteNonQueryAsync();
            }
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/agency/register", async (HttpRequest req) =>
        {
            if (!req.HasFormContentType)
                return Results.BadRequest(new { message = "multipart/form-data required" });
            var form = await req.ReadFormAsync();

            string name    = (form["name"].ToString() ?? "").Trim();
            string mobile1 = (form["mobile1"].ToString() ?? "").Trim();
            string mobile2 = (form["mobile2"].ToString() ?? "").Trim();
            string address = (form["address"].ToString() ?? "").Trim();
            string email1  = (form["email1"].ToString() ?? "").Trim().ToLowerInvariant();
            string email2  = (form["email2"].ToString() ?? "").Trim().ToLowerInvariant();
            string password= form["password"].ToString() ?? "";

            if (name.Length < 2 || string.IsNullOrWhiteSpace(mobile1)
                || string.IsNullOrWhiteSpace(address) || !IsValidEmail(email1)
                || string.IsNullOrEmpty(password))
                return Results.BadRequest(new { message = "Missing required fields." });
            if (!string.IsNullOrEmpty(email2) && !IsValidEmail(email2))
                return Results.BadRequest(new { message = "Secondary email is invalid." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            if (!await WasRecentlyVerified(conn, email1))
                return Results.BadRequest(new { message = "Primary email is not verified — verify the OTP first." });
            if (!string.IsNullOrEmpty(email2) && !await WasRecentlyVerified(conn, email2))
                return Results.BadRequest(new { message = "Secondary email is not verified." });

            await using (var dup = new MySqlCommand("SELECT COUNT(*) FROM agencies WHERE email1 = @e", conn))
            {
                dup.Parameters.AddWithValue("@e", email1);
                var c = Convert.ToInt32(await dup.ExecuteScalarAsync());
                if (c > 0)
                    return Results.BadRequest(new { message = "An agency with this primary email already exists." });
            }

            string slug = await GenerateUniqueSlug(conn, name);

            string? logoRel = null;
            var logoFile = form.Files["logo"];
            if (logoFile != null && logoFile.Length > 0 && logoFile.Length < 5 * 1024 * 1024)
            {
                var ext = (Path.GetExtension(logoFile.FileName) ?? ".jpg").ToLowerInvariant();
                if (ext.Length > 5 || !Regex.IsMatch(ext, @"^\.[a-z]+$")) ext = ".jpg";
                var fname = $"{slug}{ext}";
                var fpath = Path.Combine(LOGO_DIR, fname);
                await using var fs = File.Create(fpath);
                await logoFile.CopyToAsync(fs);
                logoRel = "/agency-uploads/" + fname;
            }

            await using (var ins = new MySqlCommand(@"
                INSERT INTO agencies
                  (name, slug, mobile1, mobile2, address, logo_path,
                   email1, email2, password_hash, status, created_at)
                VALUES
                  (@name, @slug, @m1, @m2, @addr, @logo,
                   @e1, @e2, @ph, 'pending', UTC_TIMESTAMP());", conn))
            {
                ins.Parameters.AddWithValue("@name", name);
                ins.Parameters.AddWithValue("@slug", slug);
                ins.Parameters.AddWithValue("@m1", mobile1);
                ins.Parameters.AddWithValue("@m2", (object?)NullIfEmpty(mobile2) ?? DBNull.Value);
                ins.Parameters.AddWithValue("@addr", address);
                ins.Parameters.AddWithValue("@logo", (object?)logoRel ?? DBNull.Value);
                ins.Parameters.AddWithValue("@e1", email1);
                ins.Parameters.AddWithValue("@e2", (object?)NullIfEmpty(email2) ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ph", HashPassword(password));
                await ins.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { ok = true, slug });
        });

        string manageOtpEmail = Env("MANAGE_OTP_EMAIL", "rahul@loopwar.dev");

        app.MapPost("/api/agency/manage/login", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string password = dto.GetValueOrDefault("password") ?? "";
            if (password != MANAGE_PASSWORD)
                return Results.Json(new { message = "Incorrect password" }, statusCode: 401);

            string token = NewToken();
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "INSERT INTO manage_sessions (token, expires_at) VALUES (@t, DATE_ADD(UTC_TIMESTAMP(), INTERVAL 12 HOUR));", conn);
            cmd.Parameters.AddWithValue("@t", token);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { token });
        });

        app.MapPost("/api/agency/manage/otp/request", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string password = dto.GetValueOrDefault("password") ?? "";
            if (password != MANAGE_PASSWORD)
                return Results.Json(new { message = "Incorrect password" }, statusCode: 401);

            string code = GenerateOtp();
            var expiresAt = DateTime.UtcNow.AddMinutes(10);
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            var mThrottle = await OtpThrottle(conn, manageOtpEmail, "manage");
            if (mThrottle.HourlyCapHit)
                return Results.Json(new { message = "Too many codes requested. Try again later." }, statusCode: 429);
            if (mThrottle.RetrySeconds > 0)
                return Results.Json(new { retryAfter = mThrottle.RetrySeconds,
                    message = "A code was just sent. Wait " + mThrottle.RetrySeconds + "s before asking for another." }, statusCode: 429);

            await using (var cmd = new MySqlCommand(
                "INSERT INTO agency_otps (email, code, purpose, expires_at) VALUES (@e, @c, 'manage', @x)", conn))
            {
                cmd.Parameters.AddWithValue("@e", manageOtpEmail);
                cmd.Parameters.AddWithValue("@c", code);
                cmd.Parameters.AddWithValue("@x", expiresAt);
                await cmd.ExecuteNonQueryAsync();
            }
            try   { await SendManageOtpEmail(smtp, manageOtpEmail, code); }
            catch (Exception ex) { return Results.Problem("Failed to send code: " + ex.Message); }

            return Results.Ok(new { sent = true });
        });

        app.MapPost("/api/agency/manage/otp/verify", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string code = (dto.GetValueOrDefault("code") ?? "").Trim();
            if (code.Length != 6)
                return Results.BadRequest(new { message = "Enter the 6-digit code." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            int n;
            await using (var upd = new MySqlCommand(@"
                UPDATE agency_otps
                   SET consumed = 1
                 WHERE purpose = 'manage' AND code = @c AND consumed = 0
                   AND expires_at > UTC_TIMESTAMP()
                 ORDER BY id DESC LIMIT 1;", conn))
            {
                upd.Parameters.AddWithValue("@c", code);
                n = await upd.ExecuteNonQueryAsync();
            }
            if (n == 0)
                return Results.BadRequest(new { message = "Invalid or expired code." });

            string token = NewToken();
            await using var ins = new MySqlCommand(
                "INSERT INTO manage_sessions (token, expires_at) VALUES (@t, DATE_ADD(UTC_TIMESTAMP(), INTERVAL 12 HOUR));", conn);
            ins.Parameters.AddWithValue("@t", token);
            await ins.ExecuteNonQueryAsync();
            return Results.Ok(new { token });
        });

        app.MapGet("/api/agency/manage/list", async (HttpContext ctx, string? status) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            string where = "";
            var paramz = new (string k, object v)[0];
            if (!string.IsNullOrEmpty(status) && status != "all")
            {
                where = " WHERE status = @s";
                paramz = new[] { ("@s", (object)status) };
            }

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT id, name, slug, mobile1, mobile2, address, logo_path,
                       email1, email2, db_name, status, rejected_reason,
                       created_at, approved_at
                  FROM agencies " + where + " ORDER BY created_at DESC;", conn);
            foreach (var (k, v) in paramz) cmd.Parameters.AddWithValue(k, v);

            var rows = new System.Collections.Generic.List<object>();
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                rows.Add(new {
                    id              = rdr.GetInt32("id"),
                    name            = rdr.GetString("name"),
                    slug            = rdr.GetString("slug"),
                    mobile1         = rdr.GetString("mobile1"),
                    mobile2         = rdr.IsDBNull(rdr.GetOrdinal("mobile2"))      ? null : rdr.GetString("mobile2"),
                    address         = rdr.IsDBNull(rdr.GetOrdinal("address"))      ? null : rdr.GetString("address"),
                    logoPath        = rdr.IsDBNull(rdr.GetOrdinal("logo_path"))    ? null : rdr.GetString("logo_path"),
                    email1          = rdr.GetString("email1"),
                    email2          = rdr.IsDBNull(rdr.GetOrdinal("email2"))       ? null : rdr.GetString("email2"),
                    dbName          = rdr.IsDBNull(rdr.GetOrdinal("db_name"))      ? null : rdr.GetString("db_name"),
                    status          = rdr.GetString("status"),
                    rejectedReason  = rdr.IsDBNull(rdr.GetOrdinal("rejected_reason")) ? null : rdr.GetString("rejected_reason"),
                    createdAt       = rdr.GetDateTime("created_at").ToString("O"),
                    approvedAt      = rdr.IsDBNull(rdr.GetOrdinal("approved_at"))  ? null : rdr.GetDateTime("approved_at").ToString("O"),
                });
            }
            return Results.Ok(new { agencies = rows });
        });

        app.MapPost("/api/agency/manage/approve/{id:int}", async (HttpContext ctx, int id) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            string? slug = null, email1 = null, name = null;
            await using (var sel = new MySqlCommand(
                "SELECT slug, email1, name FROM agencies WHERE id=@id AND status='pending' LIMIT 1;", conn))
            {
                sel.Parameters.AddWithValue("@id", id);
                await using var rdr = await sel.ExecuteReaderAsync();
                if (!await rdr.ReadAsync())
                    return Results.BadRequest(new { message = "Agency not found or not pending." });
                slug   = rdr.GetString(0);
                email1 = rdr.GetString(1);
                name   = rdr.GetString(2);
            }

            string dbName = "crmr_" + slug;
            string dbUser = "tu_"   + slug;
            if (dbUser.Length > 32) dbUser = dbUser.Substring(0, 32);
            string dbPass = DeriveTenantPassword(slug);

            try
            {
                await ProvisionTenant(provConn, mysqlHost, mysqlPort, dbName, dbUser, dbPass);
            }
            catch (Exception ex)
            {
                return Results.Problem("Provisioning failed: " + ex.Message);
            }

            await using (var upd = new MySqlCommand(@"
                UPDATE agencies
                   SET status = 'approved',
                       approved_at = UTC_TIMESTAMP(),
                       db_name = @db,
                       db_user = @du
                 WHERE id = @id;", conn))
            {
                upd.Parameters.AddWithValue("@db", dbName);
                upd.Parameters.AddWithValue("@du", dbUser);
                upd.Parameters.AddWithValue("@id", id);
                await upd.ExecuteNonQueryAsync();
            }

            try
            {
                await SendApprovedEmail(smtp, email1!, name!);
            } catch { }

            return Results.Ok(new { ok = true, dbName });
        });

        app.MapPost("/api/agency/manage/reject/{id:int}", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            string reason = dto.GetValueOrDefault("reason") ?? "";

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                UPDATE agencies SET status = 'rejected', rejected_reason = @r
                 WHERE id = @id AND status = 'pending';", conn);
            cmd.Parameters.AddWithValue("@r", (object?)NullIfEmpty(reason) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            int n = await cmd.ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Agency not found or not pending." });
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/agency/manage/agency/{id:int}", async (HttpContext ctx, int id) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT id, name, slug, email1, COALESCE(email2,''),
                       mobile1, COALESCE(mobile2,''),
                       COALESCE(address,''), COALESCE(mobiles_extra,''),
                       COALESCE(logo_path,''), status,
                       COALESCE(hrms_enabled,0), hrms_enabled_at
                  FROM agencies WHERE id = @id LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Agency not found" });

            var raw = rdr.GetString(8);
            var extras = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
            return Results.Ok(new
            {
                id       = rdr.GetInt32(0),
                name     = rdr.GetString(1),
                slug     = rdr.GetString(2),
                email1   = rdr.GetString(3),
                email2   = rdr.GetString(4),
                mobile1  = rdr.GetString(5),
                mobile2  = rdr.GetString(6),
                address  = rdr.GetString(7),
                extras,
                logoPath = rdr.GetString(9),
                status   = rdr.GetString(10),
                hrmsEnabled   = rdr.GetInt32(11) == 1,
                hrmsEnabledAt = rdr.IsDBNull(12) ? null : rdr.GetDateTime(12).ToString("yyyy-MM-dd HH:mm"),
            });
        });

        app.MapPost("/api/agency/manage/agency/{id:int}", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            string? S(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

            var sets   = new List<string>();
            var args   = new List<(string, object?)> { ("@id", id) };
            void Maybe(string col, string? val)
            {
                if (val == null) return;
                sets.Add($"{col}=@{col}");
                args.Add(($"@{col}", string.IsNullOrWhiteSpace(val) ? (object?)DBNull.Value : val.Trim()));
            }
            Maybe("name",    S("name"));
            Maybe("address", S("address"));
            Maybe("mobile1", S("mobile1"));
            Maybe("mobile2", S("mobile2"));

            if (root.TryGetProperty("extras", out var extrasEl) && extrasEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var lines = extrasEl.EnumerateArray()
                    .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(e => (e.GetString() ?? "").Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .Take(20)
                    .ToList();
                sets.Add("mobiles_extra=@mobiles_extra");
                args.Add(("@mobiles_extra", lines.Count == 0 ? (object?)DBNull.Value : string.Join("\n", lines)));
            }

            if (sets.Count == 0) return Results.BadRequest(new { message = "No fields to update" });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand($"UPDATE agencies SET {string.Join(", ", sets)} WHERE id=@id", conn);
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            int n = await cmd.ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Agency not found" });
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/agency/manage/agency/{id:int}/hrms", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            if (!doc.RootElement.TryGetProperty("enabled", out var el) ||
                (el.ValueKind != System.Text.Json.JsonValueKind.True &&
                 el.ValueKind != System.Text.Json.JsonValueKind.False))
                return Results.BadRequest(new { message = "enabled must be true or false" });

            bool enabled = el.GetBoolean();

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "UPDATE agencies SET hrms_enabled=@v, hrms_enabled_at=IF(@v=1, COALESCE(hrms_enabled_at, NOW()), NULL) WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@v", enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", id);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                return Results.NotFound(new { message = "Agency not found" });

            return Results.Ok(new { ok = true, hrmsEnabled = enabled });
        });

        app.MapPost("/api/agency/manage/agency/{id:int}/logo", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            if (!req.HasFormContentType)
                return Results.BadRequest(new { message = "Expected a multipart form with a logo file." });

            var form = await req.ReadFormAsync();
            var logoFile = form.Files["logo"];
            if (logoFile == null || logoFile.Length == 0)
                return Results.BadRequest(new { message = "No logo file provided." });
            if (logoFile.Length >= 5 * 1024 * 1024)
                return Results.BadRequest(new { message = "Logo must be under 5 MB." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            string? slug = null, oldPath = null;
            await using (var q = new MySqlCommand("SELECT slug, COALESCE(logo_path,'') FROM agencies WHERE id=@id LIMIT 1", conn))
            {
                q.Parameters.AddWithValue("@id", id);
                await using var rdr = await q.ExecuteReaderAsync();
                if (await rdr.ReadAsync()) { slug = rdr.GetString(0); oldPath = rdr.GetString(1); }
            }
            if (string.IsNullOrEmpty(slug)) return Results.NotFound(new { message = "Agency not found" });

            var ext = (Path.GetExtension(logoFile.FileName) ?? ".jpg").ToLowerInvariant();
            if (ext.Length > 5 || !Regex.IsMatch(ext, @"^\.[a-z]+$")) ext = ".jpg";
            Directory.CreateDirectory(LOGO_DIR);
            var fname = $"{slug}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
            var fpath = Path.Combine(LOGO_DIR, fname);
            await using (var fs = File.Create(fpath))
                await logoFile.CopyToAsync(fs);
            var logoRel = "/agency-uploads/" + fname;

            await using (var up = new MySqlCommand("UPDATE agencies SET logo_path=@l WHERE id=@id", conn))
            {
                up.Parameters.AddWithValue("@l", logoRel);
                up.Parameters.AddWithValue("@id", id);
                await up.ExecuteNonQueryAsync();
            }

            if (!string.IsNullOrEmpty(oldPath))
            {
                var oldFull = Path.Combine(LOGO_DIR, Path.GetFileName(oldPath));
                if (oldFull != fpath && File.Exists(oldFull))
                    try { File.Delete(oldFull); } catch { }
            }
            return Results.Ok(new { ok = true, logoPath = logoRel });
        });

        app.MapPost("/api/agency/manage/agency/{id:int}/password", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            var dto = await ReadJsonAsync(req);
            string password = dto.GetValueOrDefault("password") ?? "";
            if (password.Length < 6)
                return Results.BadRequest(new { message = "Password must be at least 6 characters." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            await using (var cmd = new MySqlCommand(
                "UPDATE agencies SET password_hash=@h WHERE id=@id", conn))
            {
                cmd.Parameters.AddWithValue("@h", HashPassword(password));
                cmd.Parameters.AddWithValue("@id", id);
                if (await cmd.ExecuteNonQueryAsync() == 0)
                    return Results.NotFound(new { message = "Agency not found" });
            }

            int dropped = 0;
            await using (var rev = new MySqlCommand(
                "UPDATE desktop_sessions SET revoked=1 WHERE agency_id=@id AND revoked=0", conn))
            {
                rev.Parameters.AddWithValue("@id", id);
                dropped = await rev.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { ok = true, signedOutDevices = dropped });
        });

        app.MapGet("/api/agency/desktop/profile", async (HttpContext ctx) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT name, COALESCE(address,''), mobile1,
                       COALESCE(mobile2,''), COALESCE(mobiles_extra,''),
                       COALESCE(logo_path,'')
                  FROM agencies WHERE id=@id LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@id", me.id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Agency not found" });

            var extras = rdr.GetString(4)
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            return Results.Ok(new
            {
                id       = me.id,
                name     = rdr.GetString(0),
                address  = rdr.GetString(1),
                mobile1  = rdr.GetString(2),
                mobile2  = rdr.GetString(3),
                extras,
                logoPath = rdr.GetString(5),
            });
        });

        app.MapPost("/api/agency/desktop/profile", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            string? S(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

            var sets = new List<string>();
            var args = new List<(string, object?)> { ("@id", me.id) };
            void Maybe(string col, string? val)
            {
                if (val == null) return;
                sets.Add($"{col}=@{col}");
                args.Add(($"@{col}", string.IsNullOrWhiteSpace(val) ? (object?)DBNull.Value : val.Trim()));
            }
            Maybe("name",    S("name"));
            Maybe("address", S("address"));
            Maybe("mobile1", S("mobile1"));
            Maybe("mobile2", S("mobile2"));

            if (root.TryGetProperty("extras", out var extrasEl) && extrasEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var lines = extrasEl.EnumerateArray()
                    .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(e => (e.GetString() ?? "").Trim())
                    .Where(s => s.Length > 0)
                    .Distinct()
                    .Take(20)
                    .ToList();
                sets.Add("mobiles_extra=@mobiles_extra");
                args.Add(("@mobiles_extra", lines.Count == 0 ? (object?)DBNull.Value : string.Join("\n", lines)));
            }

            if (sets.Count == 0) return Results.BadRequest(new { message = "No fields to update" });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand($"UPDATE agencies SET {string.Join(", ", sets)} WHERE id=@id", conn);
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            int n = await cmd.ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Agency not found" });
            return Results.Ok(new { ok = true });
        });

        const string TICKETS_DIR = "/opt/vkapi/agency-uploads/tickets";
        try { Directory.CreateDirectory(TICKETS_DIR); } catch { }

        app.MapPost("/api/agency/desktop/tickets", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            string S(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? (v.GetString() ?? "") : "";
            var subject = S("subject").Trim();
            var message = S("message").Trim();
            var shotB64 = S("screenshotBase64").Trim();
            if (subject.Length < 2 || message.Length < 2)
                return Results.BadRequest(new { message = "Please enter a subject and a description." });

            string agencyName = me.slug;
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using (var nc = new MySqlCommand("SELECT name FROM agencies WHERE id=@id LIMIT 1", conn))
            {
                nc.Parameters.AddWithValue("@id", me.id);
                if (await nc.ExecuteScalarAsync() is string n) agencyName = n;
            }

            string? shotPath = null;
            if (!string.IsNullOrEmpty(shotB64))
            {
                try
                {
                    var raw = shotB64.Contains(',') ? shotB64[(shotB64.IndexOf(',') + 1)..] : shotB64;
                    var bytes = Convert.FromBase64String(raw);
                    if (bytes.Length > 0 && bytes.Length <= 8 * 1024 * 1024)
                    {
                        var fn = $"ticket_{me.slug}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.jpg";
                        await File.WriteAllBytesAsync(Path.Combine(TICKETS_DIR, fn), bytes);
                        shotPath = "tickets/" + fn;
                    }
                }
                catch { }
            }

            await using var ins = new MySqlCommand(@"
                INSERT INTO support_tickets (agency_id, agency_slug, agency_name, subject, message, screenshot_path, status)
                VALUES (@aid, @slug, @aname, @subj, @msg, @shot, 'open')", conn);
            ins.Parameters.AddWithValue("@aid", me.id);
            ins.Parameters.AddWithValue("@slug", me.slug);
            ins.Parameters.AddWithValue("@aname", agencyName);
            ins.Parameters.AddWithValue("@subj", subject);
            ins.Parameters.AddWithValue("@msg", message);
            ins.Parameters.AddWithValue("@shot", (object?)shotPath ?? DBNull.Value);
            await ins.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true, id = ins.LastInsertedId });
        });

        app.MapGet("/api/agency/desktop/tickets", async (HttpContext ctx) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            var tickets = await ReadTicketHeaders(conn,
                "WHERE agency_slug=@s ORDER BY id DESC", false, ("@s", me.slug));
            foreach (var t in tickets) t["messages"] = await LoadMessages(conn, (int)t["id"]);
            return Results.Ok(tickets);
        });

        app.MapPost("/api/agency/desktop/tickets/{id:int}/messages", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();
            var body = await ReadBody(req);
            if (body.Length < 1) return Results.BadRequest(new { message = "Empty message" });
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using (var chk = new MySqlCommand("SELECT agency_slug FROM support_tickets WHERE id=@id", conn))
            {
                chk.Parameters.AddWithValue("@id", id);
                if (await chk.ExecuteScalarAsync() as string != me.slug)
                    return Results.NotFound(new { message = "Ticket not found" });
            }
            await AddMessage(conn, id, "agency", body);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/agency/desktop/client-error", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();

            static string Cap(string s, int n) => s.Length > n ? s[..n] : s;
            string op, summary, detail, context, appVer, machine, os, clientTime;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
                op         = Cap(S("operation"),   120);
                summary    = Cap(S("summary"),     500);
                detail     = Cap(S("detail"),    60000);
                context    = Cap(S("context"),    1000);
                appVer     = Cap(S("appVersion"),   40);
                machine    = Cap(S("machineName"), 120);
                os         = Cap(S("os"),          160);
                clientTime = Cap(S("occurredAt"),   40);
            }
            catch { return Results.BadRequest(new { message = "Bad report" }); }
            if (op.Length == 0) op = "unknown";

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await EnsureClientErrorTable(conn);

            string agencyName = me.slug;
            await using (var nc = new MySqlCommand("SELECT name FROM agencies WHERE id=@id LIMIT 1", conn))
            {
                nc.Parameters.AddWithValue("@id", me.id);
                if (await nc.ExecuteScalarAsync() is string n) agencyName = n;
            }
            string ip = ClientIp(ctx);

            await using var ins = new MySqlCommand(@"
                INSERT INTO client_error_log
                  (agency_id, agency_slug, agency_name, operation, summary, detail, context,
                   app_version, machine_name, os, source_ip, client_time)
                VALUES (@aid,@slug,@aname,@op,@sum,@det,@ctx,@ver,@mac,@os,@ip,@ct)", conn);
            ins.Parameters.AddWithValue("@aid",  me.id);
            ins.Parameters.AddWithValue("@slug", me.slug);
            ins.Parameters.AddWithValue("@aname", agencyName);
            ins.Parameters.AddWithValue("@op",  op);
            ins.Parameters.AddWithValue("@sum", summary.Length    == 0 ? (object)DBNull.Value : summary);
            ins.Parameters.AddWithValue("@det", detail.Length     == 0 ? (object)DBNull.Value : detail);
            ins.Parameters.AddWithValue("@ctx", context.Length    == 0 ? (object)DBNull.Value : context);
            ins.Parameters.AddWithValue("@ver", appVer.Length     == 0 ? (object)DBNull.Value : appVer);
            ins.Parameters.AddWithValue("@mac", machine.Length    == 0 ? (object)DBNull.Value : machine);
            ins.Parameters.AddWithValue("@os",  os.Length         == 0 ? (object)DBNull.Value : os);
            ins.Parameters.AddWithValue("@ip",  ip.Length         == 0 ? (object)DBNull.Value : ip);
            ins.Parameters.AddWithValue("@ct",  clientTime.Length == 0 ? (object)DBNull.Value : clientTime);
            await ins.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true, id = ins.LastInsertedId });
        });

        app.MapGet("/api/agency/manage/client-errors", async (HttpContext ctx, string? agency, int? limit) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await EnsureClientErrorTable(conn);
            int lim = Math.Clamp(limit ?? 300, 1, 1000);
            bool one = !string.IsNullOrWhiteSpace(agency);
            await using var cmd = new MySqlCommand(
                "SELECT id, agency_slug, agency_name, operation, summary, detail, context, " +
                "app_version, machine_name, os, source_ip, " +
                "DATE_FORMAT(created_at,'%Y-%m-%d %H:%i:%s') AS created_at " +
                "FROM client_error_log " + (one ? "WHERE agency_slug=@s " : "") +
                "ORDER BY id DESC LIMIT " + lim, conn);
            if (one) cmd.Parameters.AddWithValue("@s", agency);
            var list = new List<object>();
            await using var rdr = await cmd.ExecuteReaderAsync();
            string G(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
            while (await rdr.ReadAsync())
                list.Add(new {
                    id = rdr.GetInt64(0), agencySlug = G(1), agencyName = G(2),
                    operation = G(3), summary = G(4), detail = G(5), context = G(6),
                    appVersion = G(7), machineName = G(8), os = G(9), sourceIp = G(10),
                    createdAt = G(11)
                });
            return Results.Ok(list);
        });

        app.MapGet("/api/agency/manage/tickets", async (HttpContext ctx) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            var tickets = await ReadTicketHeaders(conn,
                "ORDER BY (status='resolved'), id DESC", true);
            return Results.Ok(tickets);
        });

        app.MapGet("/api/agency/manage/tickets/{id:int}/messages", async (HttpContext ctx, int id) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            return Results.Ok(await LoadMessages(conn, id));
        });

        app.MapPost("/api/agency/manage/tickets/{id:int}/messages", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            var body = await ReadBody(req);
            if (body.Length < 1) return Results.BadRequest(new { message = "Empty message" });
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await AddMessage(conn, id, "admin", body);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/agency/manage/tickets/{id:int}", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            var reply  = root.TryGetProperty("adminReply", out var rv) && rv.ValueKind == System.Text.Json.JsonValueKind.String ? rv.GetString() : null;

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            if (status is "open" or "in_progress" or "resolved")
            {
                await using var cmd = new MySqlCommand("UPDATE support_tickets SET status=@st WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@st", status);
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
            if (!string.IsNullOrWhiteSpace(reply)) await AddMessage(conn, id, "admin", reply.Trim());
            return Results.Ok(new { ok = true });
        });

        const string AGENCY_APPS_ROOT = "/opt/vkapi/agency-apps";

        app.MapGet("/api/agency/manage/apps", async (HttpContext ctx) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx)) return Results.Unauthorized();

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT slug, name, status, COALESCE(logo_path,'') FROM agencies ORDER BY name;", conn);

            var rows = new List<object>();
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                string slug     = rdr.GetString(0);
                string name     = rdr.GetString(1);
                string status   = rdr.GetString(2);
                string logoPath = rdr.GetString(3);
                string flavor = slug.Replace("_", "");
                string pkg    = $"com.crmrecoverysoftware.{flavor}";
                string apk      = Path.Combine(AGENCY_APPS_ROOT, flavor, "app.apk");
                string aab      = Path.Combine(AGENCY_APPS_ROOT, flavor, "app.aab");
                string setup    = Path.Combine(AGENCY_APPS_ROOT, flavor, "setup.exe");
                string portable = Path.Combine(AGENCY_APPS_ROOT, flavor, "portable.zip");
                string logoUrl = "";
                if (!string.IsNullOrEmpty(logoPath))
                {
                    string fname = Path.GetFileName(logoPath);
                    if (!string.IsNullOrEmpty(fname) &&
                        File.Exists(Path.Combine("/opt/vkapi/agency-uploads", fname)))
                        logoUrl = "https://api.crmrecoverysoftware.com/agency-uploads/" + fname;
                }
                rows.Add(new
                {
                    slug, name, status, flavor,
                    logoUrl,
                    packageId    = pkg,
                    apkExists    = File.Exists(apk),
                    apkSize      = File.Exists(apk) ? new FileInfo(apk).Length : 0L,
                    apkBuiltAt   = BuiltAtIst(apk),
                    aabExists    = File.Exists(aab),
                    aabSize      = File.Exists(aab) ? new FileInfo(aab).Length : 0L,
                    aabBuiltAt   = BuiltAtIst(aab),
                    setupExists  = File.Exists(setup),
                    setupSize    = File.Exists(setup) ? new FileInfo(setup).Length : 0L,
                    setupBuiltAt = BuiltAtIst(setup),
                    portableExists  = File.Exists(portable),
                    portableSize    = File.Exists(portable) ? new FileInfo(portable).Length : 0L,
                    portableBuiltAt = BuiltAtIst(portable),
                });
            }
            return Results.Ok(new { apps = rows });
        });

        app.MapGet("/api/agency/manage/apps/{flavor}/download/{type}", async (HttpContext ctx, string flavor, string type) =>
        {
            string? token = ctx.Request.Headers["X-Manage-Token"].FirstOrDefault()
                            ?? ctx.Request.Query["token"].FirstOrDefault();
            if (string.IsNullOrEmpty(token) || token.Length != 64)
                return Results.Unauthorized();
            await using (var c = new MySqlConnection(masterConn))
            {
                await c.OpenAsync();
                await using var qc = new MySqlCommand(
                    "SELECT 1 FROM manage_sessions WHERE token=@t AND expires_at > UTC_TIMESTAMP() LIMIT 1;", c);
                qc.Parameters.AddWithValue("@t", token);
                if (await qc.ExecuteScalarAsync() == null) return Results.Unauthorized();
            }

            if (!Regex.IsMatch(flavor, @"^[a-z0-9]+$")) return Results.BadRequest(new { message = "Invalid flavor" });

            (string fileName, string mime, string downloadName) = type switch
            {
                "apk"      => ("app.apk",      "application/vnd.android.package-archive", $"crms-{flavor}.apk"),
                "aab"      => ("app.aab",      "application/octet-stream",                $"crms-{flavor}.aab"),
                "setup"    => ("setup.exe",    "application/octet-stream",                $"crms-{flavor}-setup.exe"),
                "portable" => ("portable.zip", "application/zip",                         $"crms-{flavor}-portable.zip"),
                _          => ("",             "", ""),
            };
            if (string.IsNullOrEmpty(fileName))
                return Results.BadRequest(new { message = "Invalid type" });

            string path = Path.Combine(AGENCY_APPS_ROOT, flavor, fileName);
            if (!File.Exists(path)) return Results.NotFound(new { message = $"No {type} built for this agency yet." });

            return Results.File(path, mime, downloadName);
        });

        app.MapPost("/api/agency/desktop/login", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string email    = (dto.GetValueOrDefault("email") ?? "").Trim().ToLowerInvariant();
            string password =  dto.GetValueOrDefault("password") ?? "";
            if (!IsValidEmail(email) || string.IsNullOrEmpty(password))
                return Results.BadRequest(new { message = "Enter your email and password." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            int id = 0;
            string name = "", slug = "", status = "", hash = "";
            string? logoPath = null, mobile1 = null, address = null;
            bool hrmsEnabled = false;
            await using (var cmd = new MySqlCommand(@"
                SELECT id, name, slug, status, password_hash, logo_path, mobile1, address,
                       COALESCE(hrms_enabled,0) AS hrms_enabled
                  FROM agencies WHERE email1 = @e LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@e", email);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync())
                    return Results.BadRequest(new { message = "Invalid email or password." });
                id       = rdr.GetInt32("id");
                name     = rdr.GetString("name");
                slug     = rdr.GetString("slug");
                status   = rdr.GetString("status");
                hash     = rdr.IsDBNull(rdr.GetOrdinal("password_hash")) ? "" : rdr.GetString("password_hash");
                logoPath = rdr.IsDBNull(rdr.GetOrdinal("logo_path"))     ? null : rdr.GetString("logo_path");
                mobile1  = rdr.IsDBNull(rdr.GetOrdinal("mobile1"))       ? null : rdr.GetString("mobile1");
                address  = rdr.IsDBNull(rdr.GetOrdinal("address"))       ? null : rdr.GetString("address");
                hrmsEnabled = rdr.GetInt32("hrms_enabled") == 1;
            }

            if (!VerifyPassword(password, hash))
                return Results.BadRequest(new { message = "Invalid email or password." });

            if (status != "approved")
            {
                string msg = status switch
                {
                    "pending"   => "Your agency account is still awaiting verification. You'll be able to sign in once an administrator approves it.",
                    "rejected"  => "Your agency registration was not approved. Please contact CRMRS support.",
                    "suspended" => "Your agency account has been suspended. Please contact CRMRS support.",
                    _           => "Your agency account is not active.",
                };
                return Results.Json(new { message = msg }, statusCode: 403);
            }

            string token = AgencyToken.Issue(id, slug);

            string deviceToken = "";
            bool remember = (dto.GetValueOrDefault("rememberDevice") ?? "").Trim().ToLowerInvariant() is "1" or "true";
            if (remember)
            {
                try
                {
                    deviceToken = NewDeviceToken();
                    await using var ins = new MySqlCommand(@"
                        INSERT INTO desktop_sessions (agency_id, token_hash, pw_stamp, device_label, expires_at)
                        VALUES (@a, @t, @p, @d, DATE_ADD(NOW(), INTERVAL 7 DAY));", conn);
                    ins.Parameters.AddWithValue("@a", id);
                    ins.Parameters.AddWithValue("@t", Sha256Hex(deviceToken));
                    ins.Parameters.AddWithValue("@p", Sha256Hex(hash));
                    ins.Parameters.AddWithValue("@d", (dto.GetValueOrDefault("deviceLabel") ?? "").Trim());
                    await ins.ExecuteNonQueryAsync();
                }
                catch { deviceToken = ""; }
            }

            return Results.Ok(new
            {
                token,
                deviceToken,
                agencyId   = id,
                agencyName = name,
                slug,
                email,
                mobile1    = mobile1 ?? "",
                address    = address ?? "",
                logoPath   = logoPath ?? "",
                isAgency   = true,
                hrmsEnabled,
            });
        });

        app.MapPost("/api/agency/desktop/session/resume", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string deviceToken = (dto.GetValueOrDefault("deviceToken") ?? "").Trim();
            if (string.IsNullOrEmpty(deviceToken))
                return Results.Json(new { message = "No session." }, statusCode: 401);

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();

            long sessionId = 0; int agencyId = 0; string storedStamp = "";
            await using (var cmd = new MySqlCommand(@"
                SELECT id, agency_id, pw_stamp
                  FROM desktop_sessions
                 WHERE token_hash = @t AND revoked = 0 AND expires_at > NOW()
                 LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@t", Sha256Hex(deviceToken));
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync())
                    return Results.Json(new { message = "Session expired." }, statusCode: 401);
                sessionId   = rdr.GetInt64(0);
                agencyId    = rdr.GetInt32(1);
                storedStamp = rdr.GetString(2);
            }

            int id = 0;
            string name = "", slug = "", status = "", hash = "", email = "";
            string? logoPath = null, mobile1 = null, address = null;
            bool hrmsEnabled = false;
            await using (var cmd = new MySqlCommand(@"
                SELECT id, name, slug, status, COALESCE(password_hash,''), logo_path, mobile1, address, email1,
                       COALESCE(hrms_enabled,0)
                  FROM agencies WHERE id = @id LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@id", agencyId);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync())
                    return Results.Json(new { message = "Session expired." }, statusCode: 401);
                id       = rdr.GetInt32(0);
                name     = rdr.GetString(1);
                slug     = rdr.GetString(2);
                status   = rdr.GetString(3);
                hash     = rdr.GetString(4);
                logoPath = rdr.IsDBNull(5) ? null : rdr.GetString(5);
                mobile1  = rdr.IsDBNull(6) ? null : rdr.GetString(6);
                address  = rdr.IsDBNull(7) ? null : rdr.GetString(7);
                email    = rdr.IsDBNull(8) ? "" : rdr.GetString(8);
                hrmsEnabled = rdr.GetInt32(9) == 1;
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(storedStamp), Encoding.UTF8.GetBytes(Sha256Hex(hash))))
            {
                await using var rev = new MySqlCommand(
                    "UPDATE desktop_sessions SET revoked = 1 WHERE id = @id;", conn);
                rev.Parameters.AddWithValue("@id", sessionId);
                await rev.ExecuteNonQueryAsync();
                return Results.Json(new { message = "Password changed. Please sign in again." }, statusCode: 401);
            }

            if (status != "approved")
                return Results.Json(new { message = "Your agency account is not active." }, statusCode: 403);

            await using (var upd = new MySqlCommand(@"
                UPDATE desktop_sessions
                   SET last_used_at = NOW(), expires_at = DATE_ADD(NOW(), INTERVAL 7 DAY)
                 WHERE id = @id;", conn))
            {
                upd.Parameters.AddWithValue("@id", sessionId);
                await upd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new
            {
                token      = AgencyToken.Issue(id, slug),
                deviceToken,
                agencyId   = id,
                agencyName = name,
                slug,
                email,
                mobile1    = mobile1 ?? "",
                address    = address ?? "",
                logoPath   = logoPath ?? "",
                isAgency   = true,
                hrmsEnabled,
            });
        });

        app.MapPost("/api/agency/desktop/session/revoke", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string deviceToken = (dto.GetValueOrDefault("deviceToken") ?? "").Trim();
            if (string.IsNullOrEmpty(deviceToken)) return Results.Ok(new { success = true });
            try
            {
                await using var conn = new MySqlConnection(masterConn);
                await conn.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "UPDATE desktop_sessions SET revoked = 1 WHERE token_hash = @t;", conn);
                cmd.Parameters.AddWithValue("@t", Sha256Hex(deviceToken));
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
            return Results.Ok(new { success = true });
        });

        app.MapPost("/api/agency/web/login", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string email    = (dto.GetValueOrDefault("email") ?? "").Trim().ToLowerInvariant();
            string password =  dto.GetValueOrDefault("password") ?? "";
            if (!IsValidEmail(email) || string.IsNullOrEmpty(password))
                return Results.BadRequest(new { message = "Enter your email and password." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            string status = "", hash = "";
            await using (var cmd = new MySqlCommand(
                "SELECT status, COALESCE(password_hash,'') FROM agencies WHERE email1=@e LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@e", email);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync())
                    return Results.BadRequest(new { message = "Invalid email or password." });
                status = rdr.GetString(0);
                hash   = rdr.GetString(1);
            }
            if (!VerifyPassword(password, hash))
                return Results.BadRequest(new { message = "Invalid email or password." });
            if (status != "approved")
                return Results.Json(new { message = "Your agency account is not active yet. Please contact CRMRS support." }, statusCode: 403);

            string code = GenerateOtp();
            await using (var cmd = new MySqlCommand(
                "INSERT INTO agency_otps (email, code, purpose, expires_at) VALUES (@e,@c,'login',@x)", conn))
            {
                cmd.Parameters.AddWithValue("@e", email);
                cmd.Parameters.AddWithValue("@c", code);
                cmd.Parameters.AddWithValue("@x", DateTime.UtcNow.AddMinutes(10));
                await cmd.ExecuteNonQueryAsync();
            }
            try { await SendOtpEmail(smtp, email, code); }
            catch (Exception ex) { return Results.Problem("Failed to send the verification code: " + ex.Message); }
            return Results.Ok(new { otpRequired = true, email });
        });

        app.MapPost("/api/agency/web/verify", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string email = (dto.GetValueOrDefault("email") ?? "").Trim().ToLowerInvariant();
            string code  = (dto.GetValueOrDefault("code")  ?? "").Trim();
            if (!IsValidEmail(email) || code.Length != 6)
                return Results.BadRequest(new { message = "Email and 6-digit code required." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using (var cmd = new MySqlCommand(@"
                UPDATE agency_otps SET consumed=1
                 WHERE email=@e AND code=@c AND purpose='login'
                   AND consumed=0 AND expires_at > UTC_TIMESTAMP()
                 ORDER BY id DESC LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@e", email);
                cmd.Parameters.AddWithValue("@c", code);
                if (await cmd.ExecuteNonQueryAsync() == 0)
                    return Results.BadRequest(new { message = "Invalid or expired code." });
            }
            int id = 0; string name = "", slug = "", st = "";
            string? logoPath = null, mobile1 = null, address = null;
            await using (var cmd = new MySqlCommand(
                "SELECT id,name,slug,status,logo_path,mobile1,address FROM agencies WHERE email1=@e LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@e", email);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync()) return Results.BadRequest(new { message = "Account not found." });
                id = rdr.GetInt32(0); name = rdr.GetString(1); slug = rdr.GetString(2); st = rdr.GetString(3);
                logoPath = rdr.IsDBNull(4) ? null : rdr.GetString(4);
                mobile1  = rdr.IsDBNull(5) ? null : rdr.GetString(5);
                address  = rdr.IsDBNull(6) ? null : rdr.GetString(6);
            }
            if (st != "approved")
                return Results.Json(new { message = "Your agency account is not active." }, statusCode: 403);
            string token = AgencyToken.Issue(id, slug);
            return Results.Ok(new { token, agencyId = id, agencyName = name, slug, email,
                mobile1 = mobile1 ?? "", address = address ?? "", logoPath = logoPath ?? "", isAgency = true });
        });

        app.MapGet("/api/agency/web/search", async (HttpContext ctx, string? q, string? mode) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(new System.Collections.Generic.List<object>());
            bool isChassis = string.Equals(mode, "chassis", StringComparison.OrdinalIgnoreCase);
            string sql = isChassis
                ? @"SELECT vr.id, vr.vehicle_no, vr.chassis_no, vr.model, b.name AS branch_name,
                           COALESCE(f.name,'') AS financer,
                           COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y %h:%i %p'),'') AS created_on
                    FROM chassis_info ci
                    INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
                    INNER JOIN branches b ON b.id = vr.branch_id
                    LEFT  JOIN finances f ON f.id = b.finance_id
                    WHERE ci.last5 = @q"
                : @"SELECT vr.id, vr.vehicle_no, vr.chassis_no, vr.model, b.name AS branch_name,
                           COALESCE(f.name,'') AS financer,
                           COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y %h:%i %p'),'') AS created_on
                    FROM rc_info ri
                    INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
                    INNER JOIN branches b ON b.id = vr.branch_id
                    LEFT  JOIN finances f ON f.id = b.finance_id
                    WHERE ri.last4 = @q";
            try
            {
                await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, me.slug));
                await conn.OpenAsync();
                await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 15 };
                cmd.Parameters.AddWithValue("@q", q.ToUpper().Trim());
                await using var rdr = await cmd.ExecuteReaderAsync();
                var results = new System.Collections.Generic.List<object>();
                string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
                while (await rdr.ReadAsync())
                    results.Add(new {
                        Id = rdr.GetInt64(0).ToString(), VehicleNo = S(1), ChassisNo = S(2),
                        Model = S(3), BranchName = S(4), Financer = S(5), CreatedOn = S(6)
                    });
                return Results.Ok(results);
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapGet("/api/agency/web/record/{id:long}", async (HttpContext ctx, long id) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();
            const string fields = @"
                vr.id, vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model,
                vr.agreement_no, vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
                vr.sec9_available, vr.sec17_available, vr.customer_name, vr.customer_address, vr.customer_contact,
                vr.region, vr.area, vr.branch_name_raw,
                vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
                vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
                vr.sender_mail1, vr.sender_mail2, vr.executive_name, vr.pos, vr.toss, vr.remark,
                COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y %h:%i %p'),'') AS created_on,
                b.name AS branch_name, COALESCE(f.name,'') AS financer,
                COALESCE(b.contact1,'') AS b_c1, COALESCE(b.contact2,'') AS b_c2,
                COALESCE(b.contact3,'') AS b_c3, COALESCE(b.address,'') AS b_addr";
            string sql = $@"SELECT {fields} FROM vehicle_records vr
                            INNER JOIN branches b ON b.id = vr.branch_id
                            LEFT  JOIN finances f ON f.id = b.finance_id
                            WHERE vr.id = @id LIMIT 1";
            try
            {
                await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, me.slug));
                await conn.OpenAsync();
                await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 15 };
                cmd.Parameters.AddWithValue("@id", id);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync()) return Results.NotFound();
                string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
                return Results.Ok(new {
                    Id = rdr.GetInt64(0).ToString(),
                    VehicleNo = S(1), ChassisNo = S(2), EngineNo = S(3), Model = S(4),
                    AgreementNo = S(5), Bucket = S(6), GV = S(7), OD = S(8),
                    Seasoning = S(9), TBRFlag = S(10), Sec9Available = S(11), Sec17Available = S(12),
                    CustomerName = S(13), CustomerAddress = S(14), CustomerContactNos = S(15),
                    Region = S(16), Area = S(17), BranchFromExcel = S(18),
                    Level1 = S(19), Level1ContactNos = S(20), Level2 = S(21), Level2ContactNos = S(22),
                    Level3 = S(23), Level3ContactNos = S(24), Level4 = S(25), Level4ContactNos = S(26),
                    SenderMailId1 = S(27), SenderMailId2 = S(28), ExecutiveName = S(29),
                    POS = S(30), TOSS = S(31), Remark = S(32), CreatedOn = S(33),
                    BranchName = S(34), Financer = S(35),
                    FirstContactDetails = S(36), SecondContactDetails = S(37),
                    ThirdContactDetails = S(38), Address = S(39)
                });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapGet("/api/agency/web/directdata", async (HttpContext ctx) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();
            try
            {
                await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, me.slug));
                await conn.OpenAsync();
                var files = new System.Collections.Generic.List<object>();
                await using var cmd = new MySqlCommand(@"
                    SELECT wf.id, wb.bank_name, wf.file_name, wf.total_records,
                           COALESCE(wf.uploaded_by,''),
                           COALESCE(DATE_FORMAT(wf.created_at,'%d %b %Y %h:%i %p'),'')
                    FROM webhook_files wf
                    INNER JOIN webhook_banks wb ON wb.id = wf.bank_id
                    ORDER BY wf.id DESC", conn) { CommandTimeout = 15 };
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    files.Add(new
                    {
                        id           = rdr.GetInt32(0),
                        bankName     = rdr.GetString(1),
                        fileName     = rdr.GetString(2),
                        totalRecords = rdr.GetInt32(3),
                        uploadedBy   = rdr.GetString(4),
                        uploadedAt   = rdr.GetString(5),
                    });
                return Results.Ok(new { files });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapGet("/api/agency/web/directdata/{id:int}", async (HttpContext ctx, int id) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();
            try
            {
                await using var conn = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, me.slug));
                await conn.OpenAsync();
                string fileName = "", bankName = "", uploadedAt = "", relPath = "";
                int totalRecords = 0;
                await using (var cmd = new MySqlCommand(@"
                    SELECT wf.file_name, wb.bank_name, wf.file_path, wf.total_records,
                           COALESCE(DATE_FORMAT(wf.created_at,'%d %b %Y %h:%i %p'),'')
                    FROM webhook_files wf
                    INNER JOIN webhook_banks wb ON wb.id = wf.bank_id
                    WHERE wf.id = @id LIMIT 1", conn) { CommandTimeout = 15 })
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    await using var rdr = await cmd.ExecuteReaderAsync();
                    if (!await rdr.ReadAsync()) return Results.NotFound();
                    fileName     = rdr.GetString(0);
                    bankName     = rdr.GetString(1);
                    relPath      = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
                    totalRecords = rdr.GetInt32(3);
                    uploadedAt   = rdr.GetString(4);
                }

                var columns = new System.Collections.Generic.List<string>();
                var rows = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
                var fullPath = Path.Combine(app.Environment.ContentRootPath, relPath.TrimStart('/', '\\'));
                if (!string.IsNullOrEmpty(relPath) && File.Exists(fullPath))
                {
                    var text = await File.ReadAllTextAsync(fullPath, System.Text.Encoding.UTF8);
                    var parsed = ParseCsv(text, 5001);
                    if (parsed.Count > 0) columns = parsed[0];
                    if (parsed.Count > 1) rows = parsed.GetRange(1, parsed.Count - 1);
                }
                return Results.Ok(new { fileName, bankName, uploadedAt, totalRecords, columns, rows });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapGet("/api/agency/manage/agency/{id:int}/finances", async (HttpContext ctx, int id) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            string slug, status;
            await using (var mc = new MySqlConnection(masterConn))
            {
                await mc.OpenAsync();
                await using var q = new MySqlCommand("SELECT slug, status FROM agencies WHERE id=@id LIMIT 1", mc);
                q.Parameters.AddWithValue("@id", id);
                await using var r = await q.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return Results.NotFound(new { message = "Agency not found" });
                slug = r.GetString(0); status = r.GetString(1);
            }
            if (status != "approved")
                return Results.Ok(new { finances = new System.Collections.Generic.List<object>(), note = "Agency is not approved yet — it has no head offices." });
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                var finances = new System.Collections.Generic.List<object>();
                await using var cmd = new MySqlCommand("SELECT id, name, is_active FROM finances ORDER BY name", tc) { CommandTimeout = 15 };
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    finances.Add(new { id = rdr.GetInt32(0), name = rdr.GetString(1), isActive = rdr.GetInt32(2) == 1 });
                return Results.Ok(new { finances });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapGet("/api/agency/manage/integration-accounts", async (HttpContext ctx) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            var accounts = new System.Collections.Generic.List<object>();
            await using var cmd = new MySqlCommand(
                "SELECT id, finance_name, email, status, DATE_FORMAT(created_at,'%d %b %Y') FROM integration_accounts ORDER BY finance_name", conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                accounts.Add(new
                {
                    id = rdr.GetInt32(0), financeName = rdr.GetString(1), email = rdr.GetString(2),
                    status = rdr.GetString(3), createdAt = rdr.GetString(4)
                });
            return Results.Ok(new { accounts });
        });

        app.MapGet("/api/agency/manage/agency/{id:int}/grants", async (HttpContext ctx, int id) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            var grants = new System.Collections.Generic.List<object>();
            await using var cmd = new MySqlCommand(@"
                SELECT g.id, g.integration_account_id, g.finance_id, g.finance_name,
                       COALESCE(g.filters,''), g.active, a.finance_name, a.email
                  FROM agency_integration_grants g
                  JOIN integration_accounts a ON a.id = g.integration_account_id
                 WHERE g.agency_id = @id
                 ORDER BY a.finance_name, g.finance_name", conn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                grants.Add(new
                {
                    id = rdr.GetInt32(0), integrationAccountId = rdr.GetInt32(1),
                    financeId = rdr.GetInt32(2), financeName = rdr.GetString(3),
                    filters = rdr.GetString(4), active = rdr.GetInt32(5) == 1,
                    accountName = rdr.GetString(6), accountEmail = rdr.GetString(7)
                });
            return Results.Ok(new { grants });
        });

        app.MapPost("/api/agency/manage/agency/{id:int}/grants", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            var items = new System.Collections.Generic.List<(int accId, int finId, string finName, string filters, bool active)>();
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                if (doc.RootElement.TryGetProperty("grants", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var seen = new System.Collections.Generic.HashSet<string>();
                    foreach (var el in arr.EnumerateArray())
                    {
                        int accId = el.TryGetProperty("integrationAccountId", out var a) && a.TryGetInt32(out var ai) ? ai : 0;
                        int finId = el.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                        string finName = el.TryGetProperty("financeName", out var fn) && fn.ValueKind == System.Text.Json.JsonValueKind.String ? (fn.GetString() ?? "") : "";
                        string filters = el.TryGetProperty("filters", out var fl)
                            ? (fl.ValueKind == System.Text.Json.JsonValueKind.String ? (fl.GetString() ?? "") : fl.GetRawText())
                            : "";
                        bool active = !el.TryGetProperty("active", out var ac) || ac.ValueKind != System.Text.Json.JsonValueKind.False;
                        if (accId > 0 && finId > 0 && seen.Add(accId + ":" + finId))
                            items.Add((accId, finId, finName, filters, active));
                    }
                }
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            var tx = await conn.BeginTransactionAsync();
            try
            {
                await using (var del = new MySqlCommand("DELETE FROM agency_integration_grants WHERE agency_id=@id", conn, tx))
                {
                    del.Parameters.AddWithValue("@id", id);
                    await del.ExecuteNonQueryAsync();
                }
                foreach (var it in items)
                {
                    await using var ins = new MySqlCommand(@"
                        INSERT INTO agency_integration_grants
                            (agency_id, integration_account_id, finance_id, finance_name, filters, active)
                        VALUES (@ag, @acc, @fin, @fn, @fl, @ac)", conn, tx);
                    ins.Parameters.AddWithValue("@ag", id);
                    ins.Parameters.AddWithValue("@acc", it.accId);
                    ins.Parameters.AddWithValue("@fin", it.finId);
                    ins.Parameters.AddWithValue("@fn", it.finName);
                    ins.Parameters.AddWithValue("@fl", string.IsNullOrWhiteSpace(it.filters) ? (object)DBNull.Value : it.filters);
                    ins.Parameters.AddWithValue("@ac", it.active ? 1 : 0);
                    await ins.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                return Results.Ok(new { ok = true, count = items.Count });
            }
            catch (Exception ex) { await tx.RollbackAsync(); return Results.Problem(ex.Message); }
        });

        static (int id, string email)? IntegAuth(HttpContext ctx)
        {
            var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return null;
            return IntegrationToken.Verify(auth.Substring(7).Trim());
        }

        async Task<(int agencyId, string financeName, string filters)?> IntegFindGrant(int accId, string slug, int financeId)
        {
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT ag.id, g.finance_name, COALESCE(g.filters,'')
                  FROM agency_integration_grants g
                  JOIN agencies ag ON ag.id = g.agency_id
                 WHERE g.integration_account_id=@acc AND ag.slug=@slug AND g.finance_id=@fin
                   AND g.active=1 AND ag.status='approved' LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@acc", accId);
            cmd.Parameters.AddWithValue("@slug", slug);
            cmd.Parameters.AddWithValue("@fin", financeId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return null;
            return (rdr.GetInt32(0), rdr.GetString(1), rdr.GetString(2));
        }

        app.MapPost("/api/integration/account/apply", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string financeName = (dto.GetValueOrDefault("financeName") ?? "").Trim();
            string email = (dto.GetValueOrDefault("email") ?? "").Trim().ToLowerInvariant();
            string password = dto.GetValueOrDefault("password") ?? "";
            if (financeName.Length < 2) return Results.BadRequest(new { message = "Enter your finance name." });
            if (!IsValidEmail(email)) return Results.BadRequest(new { message = "Enter a valid email address." });
            if (password.Length < 6) return Results.BadRequest(new { message = "Password must be at least 6 characters." });
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            try
            {
                await using var cmd = new MySqlCommand(
                    "INSERT INTO integration_accounts (finance_name, email, password) VALUES (@n,@e,@p)", conn);
                cmd.Parameters.AddWithValue("@n", financeName);
                cmd.Parameters.AddWithValue("@e", email);
                cmd.Parameters.AddWithValue("@p", password);
                await cmd.ExecuteNonQueryAsync();
                return Results.Ok(new { ok = true });
            }
            catch (MySqlException mex) when (mex.Number == 1062)
            {
                return Results.Json(new { message = "An account with this email already exists." }, statusCode: 409);
            }
        });

        app.MapPost("/api/integration/account/login", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string email = (dto.GetValueOrDefault("email") ?? "").Trim().ToLowerInvariant();
            string password = dto.GetValueOrDefault("password") ?? "";
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            int accId; string financeName, storedPw, status;
            await using (var cmd = new MySqlCommand(
                "SELECT id, finance_name, password, status FROM integration_accounts WHERE email=@e LIMIT 1", conn))
            {
                cmd.Parameters.AddWithValue("@e", email);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync())
                    return Results.Json(new { message = "Invalid email or password." }, statusCode: 401);
                accId = rdr.GetInt32(0); financeName = rdr.GetString(1); storedPw = rdr.GetString(2); status = rdr.GetString(3);
            }
            if (storedPw != password)
                return Results.Json(new { message = "Invalid email or password." }, statusCode: 401);
            if (status != "active")
                return Results.Json(new { message = "This account has been suspended." }, statusCode: 403);
            await using (var upd = new MySqlCommand("UPDATE integration_accounts SET last_login_at=UTC_TIMESTAMP() WHERE id=@id", conn))
            { upd.Parameters.AddWithValue("@id", accId); await upd.ExecuteNonQueryAsync(); }
            return Results.Ok(new { token = IntegrationToken.Issue(accId, email), financeName, email });
        });

        app.MapGet("/api/integration/account/agencies", async (HttpContext ctx) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            var byAgency = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, object>>();
            await using var cmd = new MySqlCommand(@"
                SELECT ag.slug, ag.name, COALESCE(ag.logo_path,''),
                       g.finance_id, g.finance_name, COALESCE(g.filters,'')
                  FROM agency_integration_grants g
                  JOIN agencies ag ON ag.id = g.agency_id
                 WHERE g.integration_account_id = @acc AND g.active = 1 AND ag.status = 'approved'
                 ORDER BY ag.name, g.finance_name", conn);
            cmd.Parameters.AddWithValue("@acc", me.id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                string slug = rdr.GetString(0);
                if (!byAgency.TryGetValue(slug, out var ag))
                {
                    ag = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["slug"] = slug, ["name"] = rdr.GetString(1),
                        ["logoPath"] = rdr.GetString(2),
                        ["headOffices"] = new System.Collections.Generic.List<object>()
                    };
                    byAgency[slug] = ag;
                }
                ((System.Collections.Generic.List<object>)ag["headOffices"]).Add(new
                {
                    financeId = rdr.GetInt32(3), financeName = rdr.GetString(4), filters = rdr.GetString(5)
                });
            }
            return Results.Ok(new { agencies = byAgency.Values });
        });

        app.MapPost("/api/integration/account/records", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId, branchId, limit, offset; string search, mode;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                branchId = r.TryGetProperty("branchId", out var bb) && bb.TryGetInt32(out var bi) ? bi : 0;
                search = r.TryGetProperty("search", out var q) && q.ValueKind == System.Text.Json.JsonValueKind.String ? (q.GetString() ?? "").Trim() : "";
                mode = r.TryGetProperty("mode", out var mo) && mo.ValueKind == System.Text.Json.JsonValueKind.String ? (mo.GetString() ?? "").Trim().ToLowerInvariant() : "";
                limit = r.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li) ? li : 100;
                offset = r.TryGetProperty("offset", out var o) && o.TryGetInt32(out var oi) ? oi : 0;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (limit <= 0 || limit > 500) limit = 100;
            if (offset < 0) offset = 0;
            var grant = await IntegFindGrant(me.id, slug, financeId);
            if (grant is not { } g) return Results.Json(new { message = "You do not have access to this head office." }, statusCode: 403);

            var regions = new System.Collections.Generic.List<string>();
            var areas = new System.Collections.Generic.List<string>();
            var buckets = new System.Collections.Generic.List<string>();
            try
            {
                if (!string.IsNullOrWhiteSpace(g.filters))
                {
                    using var fd = System.Text.Json.JsonDocument.Parse(g.filters);
                    void Pull(string k, System.Collections.Generic.List<string> into)
                    {
                        if (fd.RootElement.TryGetProperty(k, out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                            foreach (var e in arr.EnumerateArray())
                            { var v = e.GetString(); if (!string.IsNullOrWhiteSpace(v)) into.Add(v.Trim()); }
                    }
                    Pull("regions", regions); Pull("areas", areas); Pull("buckets", buckets);
                }
            }
            catch { }

            var colList = string.Join(", ", IntegRecordCols.Select(c => "vr.`" + c.Col + "`"));
            var where = new System.Collections.Generic.List<string> { "b.finance_id = @fin" };
            var ps = new System.Collections.Generic.List<(string, object)> { ("@fin", financeId) };
            void InClause(string col, System.Collections.Generic.List<string> vals, string p)
            {
                if (vals.Count == 0) return;
                var names = new System.Collections.Generic.List<string>();
                for (int i = 0; i < vals.Count; i++) { var pn = p + i; names.Add(pn); ps.Add((pn, vals[i])); }
                where.Add($"vr.`{col}` IN (" + string.Join(",", names) + ")");
            }
            InClause("region", regions, "@rg"); InClause("area", areas, "@ar"); InClause("bucket", buckets, "@bk");
            if (branchId > 0) { where.Add("vr.branch_id = @bid"); ps.Add(("@bid", branchId)); }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var digits = new string(search.Where(char.IsDigit).ToArray());
                if (mode == "chassis")
                {
                    where.Add("EXISTS (SELECT 1 FROM chassis_info ci WHERE ci.vehicle_record_id=vr.id AND ci.last5=@cs)");
                    ps.Add(("@cs", search.ToUpperInvariant()));
                }
                else if (digits.Length == 4 && digits.Length == search.Length)
                {
                    where.Add("EXISTS (SELECT 1 FROM rc_info ri WHERE ri.vehicle_record_id=vr.id AND ri.last4=@rc)");
                    ps.Add(("@rc", digits));
                }
                else
                {
                    where.Add("(vr.vehicle_no LIKE @q OR vr.chassis_no LIKE @q OR vr.agreement_no LIKE @q OR vr.customer_name LIKE @q)");
                    ps.Add(("@q", "%" + search + "%"));
                }
            }
            string whereSql = "WHERE " + string.Join(" AND ", where);

            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                long total;
                await using (var cc = new MySqlCommand($"SELECT COUNT(*) FROM vehicle_records vr JOIN branches b ON b.id=vr.branch_id {whereSql}", tc) { CommandTimeout = 20 })
                { foreach (var (k, v) in ps) cc.Parameters.AddWithValue(k, v); total = Convert.ToInt64(await cc.ExecuteScalarAsync()); }

                var rows = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
                var ids = new System.Collections.Generic.List<long>();
                await using (var cmd = new MySqlCommand($"SELECT vr.id, {colList} FROM vehicle_records vr JOIN branches b ON b.id=vr.branch_id {whereSql} ORDER BY vr.id DESC LIMIT {limit} OFFSET {offset}", tc) { CommandTimeout = 30 })
                {
                    foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
                    await using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        ids.Add(rdr.GetInt64(0));
                        var row = new System.Collections.Generic.List<string>(IntegRecordCols.Length);
                        for (int i = 0; i < IntegRecordCols.Length; i++) row.Add(rdr.IsDBNull(i + 1) ? "" : rdr.GetValue(i + 1)?.ToString() ?? "");
                        rows.Add(row);
                    }
                }
                return Results.Ok(new { columns = IntegRecordCols.Select(c => c.Label).ToArray(), rows, ids, total, limit, offset, financeName = g.financeName });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/files", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            var grant = await IntegFindGrant(me.id, slug, financeId);
            if (grant is not { } g) return Results.Json(new { message = "You do not have access to this head office." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                var files = new System.Collections.Generic.List<object>();
                await using var cmd = new MySqlCommand(@"
                    SELECT wf.file_name, wf.total_records, COALESCE(DATE_FORMAT(wf.created_at,'%d %b %Y %h:%i %p'),'')
                    FROM webhook_files wf JOIN webhook_banks wb ON wb.id=wf.bank_id
                    WHERE wb.bank_name=@b ORDER BY wf.id DESC", tc) { CommandTimeout = 15 };
                cmd.Parameters.AddWithValue("@b", g.financeName);
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    files.Add(new { fileName = rdr.GetString(0), totalRecords = rdr.GetInt32(1), uploadedAt = rdr.GetString(2) });
                return Results.Ok(new { files });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        var integWebhookRoot = Path.Combine(app.Environment.ContentRootPath, "webhook-files");
        try { Directory.CreateDirectory(integWebhookRoot); } catch { }

        app.MapPost("/api/integration/account/upload", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug = "", fileName = "", vehicleType = "";
            int financeId = 0;
            var headers = new System.Collections.Generic.List<string>();
            var rows = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                fileName = r.TryGetProperty("fileName", out var fnm) ? (fnm.GetString() ?? "") : "";
                vehicleType = r.TryGetProperty("vehicleType", out var vt) && vt.ValueKind == System.Text.Json.JsonValueKind.String ? (vt.GetString() ?? "") : "";
                if (r.TryGetProperty("headers", out var hs) && hs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var h in hs.EnumerateArray()) headers.Add(h.GetString() ?? "");
                if (r.TryGetProperty("rows", out var rs) && rs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var row in rs.EnumerateArray())
                    {
                        var cells = new System.Collections.Generic.List<string>();
                        if (row.ValueKind == System.Text.Json.JsonValueKind.Array)
                            foreach (var c in row.EnumerateArray())
                                cells.Add(c.ValueKind == System.Text.Json.JsonValueKind.String ? (c.GetString() ?? "") : c.ToString());
                        rows.Add(cells);
                    }
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (headers.Count == 0 || rows.Count == 0) return Results.BadRequest(new { message = "Empty sheet." });

            var grant = await IntegFindGrant(me.id, slug, financeId);
            if (grant is not { } g) return Results.Json(new { message = "You do not have access to this head office." }, statusCode: 403);

            var safeSlug = Regex.Replace(slug, "[^a-z0-9_-]", "");
            var slotDir = Path.Combine(integWebhookRoot, safeSlug);
            Directory.CreateDirectory(slotDir);
            var baseName = string.IsNullOrWhiteSpace(fileName) ? "integration-upload" : fileName.Replace(" ", "_");
            var csvName = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{baseName}.csv";
            var csvPath = Path.Combine(slotDir, csvName);
            var relPath = Path.Combine("webhook-files", safeSlug, csvName);
            static string Csv(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            int totalRows = 0;
            try
            {
                await using var sw = new StreamWriter(csvPath, false, System.Text.Encoding.UTF8);
                await sw.WriteLineAsync(string.Join(",", headers.Select(Csv)));
                foreach (var row in rows)
                {
                    var cells = new System.Collections.Generic.List<string>(headers.Count);
                    for (int i = 0; i < headers.Count; i++) cells.Add(Csv(i < row.Count ? row[i] : ""));
                    await sw.WriteLineAsync(string.Join(",", cells));
                    totalRows++;
                }
            }
            catch (Exception ex) { return Results.Problem($"CSV write failed: {ex.Message}"); }
            try
            {
                await using var c = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await c.OpenAsync();
                int bankId;
                await using (var bankCmd = new MySqlCommand(@"
                    INSERT INTO webhook_banks (bank_name) VALUES (@n) ON DUPLICATE KEY UPDATE bank_name=bank_name;
                    SELECT id FROM webhook_banks WHERE bank_name=@n LIMIT 1;", c))
                { bankCmd.Parameters.AddWithValue("@n", g.financeName); bankId = Convert.ToInt32(await bankCmd.ExecuteScalarAsync()); }
                await using (var fileCmd = new MySqlCommand(@"
                    INSERT INTO webhook_files (bank_id, file_name, file_path, vehicle_type, uploaded_by, uploaded_date, total_records)
                    VALUES (@bid,@fn,@fp,@vt,@ub,@ud,@tr)", c))
                {
                    fileCmd.Parameters.AddWithValue("@bid", bankId);
                    fileCmd.Parameters.AddWithValue("@fn", fileName.Length > 0 ? fileName : csvName);
                    fileCmd.Parameters.AddWithValue("@fp", relPath);
                    fileCmd.Parameters.AddWithValue("@vt", vehicleType ?? "");
                    fileCmd.Parameters.AddWithValue("@ub", me.email);
                    fileCmd.Parameters.AddWithValue("@ud", DateTime.UtcNow.ToString("dd MMM yyyy"));
                    fileCmd.Parameters.AddWithValue("@tr", totalRows);
                    await fileCmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex) { return Results.Problem($"DB insert failed: {ex.Message}"); }
            return Results.Ok(new { ok = true, records = totalRows });
        });

        app.MapPost("/api/integration/account/branches", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "You do not have access to this head office." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                var branches = new List<object>();
                await using var cmd = new MySqlCommand(@"
                    SELECT id, name, COALESCE(total_records,0),
                           COALESCE(DATE_FORMAT(uploaded_at,'%d %b %Y %h:%i %p'),''),
                           COALESCE(address,''), COALESCE(contact1,'')
                    FROM branches WHERE finance_id=@fin ORDER BY name", tc) { CommandTimeout = 15 };
                cmd.Parameters.AddWithValue("@fin", financeId);
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    branches.Add(new { id = rdr.GetInt32(0), name = rdr.GetString(1), totalRecords = rdr.GetInt64(2), uploadedAt = rdr.GetString(3), address = rdr.GetString(4), contact = rdr.GetString(5) });
                return Results.Ok(new { branches });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/branch/create", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug, name, address, contact; int financeId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                name = (r.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "").Trim();
                address = r.TryGetProperty("address", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.String ? (a.GetString() ?? "") : "";
                contact = r.TryGetProperty("contact", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String ? (c.GetString() ?? "") : "";
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (name.Length < 1) return Results.BadRequest(new { message = "Enter a branch name." });
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "You do not have access to this head office." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO branches (finance_id, name, contact1, address) VALUES (@fin,@n,@c,@addr);
                    SELECT LAST_INSERT_ID();", tc);
                cmd.Parameters.AddWithValue("@fin", financeId);
                cmd.Parameters.AddWithValue("@n", IntegCap(name, 255));
                cmd.Parameters.AddWithValue("@c", IntegCap(contact, 255));
                cmd.Parameters.AddWithValue("@addr", address ?? "");
                var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return Results.Ok(new { id, name });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/record", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId; long recordId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                recordId = r.TryGetProperty("recordId", out var rid) && rid.TryGetInt64(out var ri) ? ri : 0;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "No access." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                var colList = string.Join(", ", IntegFullCols.Select(c => IntegColExpr(c.Col)));
                await using var cmd = new MySqlCommand($@"
                    SELECT {colList}, b.name FROM vehicle_records vr
                    JOIN branches b ON b.id=vr.branch_id
                    WHERE vr.id=@id AND b.finance_id=@fin LIMIT 1", tc) { CommandTimeout = 15 };
                cmd.Parameters.AddWithValue("@id", recordId);
                cmd.Parameters.AddWithValue("@fin", financeId);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Record not found." });
                var fields = new List<object>();
                for (int i = 0; i < IntegFullCols.Length; i++)
                    fields.Add(new { label = IntegFullCols[i].Label, value = rdr.IsDBNull(i) ? "" : rdr.GetValue(i)?.ToString() ?? "" });
                return Results.Ok(new { fields, branchName = rdr.IsDBNull(IntegFullCols.Length) ? "" : rdr.GetString(IntegFullCols.Length) });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/import", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug, fileName = ""; int financeId, branchId;
            var headers = new List<string>(); var rows = new List<List<string>>();
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                branchId = r.TryGetProperty("branchId", out var b) && b.TryGetInt32(out var bi) ? bi : 0;
                fileName = r.TryGetProperty("fileName", out var fn) && fn.ValueKind == System.Text.Json.JsonValueKind.String ? (fn.GetString() ?? "") : "";
                if (r.TryGetProperty("headers", out var hs) && hs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var h in hs.EnumerateArray()) headers.Add(h.GetString() ?? "");
                if (r.TryGetProperty("rows", out var rs) && rs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var row in rs.EnumerateArray())
                    {
                        var cells = new List<string>();
                        if (row.ValueKind == System.Text.Json.JsonValueKind.Array)
                            foreach (var c in row.EnumerateArray())
                                cells.Add(c.ValueKind == System.Text.Json.JsonValueKind.String ? (c.GetString() ?? "") : c.ToString());
                        rows.Add(cells);
                    }
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (headers.Count == 0 || rows.Count == 0) return Results.BadRequest(new { message = "Empty sheet." });
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "No access to this head office." }, statusCode: 403);

            var mapped = new List<(int idx, string col)>();
            var unknown = new List<string>();
            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i];
                if (string.IsNullOrWhiteSpace(h)) continue;
                if (IntegImportCols.TryGetValue(IntegNormKey(h), out var col)) mapped.Add((i, col));
                else unknown.Add(h);
            }
            if (unknown.Count > 0)
                return Results.BadRequest(new { message = "These columns are not recognised and must be removed or renamed: " + string.Join(", ", unknown), unknownColumns = unknown });
            if (mapped.Count == 0) return Results.BadRequest(new { message = "No known columns to import." });

            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                await using (var bc = new MySqlCommand("SELECT COUNT(*) FROM branches WHERE id=@bid AND finance_id=@fin", tc))
                {
                    bc.Parameters.AddWithValue("@bid", branchId); bc.Parameters.AddWithValue("@fin", financeId);
                    if (Convert.ToInt32(await bc.ExecuteScalarAsync()) == 0)
                        return Results.BadRequest(new { message = "Select a valid branch under this head office." });
                }
                int inserted = await IntegImportToBranch(tc, slug, financeId, branchId, headers, mapped, rows, fileName, me.email, app.Environment.ContentRootPath);
                return Results.Ok(new { ok = true, records = inserted });
            }
            catch (Exception ex) { return Results.Problem($"Import failed: {ex.Message}"); }
        });

        app.MapPost("/api/integration/account/search-logs", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug, fromDate, toDate, q; int financeId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                fromDate = r.TryGetProperty("fromDate", out var fd) && fd.ValueKind == System.Text.Json.JsonValueKind.String ? (fd.GetString() ?? "") : "";
                toDate = r.TryGetProperty("toDate", out var td) && td.ValueKind == System.Text.Json.JsonValueKind.String ? (td.GetString() ?? "") : "";
                q = r.TryGetProperty("q", out var qq) && qq.ValueKind == System.Text.Json.JsonValueKind.String ? (qq.GetString() ?? "").Trim() : "";
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "No access." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                var sql = new System.Text.StringBuilder(@"
                    SELECT sl.id, sl.user_id, u.name, u.mobile, sl.vehicle_no, sl.chassis_no, sl.model,
                           sl.lat, sl.lng, COALESCE(sl.address,''),
                           DATE_FORMAT(CONVERT_TZ(sl.device_time,'+00:00','+05:30'),'%d %b %Y %h:%i %p')
                    FROM search_logs sl
                    JOIN app_users u ON u.id=sl.user_id
                    WHERE EXISTS (SELECT 1 FROM vehicle_records vr JOIN branches b ON b.id=vr.branch_id
                                  WHERE b.finance_id=@fin AND vr.vehicle_no = sl.vehicle_no)");
                var ps = new List<(string, object)> { ("@fin", financeId) };
                if (!string.IsNullOrWhiteSpace(fromDate)) { sql.Append(" AND DATE(sl.server_time)>=@fd"); ps.Add(("@fd", fromDate)); }
                if (!string.IsNullOrWhiteSpace(toDate)) { sql.Append(" AND DATE(sl.server_time)<=@td"); ps.Add(("@td", toDate)); }
                if (!string.IsNullOrWhiteSpace(q)) { sql.Append(" AND (sl.vehicle_no LIKE @q OR sl.chassis_no LIKE @q)"); ps.Add(("@q", "%" + q + "%")); }
                sql.Append(" ORDER BY sl.server_time DESC LIMIT 2000");
                await using var cmd = new MySqlCommand(sql.ToString(), tc) { CommandTimeout = 40 };
                foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
                var logs = new List<object>();
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    logs.Add(new
                    {
                        id = rdr.GetInt64(0), userId = rdr.GetInt64(1), userName = rdr.GetString(2), userMobile = rdr.GetString(3),
                        vehicleNo = rdr.GetString(4), chassisNo = rdr.GetString(5), model = rdr.GetString(6),
                        lat = rdr.IsDBNull(7) ? (double?)null : rdr.GetDouble(7), lng = rdr.IsDBNull(8) ? (double?)null : rdr.GetDouble(8),
                        address = rdr.GetString(9), time = rdr.GetString(10)
                    });
                return Results.Ok(new { logs });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/remove-agency", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                slug = doc.RootElement.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (string.IsNullOrWhiteSpace(slug)) return Results.BadRequest(new { message = "No agency specified." });
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                DELETE g FROM agency_integration_grants g
                JOIN agencies ag ON ag.id=g.agency_id
                WHERE g.integration_account_id=@acc AND ag.slug=@slug", conn);
            cmd.Parameters.AddWithValue("@acc", me.id);
            cmd.Parameters.AddWithValue("@slug", slug);
            int n = await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { ok = true, removed = n });
        });

        app.MapPost("/api/integration/account/agent", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId; long userId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                userId = r.TryGetProperty("userId", out var u) && u.TryGetInt64(out var ui) ? ui : 0;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "No access." }, statusCode: 403);
            const string BASE = "https://api.crmrecoverysoftware.com";
            string PhotoUrl(string? rel) => string.IsNullOrEmpty(rel) ? "" : BASE + "/uploads/" + rel.TrimStart('/');
            string Pfp(string? p)
            {
                if (string.IsNullOrEmpty(p)) return "";
                if (p.StartsWith("http") || p.StartsWith("data:")) return p;
                if (p.Length < 256 && p.Contains('/') && !p.Contains('+') && !p.Contains('=')) return BASE + "/uploads/" + p.TrimStart('/');
                return "data:image/jpeg;base64," + p;
            }
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                object? profile = null;
                await using (var cmd = new MySqlCommand(@"
                    SELECT name, mobile, COALESCE(address,''), COALESCE(pincode,''), pfp,
                           is_active, is_admin, COALESCE(is_stopped,0), COALESCE(is_blacklisted,0),
                           COALESCE(account_number,''), COALESCE(ifsc_code,''),
                           COALESCE(DATE_FORMAT(last_seen,'%d %b %Y %h:%i %p'),''), last_lat, last_lng,
                           COALESCE(DATE_FORMAT(created_at,'%d %b %Y'),''),
                           COALESCE(kyc_aadhaar_name,''), COALESCE(kyc_aadhaar_dob,''), COALESCE(kyc_aadhaar_gender,''),
                           COALESCE(kyc_aadhaar_address,''), COALESCE(kyc_aadhaar_last4,''), COALESCE(kyc_aadhaar_number,''),
                           COALESCE(kyc_aadhaar_verified,0), COALESCE(kyc_pan,''), COALESCE(kyc_pan_name,''), COALESCE(kyc_pan_verified,0),
                           COALESCE(kyc_bank_holder,''), COALESCE(kyc_bank_verified,0), COALESCE(kyc_reg_location,''),
                           COALESCE(kyc_status,'pending'), COALESCE(kyc_reject_note,''), COALESCE(DATE_FORMAT(kyc_verified_at,'%d %b %Y'),'')
                    FROM app_users WHERE id=@uid LIMIT 1", tc) { CommandTimeout = 15 })
                {
                    cmd.Parameters.AddWithValue("@uid", userId);
                    await using var rdr = await cmd.ExecuteReaderAsync();
                    if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Agent not found." });
                    string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
                    bool B(int i) => !rdr.IsDBNull(i) && rdr.GetInt32(i) != 0;
                    profile = new
                    {
                        name = S(0), mobile = S(1), address = S(2), pincode = S(3), pfp = Pfp(rdr.IsDBNull(4) ? null : rdr.GetString(4)),
                        isActive = B(5), isAdmin = B(6), isStopped = B(7), isBlacklisted = B(8),
                        accountNumber = S(9), ifsc = S(10), lastSeen = S(11),
                        lastLat = rdr.IsDBNull(12) ? (double?)null : rdr.GetDouble(12), lastLng = rdr.IsDBNull(13) ? (double?)null : rdr.GetDouble(13),
                        createdAt = S(14),
                        kyc = new
                        {
                            status = S(28), rejectNote = S(29), verifiedAt = S(30),
                            aadhaar = new { name = S(15), dob = S(16), gender = S(17), address = S(18), last4 = S(19), number = S(20), verified = B(21) },
                            pan = new { number = S(22), name = S(23), verified = B(24) },
                            bank = new { holder = S(25), verified = B(26) },
                            regLocation = S(27)
                        }
                    };
                }
                string af = "", ab = "", pf = "", selfie = "", aphoto = "";
                try
                {
                    await using var kc = new MySqlCommand("SELECT aadhaar_front, aadhaar_back, pan_front, selfie, aadhaar_photo FROM user_kyc WHERE user_id=@uid LIMIT 1", tc);
                    kc.Parameters.AddWithValue("@uid", userId);
                    await using var kr = await kc.ExecuteReaderAsync();
                    if (await kr.ReadAsync())
                    {
                        af = PhotoUrl(kr.IsDBNull(0) ? null : kr.GetString(0)); ab = PhotoUrl(kr.IsDBNull(1) ? null : kr.GetString(1));
                        pf = PhotoUrl(kr.IsDBNull(2) ? null : kr.GetString(2)); selfie = PhotoUrl(kr.IsDBNull(3) ? null : kr.GetString(3));
                        aphoto = PhotoUrl(kr.IsDBNull(4) ? null : kr.GetString(4));
                    }
                }
                catch { }
                return Results.Ok(new { profile, photos = new { aadhaarFront = af, aadhaarBack = ab, panFront = pf, selfie, aadhaarPhoto = aphoto } });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/vehicle", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug, vehicleNo; int financeId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                vehicleNo = (r.TryGetProperty("vehicleNo", out var v) ? (v.GetString() ?? "") : "").Trim();
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (string.IsNullOrWhiteSpace(vehicleNo)) return Results.BadRequest(new { message = "No vehicle number." });
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "No access." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                var colList = string.Join(", ", IntegFullCols.Select(c => IntegColExpr(c.Col)));
                var records = new List<object>();
                await using var cmd = new MySqlCommand($@"
                    SELECT {colList}, b.name FROM vehicle_records vr
                    JOIN branches b ON b.id=vr.branch_id
                    WHERE b.finance_id=@fin AND vr.vehicle_no=@vno ORDER BY vr.id DESC LIMIT 20", tc) { CommandTimeout = 15 };
                cmd.Parameters.AddWithValue("@fin", financeId);
                cmd.Parameters.AddWithValue("@vno", vehicleNo);
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    var fields = new List<object>();
                    for (int i = 0; i < IntegFullCols.Length; i++)
                        fields.Add(new { label = IntegFullCols[i].Label, value = rdr.IsDBNull(i) ? "" : rdr.GetValue(i)?.ToString() ?? "" });
                    records.Add(new { branchName = rdr.IsDBNull(IntegFullCols.Length) ? "" : rdr.GetString(IntegFullCols.Length), fields });
                }
                return Results.Ok(new { records });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/uploads", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "No access." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                var uploads = new List<object>();
                await using var cmd = new MySqlCommand(@"
                    SELECT u.id, u.file_name, u.total_records,
                           COALESCE(DATE_FORMAT(u.created_at,'%d %b %Y %h:%i %p'),''),
                           COALESCE(u.uploaded_by,''), b.name, (u.file_path IS NOT NULL AND u.file_path<>'')
                    FROM integration_uploads u JOIN branches b ON b.id=u.branch_id
                    WHERE u.finance_id=@fin ORDER BY u.id DESC", tc) { CommandTimeout = 15 };
                cmd.Parameters.AddWithValue("@fin", financeId);
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    uploads.Add(new
                    {
                        id = rdr.GetInt32(0), fileName = rdr.GetString(1), totalRecords = rdr.GetInt32(2),
                        createdAt = rdr.GetString(3), uploadedBy = rdr.GetString(4), branchName = rdr.GetString(5),
                        hasFile = rdr.GetInt32(6) == 1
                    });
                return Results.Ok(new { uploads });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/upload/delete", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId, uploadId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                uploadId = r.TryGetProperty("uploadId", out var u) && u.TryGetInt32(out var ui) ? ui : 0;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "No access." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                int branchId; string filePath = "";
                await using (var sel = new MySqlCommand("SELECT branch_id, COALESCE(file_path,'') FROM integration_uploads WHERE id=@id AND finance_id=@fin LIMIT 1", tc))
                {
                    sel.Parameters.AddWithValue("@id", uploadId);
                    sel.Parameters.AddWithValue("@fin", financeId);
                    await using var rdr = await sel.ExecuteReaderAsync();
                    if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Upload not found." });
                    branchId = rdr.GetInt32(0); filePath = rdr.GetString(1);
                }
                int removed;
                await using (var del = new MySqlCommand("DELETE FROM vehicle_records WHERE upload_id=@id AND branch_id=@bid", tc) { CommandTimeout = 120 })
                {
                    del.Parameters.AddWithValue("@id", uploadId);
                    del.Parameters.AddWithValue("@bid", branchId);
                    removed = await del.ExecuteNonQueryAsync();
                }
                await using (var du = new MySqlCommand("DELETE FROM integration_uploads WHERE id=@id", tc))
                { du.Parameters.AddWithValue("@id", uploadId); await du.ExecuteNonQueryAsync(); }
                await using (var st = new MySqlCommand("UPDATE branches SET total_records=(SELECT COUNT(*) FROM vehicle_records WHERE branch_id=@bid) WHERE id=@bid", tc) { CommandTimeout = 60 })
                { st.Parameters.AddWithValue("@bid", branchId); await st.ExecuteNonQueryAsync(); }
                if (!string.IsNullOrEmpty(filePath))
                {
                    try { var full = Path.Combine(app.Environment.ContentRootPath, filePath.Replace('/', Path.DirectorySeparatorChar)); if (File.Exists(full)) File.Delete(full); }
                    catch { }
                }
                return Results.Ok(new { ok = true, removed });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/upload/download", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId, uploadId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                uploadId = r.TryGetProperty("uploadId", out var u) && u.TryGetInt32(out var ui) ? ui : 0;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "No access." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                string fileName = "upload", filePath = "";
                await using (var sel = new MySqlCommand("SELECT COALESCE(file_name,'upload'), COALESCE(file_path,'') FROM integration_uploads WHERE id=@id AND finance_id=@fin LIMIT 1", tc))
                {
                    sel.Parameters.AddWithValue("@id", uploadId);
                    sel.Parameters.AddWithValue("@fin", financeId);
                    await using var rdr = await sel.ExecuteReaderAsync();
                    if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "Upload not found." });
                    fileName = rdr.GetString(0); filePath = rdr.GetString(1);
                }
                if (string.IsNullOrEmpty(filePath)) return Results.NotFound(new { message = "No file stored for this upload." });
                var full = Path.Combine(app.Environment.ContentRootPath, filePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) return Results.NotFound(new { message = "File is no longer available." });
                var bytes = await File.ReadAllBytesAsync(full);
                var dl = Path.GetFileNameWithoutExtension(fileName) + ".csv";
                return Results.File(bytes, "text/csv", dl);
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapGet("/api/integration/account/all-targets", async (HttpContext ctx) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            var agencies = new List<Dictionary<string, object>>();
            var bySlug = new Dictionary<string, Dictionary<string, object>>();
            var hoByKey = new Dictionary<string, Dictionary<string, object>>();
            var favSet = new HashSet<string>();
            await using (var conn = new MySqlConnection(masterConn))
            {
                await conn.OpenAsync();
                await using (var fc = new MySqlCommand("SELECT agency_id, branch_id FROM integration_favourite_branches WHERE integration_account_id=@acc", conn))
                {
                    fc.Parameters.AddWithValue("@acc", me.id);
                    await using var fr = await fc.ExecuteReaderAsync();
                    while (await fr.ReadAsync()) favSet.Add(fr.GetInt32(0) + ":" + fr.GetInt32(1));
                }
                await using var cmd = new MySqlCommand(@"
                    SELECT ag.id, ag.slug, ag.name, COALESCE(ag.logo_path,''), g.finance_id, g.finance_name
                    FROM agency_integration_grants g JOIN agencies ag ON ag.id=g.agency_id
                    WHERE g.integration_account_id=@acc AND g.active=1 AND ag.status='approved'
                    ORDER BY ag.name, g.finance_name", conn);
                cmd.Parameters.AddWithValue("@acc", me.id);
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    int agencyId = rdr.GetInt32(0);
                    string slug = rdr.GetString(1);
                    if (!bySlug.TryGetValue(slug, out var ag))
                    {
                        ag = new Dictionary<string, object> { ["id"] = agencyId, ["slug"] = slug, ["name"] = rdr.GetString(2), ["logoPath"] = rdr.GetString(3), ["headOffices"] = new List<object>() };
                        bySlug[slug] = ag; agencies.Add(ag);
                    }
                    int finId = rdr.GetInt32(4);
                    var ho = new Dictionary<string, object> { ["financeId"] = finId, ["financeName"] = rdr.GetString(5), ["branches"] = new List<object>() };
                    ((List<object>)ag["headOffices"]).Add(ho);
                    hoByKey[slug + ":" + finId] = ho;
                }
            }
            foreach (var ag in agencies)
            {
                string slug = (string)ag["slug"];
                int agencyId = (int)ag["id"];
                var hos = (List<object>)ag["headOffices"];
                var finIds = hos.Select(h => (int)((Dictionary<string, object>)h)["financeId"]).ToList();
                if (finIds.Count == 0) continue;
                try
                {
                    await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                    await tc.OpenAsync();
                    await using var cmd = new MySqlCommand(
                        "SELECT id, name, finance_id, COALESCE(total_records,0) FROM branches WHERE finance_id IN (" + string.Join(",", finIds) + ") ORDER BY name", tc) { CommandTimeout = 15 };
                    await using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        int finId = rdr.GetInt32(2);
                        int brId = rdr.GetInt32(0);
                        if (hoByKey.TryGetValue(slug + ":" + finId, out var ho))
                            ((List<object>)ho["branches"]).Add(new { id = brId, name = rdr.GetString(1), totalRecords = rdr.GetInt64(3), isFavourite = favSet.Contains(agencyId + ":" + brId) });
                    }
                }
                catch { }
            }
            return Results.Ok(new { agencies });
        });

        app.MapPost("/api/integration/account/favourite", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId, branchId; bool fav;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                branchId = r.TryGetProperty("branchId", out var b) && b.TryGetInt32(out var bi) ? bi : 0;
                fav = r.TryGetProperty("favourite", out var fv) && fv.ValueKind == System.Text.Json.JsonValueKind.True;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            var grant = await IntegFindGrant(me.id, slug, financeId);
            if (grant is not { } g) return Results.Json(new { message = "No access." }, statusCode: 403);
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            if (fav)
            {
                await using (var clr = new MySqlCommand("DELETE FROM integration_favourite_branches WHERE integration_account_id=@acc", conn))
                { clr.Parameters.AddWithValue("@acc", me.id); await clr.ExecuteNonQueryAsync(); }
                await using var cmd = new MySqlCommand("INSERT IGNORE INTO integration_favourite_branches (integration_account_id, agency_id, finance_id, branch_id) VALUES (@acc,@ag,@fin,@br)", conn);
                cmd.Parameters.AddWithValue("@acc", me.id);
                cmd.Parameters.AddWithValue("@ag", g.agencyId);
                cmd.Parameters.AddWithValue("@fin", financeId);
                cmd.Parameters.AddWithValue("@br", branchId);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                await using var cmd = new MySqlCommand("DELETE FROM integration_favourite_branches WHERE integration_account_id=@acc AND agency_id=@ag AND branch_id=@br", conn);
                cmd.Parameters.AddWithValue("@acc", me.id);
                cmd.Parameters.AddWithValue("@ag", g.agencyId);
                cmd.Parameters.AddWithValue("@br", branchId);
                await cmd.ExecuteNonQueryAsync();
            }
            return Results.Ok(new { ok = true, favourite = fav });
        });

        app.MapPost("/api/integration/account/search-log/delete", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug; int financeId; long logId;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                financeId = r.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                logId = r.TryGetProperty("logId", out var l) && l.TryGetInt64(out var li) ? li : 0;
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (await IntegFindGrant(me.id, slug, financeId) is null)
                return Results.Json(new { message = "No access." }, statusCode: 403);
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                await using var cmd = new MySqlCommand(@"
                    DELETE sl FROM search_logs sl
                    WHERE sl.id=@id AND EXISTS (SELECT 1 FROM vehicle_records vr JOIN branches b ON b.id=vr.branch_id
                                                WHERE b.finance_id=@fin AND vr.vehicle_no = sl.vehicle_no)", tc) { CommandTimeout = 30 };
                cmd.Parameters.AddWithValue("@id", logId);
                cmd.Parameters.AddWithValue("@fin", financeId);
                int n = await cmd.ExecuteNonQueryAsync();
                return Results.Ok(new { ok = true, removed = n });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/message/send", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string slug, message;
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                slug = r.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                message = (r.TryGetProperty("message", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String ? (m.GetString() ?? "") : "").Trim();
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (message.Length == 0) return Results.BadRequest(new { message = "Enter a message." });
            if (message.Length > 4000) message = message.Substring(0, 4000);

            string financeName;
            await using (var mc = new MySqlConnection(masterConn))
            {
                await mc.OpenAsync();
                await using var cmd = new MySqlCommand(@"
                    SELECT a.finance_name FROM integration_accounts a
                    WHERE a.id=@acc AND EXISTS (
                        SELECT 1 FROM agency_integration_grants g JOIN agencies ag ON ag.id=g.agency_id
                        WHERE g.integration_account_id=@acc AND ag.slug=@slug AND g.active=1 AND ag.status='approved')
                    LIMIT 1", mc);
                cmd.Parameters.AddWithValue("@acc", me.id);
                cmd.Parameters.AddWithValue("@slug", slug);
                var fn = await cmd.ExecuteScalarAsync();
                if (fn == null || fn == DBNull.Value)
                    return Results.Json(new { message = "You are not integrated with this agency." }, statusCode: 403);
                financeName = (string)fn;
            }
            try
            {
                await using var tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug));
                await tc.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "INSERT INTO integration_agency_messages (integration_account_id, from_finance_name, from_email, message) VALUES (@acc,@fn,@em,@msg)", tc);
                cmd.Parameters.AddWithValue("@acc", me.id);
                cmd.Parameters.AddWithValue("@fn", financeName);
                cmd.Parameters.AddWithValue("@em", me.email);
                cmd.Parameters.AddWithValue("@msg", message);
                await cmd.ExecuteNonQueryAsync();
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex) { return Results.Problem(ex.Message); }
        });

        app.MapPost("/api/integration/account/import-universal", async (HttpContext ctx, HttpRequest req) =>
        {
            var who = IntegAuth(ctx);
            if (who is not { } me) return Results.Unauthorized();
            string fileName = "";
            var headers = new List<string>(); var rows = new List<List<string>>();
            var targets = new List<(string slug, int financeId, int branchId)>();
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
                var r = doc.RootElement;
                fileName = r.TryGetProperty("fileName", out var fn) && fn.ValueKind == System.Text.Json.JsonValueKind.String ? (fn.GetString() ?? "") : "";
                if (r.TryGetProperty("headers", out var hs) && hs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var h in hs.EnumerateArray()) headers.Add(h.GetString() ?? "");
                if (r.TryGetProperty("rows", out var rs) && rs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var row in rs.EnumerateArray())
                    {
                        var cells = new List<string>();
                        if (row.ValueKind == System.Text.Json.JsonValueKind.Array)
                            foreach (var c in row.EnumerateArray()) cells.Add(c.ValueKind == System.Text.Json.JsonValueKind.String ? (c.GetString() ?? "") : c.ToString());
                        rows.Add(cells);
                    }
                if (r.TryGetProperty("targets", out var ts) && ts.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var t in ts.EnumerateArray())
                    {
                        string tslug = t.TryGetProperty("agencySlug", out var s) ? (s.GetString() ?? "") : "";
                        int tfin = t.TryGetProperty("financeId", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
                        int tbr = t.TryGetProperty("branchId", out var b) && b.TryGetInt32(out var bi) ? bi : 0;
                        if (!string.IsNullOrWhiteSpace(tslug) && tfin > 0 && tbr > 0) targets.Add((tslug, tfin, tbr));
                    }
            }
            catch { return Results.BadRequest(new { message = "Invalid body." }); }
            if (headers.Count == 0 || rows.Count == 0) return Results.BadRequest(new { message = "Empty sheet." });
            if (targets.Count == 0) return Results.BadRequest(new { message = "Select at least one branch to send to." });

            var mapped = new List<(int idx, string col)>();
            var unknown = new List<string>();
            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i];
                if (string.IsNullOrWhiteSpace(h)) continue;
                if (IntegImportCols.TryGetValue(IntegNormKey(h), out var col)) mapped.Add((i, col));
                else unknown.Add(h);
            }
            if (unknown.Count > 0) return Results.BadRequest(new { message = "These columns are not recognised and must be removed or renamed: " + string.Join(", ", unknown), unknownColumns = unknown });
            if (mapped.Count == 0) return Results.BadRequest(new { message = "No known columns to import." });

            var results = new List<object>();
            int totalInserted = 0, okCount = 0, failCount = 0;
            foreach (var g in targets.GroupBy(t => t.slug))
            {
                MySqlConnection? tc = null;
                try { tc = new MySqlConnection(TenantContext.BuildTenantConn(mysqlHost, mysqlPort, g.Key)); await tc.OpenAsync(); }
                catch (Exception ex)
                {
                    foreach (var t in g) { results.Add(new { agencySlug = t.slug, financeId = t.financeId, branchId = t.branchId, ok = false, error = "Agency unavailable: " + ex.Message }); failCount++; }
                    if (tc != null) await tc.DisposeAsync();
                    continue;
                }
                foreach (var t in g)
                {
                    try
                    {
                        if (await IntegFindGrant(me.id, t.slug, t.financeId) is null) throw new Exception("No access to this head office.");
                        int ins = await IntegImportToBranch(tc, t.slug, t.financeId, t.branchId, headers, mapped, rows, fileName, me.email, app.Environment.ContentRootPath);
                        results.Add(new { agencySlug = t.slug, financeId = t.financeId, branchId = t.branchId, ok = true, records = ins });
                        totalInserted += ins; okCount++;
                    }
                    catch (Exception ex) { results.Add(new { agencySlug = t.slug, financeId = t.financeId, branchId = t.branchId, ok = false, error = ex.Message }); failCount++; }
                }
                await tc.DisposeAsync();
            }
            return Results.Ok(new { ok = failCount == 0, totalRecords = totalInserted, branches = okCount, failed = failCount, results });
        });
    }

    private static async Task<int> IntegImportToBranch(
        MySqlConnection tc, string slug, int financeId, int branchId,
        System.Collections.Generic.List<string> headers, System.Collections.Generic.List<(int idx, string col)> mapped,
        System.Collections.Generic.List<System.Collections.Generic.List<string>> rows,
        string fileName, string uploadedBy, string contentRoot)
    {
        await using (var bc = new MySqlCommand("SELECT COUNT(*) FROM branches WHERE id=@bid AND finance_id=@fin", tc))
        {
            bc.Parameters.AddWithValue("@bid", branchId); bc.Parameters.AddWithValue("@fin", financeId);
            if (Convert.ToInt32(await bc.ExecuteScalarAsync()) == 0) throw new Exception("Invalid branch for this head office.");
        }
        int uploadId;
        await using (var uc = new MySqlCommand(@"
            INSERT INTO integration_uploads (finance_id, branch_id, uploaded_by, file_name, total_records)
            VALUES (@fin,@bid,@by,@fn,0);
            SELECT LAST_INSERT_ID();", tc))
        {
            uc.Parameters.AddWithValue("@fin", financeId);
            uc.Parameters.AddWithValue("@bid", branchId);
            uc.Parameters.AddWithValue("@by", uploadedBy);
            uc.Parameters.AddWithValue("@fn", IntegCap(string.IsNullOrWhiteSpace(fileName) ? "upload.xlsx" : fileName, 500));
            uploadId = Convert.ToInt32(await uc.ExecuteScalarAsync());
        }
        var safeSlug = Regex.Replace(slug, "[^a-z0-9_-]", "");
        var slotDir = Path.Combine(contentRoot, "integration-uploads", safeSlug);
        Directory.CreateDirectory(slotDir);
        var baseName = string.IsNullOrWhiteSpace(fileName) ? "upload" : Regex.Replace(Path.GetFileNameWithoutExtension(fileName), "[^A-Za-z0-9_-]", "_");
        var csvName = uploadId + "-" + baseName + ".csv";
        var relPath = "integration-uploads/" + safeSlug + "/" + csvName;
        static string Csv(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
        try
        {
            await using var sw = new StreamWriter(Path.Combine(slotDir, csvName), false, System.Text.Encoding.UTF8);
            await sw.WriteLineAsync(string.Join(",", headers.Select(Csv)));
            foreach (var row in rows)
            {
                var cells = new System.Collections.Generic.List<string>(headers.Count);
                for (int i = 0; i < headers.Count; i++) cells.Add(Csv(i < row.Count ? row[i] : ""));
                await sw.WriteLineAsync(string.Join(",", cells));
            }
        }
        catch { }
        string colSql = "branch_id, upload_id, " + string.Join(", ", mapped.Select(m => "`" + m.col + "`"));
        int inserted = 0;
        const int batch = 200;
        for (int start = 0; start < rows.Count; start += batch)
        {
            int end = Math.Min(start + batch, rows.Count);
            var sb = new System.Text.StringBuilder();
            sb.Append("INSERT INTO vehicle_records (").Append(colSql).Append(") VALUES ");
            var ps = new System.Collections.Generic.List<(string, object)>();
            for (int rI = start; rI < end; rI++)
            {
                if (rI > start) sb.Append(',');
                sb.Append("(@b").Append(rI).Append(",@u").Append(rI);
                ps.Add(("@b" + rI, branchId));
                ps.Add(("@u" + rI, uploadId));
                var row = rows[rI];
                for (int m = 0; m < mapped.Count; m++)
                {
                    var pn = "@p" + rI + "_" + m;
                    sb.Append(',').Append(pn);
                    var idx = mapped[m].idx;
                    ps.Add((pn, (object)(idx < row.Count ? IntegCap(row[idx], 250) : "")));
                }
                sb.Append(')');
            }
            await using var ins = new MySqlCommand(sb.ToString(), tc) { CommandTimeout = 120 };
            foreach (var (k, v) in ps) ins.Parameters.AddWithValue(k, v);
            inserted += await ins.ExecuteNonQueryAsync();
        }
        await using (var uu = new MySqlCommand("UPDATE integration_uploads SET file_path=@fp, total_records=@tr WHERE id=@id", tc))
        {
            uu.Parameters.AddWithValue("@fp", relPath);
            uu.Parameters.AddWithValue("@tr", inserted);
            uu.Parameters.AddWithValue("@id", uploadId);
            await uu.ExecuteNonQueryAsync();
        }
        await using (var rcx = new MySqlCommand(@"
            DELETE ri FROM rc_info ri INNER JOIN vehicle_records vr ON vr.id=ri.vehicle_record_id WHERE vr.branch_id=@bid;
            INSERT INTO rc_info (vehicle_record_id,rc_number,model,last4)
              SELECT id, vehicle_no, COALESCE(model,''),
                     LEFT(REGEXP_SUBSTR(vehicle_no,'[0-9]{4}[^0-9]*$'),4)
              FROM vehicle_records WHERE branch_id=@bid AND vehicle_no IS NOT NULL AND vehicle_no!='';
            DELETE ci FROM chassis_info ci INNER JOIN vehicle_records vr ON vr.id=ci.vehicle_record_id WHERE vr.branch_id=@bid;
            INSERT INTO chassis_info (vehicle_record_id,chassis_number,model,last5)
              SELECT id, chassis_no, COALESCE(model,''), RIGHT(chassis_no,5)
              FROM vehicle_records WHERE branch_id=@bid AND chassis_no IS NOT NULL AND chassis_no!='';", tc) { CommandTimeout = 300 })
        {
            rcx.Parameters.AddWithValue("@bid", branchId);
            await rcx.ExecuteNonQueryAsync();
        }
        await using (var st = new MySqlCommand("UPDATE branches SET total_records=(SELECT COUNT(*) FROM vehicle_records WHERE branch_id=@bid), uploaded_at=NOW() WHERE id=@bid", tc) { CommandTimeout = 60 })
        { st.Parameters.AddWithValue("@bid", branchId); await st.ExecuteNonQueryAsync(); }
        return inserted;
    }

    private static System.Collections.Generic.List<System.Collections.Generic.List<string>> ParseCsv(string text, int maxLines)
    {
        var result = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
        var field = new System.Text.StringBuilder();
        var row = new System.Collections.Generic.List<string>();
        bool inQuotes = false;
        int n = text.Length;
        for (int i = 0; i < n; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < n && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (ch == '\r') { }
                else if (ch == '\n')
                {
                    row.Add(field.ToString()); field.Clear();
                    result.Add(row); row = new System.Collections.Generic.List<string>();
                    if (result.Count >= maxLines) return result;
                }
                else field.Append(ch);
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); result.Add(row); }
        return result;
    }

    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

    private static async Task<System.Collections.Generic.Dictionary<string, string>>
        ReadJsonAsync(HttpRequest req)
    {
        try
        {
            using var sr = new StreamReader(req.Body);
            var json = await sr.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(json)) return new();
            var doc = System.Text.Json.JsonDocument.Parse(json).RootElement;
            var d = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc.ValueKind == System.Text.Json.JsonValueKind.Object)
                foreach (var p in doc.EnumerateObject())
                    d[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? (p.Value.GetString() ?? "")
                        : p.Value.ToString();
            return d;
        }
        catch { return new(); }
    }

    private static async Task<(int RetrySeconds, bool HourlyCapHit)> OtpThrottle(
        MySqlConnection conn, string email, string purpose)
    {
        const int WindowSeconds = 60;
        const int MaxPerHour    = 5;
        const int TtlMinutes    = 10;

        DateTime? newest = null;
        await using (var cmd = new MySqlCommand(
            "SELECT expires_at FROM agency_otps WHERE email=@e AND purpose=@p ORDER BY id DESC LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("@e", email);
            cmd.Parameters.AddWithValue("@p", purpose);
            var v = await cmd.ExecuteScalarAsync();
            if (v is DateTime d) newest = d;
        }

        if (newest is DateTime exp)
        {
            var issuedUtc = exp.AddMinutes(-TtlMinutes);
            var age = (DateTime.UtcNow - issuedUtc).TotalSeconds;
            if (age >= 0 && age < WindowSeconds)
                return ((int)Math.Ceiling(WindowSeconds - age), false);
        }

        await using (var cnt = new MySqlCommand(
            "SELECT COUNT(*) FROM agency_otps WHERE email=@e AND purpose=@p " +
            "AND expires_at > DATE_SUB(UTC_TIMESTAMP(), INTERVAL @back MINUTE)", conn))
        {
            cnt.Parameters.AddWithValue("@e", email);
            cnt.Parameters.AddWithValue("@p", purpose);
            cnt.Parameters.AddWithValue("@back", 60 - TtlMinutes);
            if (Convert.ToInt32(await cnt.ExecuteScalarAsync()) >= MaxPerHour)
                return (0, true);
        }

        return (0, false);
    }

    private const int AuthChallengeSeconds = 120;

    private static string NewChallengeId()
    {
        var b = new byte[16];
        RandomNumberGenerator.Fill(b);
        return Convert.ToHexString(b).ToLowerInvariant();
    }

    private static string NewNonce()
    {
        var b = new byte[32];
        RandomNumberGenerator.Fill(b);
        return Convert.ToHexString(b).ToLowerInvariant();
    }

    private static string NewPairCode() => RandomNumberGenerator.GetInt32(10, 100).ToString();

    private static string QrDataUri(string text)
    {
        using var gen = new QRCoder.QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    private static readonly TimeSpan IstOffset = TimeSpan.FromMinutes(330);

    private static async Task RecordDesktopLoginAsync(
        MySqlConnection conn, long userId, string method, string deviceLabel)
    {
        var now = IstNow();
        var day = now.Date;

        try
        {
            await using var ins = new MySqlCommand(
                "INSERT INTO desktop_logins (user_id, at, work_date, method, device_label) " +
                "VALUES (@u, @t, @d, @m, @l);", conn);
            ins.Parameters.AddWithValue("@u", userId);
            ins.Parameters.AddWithValue("@t", now);
            ins.Parameters.AddWithValue("@d", day);
            ins.Parameters.AddWithValue("@m", method);
            ins.Parameters.AddWithValue("@l", deviceLabel ?? "");
            await ins.ExecuteNonQueryAsync();
        }
        catch { /* a missing table must never block a sign-in */ }

        try
        {
            await using var att = new MySqlCommand(
                "INSERT INTO attendance (user_id, work_date, marked_at, status, source, marked_by) " +
                "VALUES (@u, @d, @t, 'present', 'login', 'Signed in') " +
                "ON DUPLICATE KEY UPDATE id = id;", conn);
            att.Parameters.AddWithValue("@u", userId);
            att.Parameters.AddWithValue("@d", day);
            att.Parameters.AddWithValue("@t", now);
            await att.ExecuteNonQueryAsync();
        }
        catch { }
    }

    private static DateTime? ParseIstMonth(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        return DateTime.TryParse(v + "-01", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d.Date : null;
    }

    private static (string slug, long userId)? MeFromToken(HttpContext ctx)
    {
        string t = ctx.Request.Headers["X-Profile-Token"].FirstOrDefault() ?? "";
        return ProfileToken.Verify(t);
    }

    private static async Task EnsureEmploymentRow(MySqlConnection conn, long userId)
    {
        try
        {
            await using var cmd = new MySqlCommand(
                "INSERT INTO hrms_employment (user_id) VALUES (@u) ON DUPLICATE KEY UPDATE user_id = user_id;", conn);
            cmd.Parameters.AddWithValue("@u", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    private static DateTime IstNow() => DateTime.UtcNow + IstOffset;

    private static DateTime IstToday() => IstNow().Date;

    private static DateTime? ParseIstDate(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        return DateTime.TryParseExact(v.Trim(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d.Date : null;
    }

    private static string PfpUrl(string? p)
    {
        if (string.IsNullOrEmpty(p)) return "";
        if (p.StartsWith("http") || p.StartsWith("data:")) return p;
        if (p.Length < 256 && p.Contains('/') && !p.Contains('+') && !p.Contains('='))
            return "https://api.crmrecoverysoftware.com/uploads/" + p.TrimStart('/');
        return "data:image/jpeg;base64," + p;
    }

    private static string MaskEmail(string email)
    {
        int at = email.IndexOf('@');
        if (at <= 0) return "***";
        string user = email.Substring(0, at), dom = email.Substring(at);
        if (user.Length <= 2) return user.Substring(0, 1) + "***" + dom;
        return user.Substring(0, 2) + new string('*', Math.Min(6, user.Length - 2)) + dom;
    }

    private static async Task<string?> HrmsSessionSlug(string masterConn, HttpContext ctx)
    {
        string token = ctx.Request.Headers["X-Hrms-Token"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(token)) return null;

        await using var conn = new MySqlConnection(masterConn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            SELECT a.slug FROM hrms_sessions s
              JOIN agencies a ON a.id = s.agency_id
             WHERE s.token_hash = @t AND s.revoked = 0 AND s.expires_at > UTC_TIMESTAMP()
               AND a.status = 'approved' AND COALESCE(a.hrms_enabled,0) = 1
             LIMIT 1;", conn);
        cmd.Parameters.AddWithValue("@t", Sha256Hex(token));
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    private static async Task<int?> HrmsSessionAgencyId(string masterConn, HttpContext ctx)
    {
        string token = ctx.Request.Headers["X-Hrms-Token"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(token)) return null;

        await using var conn = new MySqlConnection(masterConn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT s.agency_id FROM hrms_sessions s JOIN agencies a ON a.id = s.agency_id " +
            "WHERE s.token_hash=@t AND s.revoked=0 AND s.expires_at > UTC_TIMESTAMP() " +
            "AND a.status='approved' AND COALESCE(a.hrms_enabled,0)=1 LIMIT 1;", conn);
        cmd.Parameters.AddWithValue("@t", Sha256Hex(token));
        var got = await cmd.ExecuteScalarAsync();
        return got is null ? null : Convert.ToInt32(got);
    }

    private static async Task<object?> HrmsSessionAgency(string masterConn, HttpContext ctx)
    {
        string token = ctx.Request.Headers["X-Hrms-Token"].FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(token)) return null;

        await using var conn = new MySqlConnection(masterConn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            SELECT a.id, a.name, a.slug, COALESCE(a.logo_path,''), a.email1,
                   COALESCE(a.email2,''), a.mobile1, COALESCE(a.mobile2,''),
                   COALESCE(a.address,''), a.status, a.created_at, a.approved_at,
                   a.hrms_enabled_at, s.expires_at, COALESCE(a.qr_proximity,'warn'),
                   a.geo_lat, a.geo_lng, COALESCE(a.geo_radius_m,200), COALESCE(a.geo_label,'')
              FROM hrms_sessions s
              JOIN agencies a ON a.id = s.agency_id
             WHERE s.token_hash = @t AND s.revoked = 0 AND s.expires_at > UTC_TIMESTAMP()
               AND a.status = 'approved' AND COALESCE(a.hrms_enabled,0) = 1
             LIMIT 1;", conn);
        cmd.Parameters.AddWithValue("@t", Sha256Hex(token));
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;

        string? D(int i, string fmt = "yyyy-MM-dd HH:mm") =>
            rdr.IsDBNull(i) ? null : rdr.GetDateTime(i).ToString(fmt);

        return new
        {
            agencyId   = rdr.GetInt32(0),
            agencyName = rdr.GetString(1),
            slug       = rdr.GetString(2),
            logoPath   = rdr.GetString(3),
            email      = MaskEmail(rdr.GetString(4)),
            email2     = rdr.GetString(5).Length == 0 ? "" : MaskEmail(rdr.GetString(5)),
            mobile1    = rdr.GetString(6),
            mobile2    = rdr.GetString(7),
            address    = rdr.GetString(8),
            status     = rdr.GetString(9),
            registeredAt = D(10),
            approvedAt   = D(11),
            hrmsSince    = D(12),
            sessionExpiresAt = rdr.GetDateTime(13).ToString("yyyy-MM-ddTHH:mm:ss") + "Z",
            sessionHours     = 12,
            qrProximity      = rdr.GetString(14),
            geoLat           = rdr.IsDBNull(15) ? (double?)null : rdr.GetDouble(15),
            geoLng           = rdr.IsDBNull(16) ? (double?)null : rdr.GetDouble(16),
            geoRadiusM       = rdr.GetInt32(17),
            geoLabel         = rdr.GetString(18),
            mapsKey          = Env("GOOGLE_MAPS_KEY", ""),
        };
    }

    private static bool IsValidEmail(string e) =>
        !string.IsNullOrEmpty(e) && Regex.IsMatch(e, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string GenerateOtp()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        int v = BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
        return (v % 1000000).ToString("D6");
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes);
    }

    private static string HashPassword(string password)
    {
        const int iter = 100_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        using var kdf = new Rfc2898DeriveBytes(password, salt, iter, HashAlgorithmName.SHA256);
        var hash = kdf.GetBytes(32);
        return $"pbkdf2${iter}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static string NewDeviceToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    internal static string ClientIp(HttpContext ctx)
    {
        var direct = ctx.Connection.RemoteIpAddress;
        bool viaLocalProxy = direct == null || System.Net.IPAddress.IsLoopback(direct);

        if (viaLocalProxy)
        {
            string fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(fwd))
            {
                var first = fwd.Split(',')[0].Trim();
                if (first.Length > 0) return Normalise(first);
            }
            string real = ctx.Request.Headers["X-Real-IP"].ToString().Trim();
            if (real.Length > 0) return Normalise(real);
        }

        return direct == null ? "" : Normalise(direct.ToString());
    }

    private static string Normalise(string ip)
    {
        ip = ip.Trim();
        if (ip.StartsWith("[")) { int e = ip.IndexOf(']'); if (e > 0) ip = ip.Substring(1, e - 1); }
        else if (ip.Count(c => c == ':') == 1) ip = ip.Split(':')[0];   // host:port, IPv4 only
        if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase)) ip = ip.Substring(7);
        return ip.Length > 45 ? ip.Substring(0, 45) : ip;
    }

    internal static string Sha256Hex(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""))).ToLowerInvariant();

    public static bool VerifyPassword(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
            int iter = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var hash = Convert.FromBase64String(parts[3]);
            using var kdf = new Rfc2898DeriveBytes(password, salt, iter, HashAlgorithmName.SHA256);
            var check = kdf.GetBytes(hash.Length);
            return CryptographicOperations.FixedTimeEquals(hash, check);
        }
        catch { return false; }
    }

    public static string DeriveTenantPassword(string slug)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TenantDbSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes("tenant:" + slug));
        return "T1!" + Convert.ToBase64String(bytes).Replace('+','-').Replace('/','_').Substring(0, 25);
    }

    private static async Task<bool> WasRecentlyVerified(MySqlConnection conn, string email)
    {
        await using var cmd = new MySqlCommand(@"
            SELECT MAX(expires_at) FROM agency_otps
             WHERE email = @e AND purpose = 'register' AND consumed = 1
               AND expires_at > UTC_TIMESTAMP() - INTERVAL 30 MINUTE;", conn);
        cmd.Parameters.AddWithValue("@e", email);
        var v = await cmd.ExecuteScalarAsync();
        return v != null && v != DBNull.Value;
    }

    private static async Task<bool> IsManageTokenValid(string masterConn, HttpContext ctx)
    {
        string? token = ctx.Request.Headers["X-Manage-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(token) || token.Length != 64) return false;
        await using var conn = new MySqlConnection(masterConn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT 1 FROM manage_sessions WHERE token=@t AND expires_at > UTC_TIMESTAMP() LIMIT 1;", conn);
        cmd.Parameters.AddWithValue("@t", token);
        var r = await cmd.ExecuteScalarAsync();
        return r != null;
    }

    private static async Task<string> GenerateUniqueSlug(MySqlConnection conn, string name)
    {
        string baseSlug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        if (baseSlug.Length < 2) baseSlug = "agency";
        if (baseSlug.Length > 40) baseSlug = baseSlug.Substring(0, 40);
        string slug = baseSlug;
        int suffix = 1;
        while (true)
        {
            await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM agencies WHERE slug = @s", conn);
            cmd.Parameters.AddWithValue("@s", slug);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0) return slug;
            suffix++;
            slug = baseSlug + "_" + suffix;
            if (suffix > 9999) throw new Exception("Could not derive a unique slug.");
        }
    }

    private static async Task ProvisionTenant(string provConn, string mysqlHost, int mysqlPort,
                                              string dbName, string dbUser, string dbPass)
    {
        if (!Regex.IsMatch(dbName, "^[a-z0-9_]+$") || !Regex.IsMatch(dbUser, "^[a-z0-9_]+$"))
            throw new Exception("Internal: invalid identifier in provisioning.");

        await using (var conn = new MySqlConnection(provConn))
        {
            await conn.OpenAsync();

            await Exec(conn, $"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;");
            await Exec(conn, $"CREATE USER IF NOT EXISTS `{dbUser}`@`localhost` IDENTIFIED BY @pwd;", ("@pwd", dbPass));
            await Exec(conn, $"GRANT ALL PRIVILEGES ON `{dbName}`.* TO `{dbUser}`@`localhost`;");
            await Exec(conn, "FLUSH PRIVILEGES;");
        }

        string tenantConn =
            $"server={mysqlHost};port={mysqlPort};database={dbName};" +
            $"uid={dbUser};pwd={dbPass};" +
             "Pooling=false;AllowUserVariables=true;DefaultCommandTimeout=120;";

        string  ddl      = await File.ReadAllTextAsync(ResolveSchemaFile("tenant_template.sql", required: true)!);
        string? seedPath = ResolveSchemaFile("tenant_seed.sql", required: false);
        string? seed     = seedPath is null ? null : await File.ReadAllTextAsync(seedPath);

        await using (var conn = new MySqlConnection(tenantConn))
        {
            await conn.OpenAsync();
            await RunSqlScript(conn, ddl);
            if (!string.IsNullOrWhiteSpace(seed))
                await RunSqlScript(conn, seed);
        }
    }

    private static string? ResolveSchemaFile(string name, bool required)
    {
        string p = Path.Combine(AppContext.BaseDirectory, "dbschema", name);
        if (!File.Exists(p))
            p = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "dbschema", name));
        if (File.Exists(p)) return p;
        if (required) throw new Exception($"{name} not found alongside the API binary.");
        return null;
    }

    private static async Task RunSqlScript(MySqlConnection conn, string sql)
    {
        string delimiter = ";";
        var buf = new StringBuilder();

        foreach (var raw in sql.Split('\n'))
        {
            var line    = raw.TrimEnd('\r');
            var trimmed = line.Trim();

            if (buf.Length == 0 && (trimmed.Length == 0 || trimmed.StartsWith("--")))
                continue;

            if (trimmed.StartsWith("DELIMITER ", StringComparison.OrdinalIgnoreCase))
            {
                delimiter = trimmed.Substring("DELIMITER ".Length).Trim();
                continue;
            }

            buf.Append(line).Append('\n');
            if (!trimmed.EndsWith(delimiter, StringComparison.Ordinal))
                continue;

            var stmt = buf.ToString().Trim();
            buf.Clear();
            stmt = stmt.Substring(0, stmt.Length - delimiter.Length).Trim();
            await ExecScriptStatement(conn, stmt);
        }
        await ExecScriptStatement(conn, buf.ToString().Trim());
    }

    private static async Task ExecScriptStatement(MySqlConnection conn, string stmt)
    {
        if (stmt.Length == 0 || stmt == ";") return;
        stmt = Regex.Replace(stmt, @"DEFINER\s*=\s*`[^`]*`@`[^`]*`\s*", "");
        await using var cmd = new MySqlCommand(stmt, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task Exec(MySqlConnection conn, string sql, params (string k, object v)[] paramz)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        foreach (var (k, v) in paramz) cmd.Parameters.AddWithValue(k, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class SmtpConfig
    {
        public string Host = ""; public int Port;
        public string User = ""; public string Pass = "";
        public bool Ssl = true;
        public string FromAddr = ""; public string FromName = "";
    }

    private static async Task SendOtpEmail(SmtpConfig s, string to, string code)
    {
        string subject = $"Your CRMRS verification code: {code}";
        string html = $@"
<div style=""font-family:'Hanken Grotesk',Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;padding:32px;color:#100f0c;background:#fbfaf7;"">
  <div style=""background:#ffffff;border-radius:16px;border:1px solid #ece9e2;padding:36px;"">
    <div style=""text-align:center;margin-bottom:26px;"">
      <img src=""https://crmrecoverysoftware.com/assets/crmrs-banner.png"" alt=""CRMRS"" width=""170"" style=""display:block;margin:0 auto;width:170px;max-width:62%;height:auto;border:0;outline:none;text-decoration:none;"" />
      <div style=""font-size:9px;letter-spacing:3px;color:#ff5500;font-weight:700;margin-top:7px;"">RECOVERY SOFTWARE</div>
    </div>
    <h2 style=""margin:0 0 8px;font-family:'Archivo',Segoe UI,Arial,sans-serif;font-size:20px;font-weight:800;color:#100f0c;text-align:center;"">Verify your email</h2>
    <p style=""margin:0 0 24px;color:#5a574f;font-size:14px;text-align:center;"">Use the code below to verify your email address. It is valid for 10 minutes.</p>
    <div style=""font-family:'Archivo',Segoe UI,Arial,sans-serif;font-size:36px;font-weight:800;letter-spacing:.18em;text-align:center;padding:18px;background:#fff1ea;color:#cc3c00;border-radius:12px;border:1px solid #ffd9c2;"">{code}</div>
    <p style=""margin:24px 0 0;color:#9a978f;font-size:12.5px;text-align:center;"">If you did not request this code, you can safely ignore this email.</p>
  </div>
  <p style=""text-align:center;color:#9a978f;font-size:11px;margin-top:16px;"">© CRMRS · team@crmrecoverysoftware.com</p>
</div>";
        await SendMail(s, to, subject, html);
    }

    private static async Task SendApprovedEmail(SmtpConfig s, string to, string agencyName)
    {
        string subject = "Your CRMRS agency has been approved";
        string html = $@"
<div style=""font-family:'Hanken Grotesk',Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;padding:32px;color:#100f0c;background:#fbfaf7;"">
  <div style=""background:#ffffff;border-radius:16px;border:1px solid #ece9e2;padding:36px;"">
    <div style=""text-align:center;margin-bottom:26px;"">
      <img src=""https://crmrecoverysoftware.com/assets/crmrs-banner.png"" alt=""CRMRS"" width=""170"" style=""display:block;margin:0 auto;width:170px;max-width:62%;height:auto;border:0;outline:none;text-decoration:none;"" />
      <div style=""font-size:9px;letter-spacing:3px;color:#ff5500;font-weight:700;margin-top:7px;"">RECOVERY SOFTWARE</div>
    </div>
    <h2 style=""margin:0 0 10px;font-family:'Archivo',Segoe UI,Arial,sans-serif;font-size:22px;font-weight:800;color:#ff5500;text-align:center;"">You're approved 🎉</h2>
    <p style=""margin:0 0 18px;color:#100f0c;font-size:15px;text-align:center;""><strong>{System.Net.WebUtility.HtmlEncode(agencyName)}</strong>, your CRMRS agency account is now active.</p>
    <p style=""margin:0 0 18px;color:#5a574f;font-size:14px;"">You can sign in to the desktop application using your primary email and the password you set during registration.</p>
    <p style=""margin:0;color:#5a574f;font-size:13px;"">Your agency has its own private workspace — your data is fully isolated from every other agency.</p>
  </div>
  <p style=""text-align:center;color:#9a978f;font-size:11px;margin-top:16px;"">© CRMRS · team@crmrecoverysoftware.com</p>
</div>";
        await SendMail(s, to, subject, html);
    }

    private static async Task SendManageOtpEmail(SmtpConfig s, string to, string code)
    {
        string subject = $"CRMRS admin sign-in code: {code}";
        string html = $@"
<div style=""font-family:'Hanken Grotesk',Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;padding:32px;color:#100f0c;background:#fbfaf7;"">
  <div style=""background:#ffffff;border-radius:16px;border:1px solid #ece9e2;padding:36px;"">
    <div style=""text-align:center;margin-bottom:26px;"">
      <img src=""https://crmrecoverysoftware.com/assets/crmrs-banner.png"" alt=""CRMRS"" width=""170"" style=""display:block;margin:0 auto;width:170px;max-width:62%;height:auto;border:0;outline:none;text-decoration:none;"" />
      <div style=""font-size:9px;letter-spacing:3px;color:#ff5500;font-weight:700;margin-top:7px;"">RECOVERY SOFTWARE</div>
    </div>
    <h2 style=""margin:0 0 8px;font-family:'Archivo',Segoe UI,Arial,sans-serif;font-size:20px;font-weight:800;color:#100f0c;text-align:center;"">Administrator sign-in</h2>
    <p style=""margin:0 0 24px;color:#5a574f;font-size:14px;text-align:center;"">Use the code below to finish signing in to the manage page. It is valid for 10 minutes.</p>
    <div style=""font-family:'Archivo',Segoe UI,Arial,sans-serif;font-size:36px;font-weight:800;letter-spacing:.18em;text-align:center;padding:18px;background:#fff1ea;color:#cc3c00;border-radius:12px;border:1px solid #ffd9c2;"">{code}</div>
    <p style=""margin:24px 0 0;color:#9a978f;font-size:12.5px;text-align:center;"">If you did not request this code, someone may have tried to access the admin page — you can safely ignore the email.</p>
  </div>
  <p style=""text-align:center;color:#9a978f;font-size:11px;margin-top:16px;"">© CRMRS · admin sign-in</p>
</div>";
        await SendMail(s, to, subject, html);
    }

    private static async Task SendMail(SmtpConfig s, string to, string subject, string html)
    {
        using var msg = new MailMessage();
        msg.From = new MailAddress(s.FromAddr, s.FromName, Encoding.UTF8);
        msg.To.Add(new MailAddress(to));
        msg.Subject = subject;
        msg.SubjectEncoding = Encoding.UTF8;
        msg.Body = html;
        msg.BodyEncoding = Encoding.UTF8;
        msg.IsBodyHtml = true;

        using var client = new SmtpClient(s.Host, s.Port)
        {
            EnableSsl = s.Ssl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (!string.IsNullOrEmpty(s.User))
            client.Credentials = new NetworkCredential(s.User, s.Pass);
        await client.SendMailAsync(msg);
    }
}
