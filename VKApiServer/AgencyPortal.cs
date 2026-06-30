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
