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
    private const string MANAGE_PASSWORD = "crmrs@kc.12";

    private const string LOGO_DIR = "/opt/vkapi/agency-uploads";

    private static readonly string TenantDbSecret =
        Environment.GetEnvironmentVariable("TENANT_DB_SECRET")
        ?? "crmrs-tenant-secret-rotate-me-2026";

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
            $"uid={Env("MASTER_DB_USER",     "crm_master_app")};" +
            $"pwd={Env("MASTER_DB_PASSWORD", "SET_VIA_ENV")};" +
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
                       COALESCE(logo_path,''), status
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
            string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "";

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
            await using (var cmd = new MySqlCommand(@"
                SELECT id, name, slug, status, password_hash, logo_path, mobile1, address
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
            return Results.Ok(new
            {
                token,
                agencyId   = id,
                agencyName = name,
                slug,
                email,
                mobile1    = mobile1 ?? "",
                address    = address ?? "",
                logoPath   = logoPath ?? "",
                isAgency   = true,
            });
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
      <span style=""font-family:'Archivo',Segoe UI,Arial,sans-serif;font-weight:900;font-size:24px;letter-spacing:-0.5px;color:#100f0c;"">CRM<span style=""color:#ff5500;"">RS</span></span>
      <div style=""font-size:9px;letter-spacing:3px;color:#ff5500;font-weight:700;margin-top:3px;"">RECOVERY SOFTWARE</div>
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
      <span style=""font-family:'Archivo',Segoe UI,Arial,sans-serif;font-weight:900;font-size:24px;letter-spacing:-0.5px;color:#100f0c;"">CRM<span style=""color:#ff5500;"">RS</span></span>
      <div style=""font-size:9px;letter-spacing:3px;color:#ff5500;font-weight:700;margin-top:3px;"">RECOVERY SOFTWARE</div>
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
      <span style=""font-family:'Archivo',Segoe UI,Arial,sans-serif;font-weight:900;font-size:24px;letter-spacing:-0.5px;color:#100f0c;"">CRM<span style=""color:#ff5500;"">RS</span></span>
      <div style=""font-size:9px;letter-spacing:3px;color:#ff5500;font-weight:700;margin-top:3px;"">RECOVERY SOFTWARE</div>
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
