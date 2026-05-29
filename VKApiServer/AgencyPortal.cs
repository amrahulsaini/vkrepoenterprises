// ─────────────────────────────────────────────────────────────────────
//  CRMS Agency Portal endpoints — registration + admin manage
//
//  Routes under  /api/agency/*  served by VKApiServer (port 5002), proxied
//  by OpenLiteSpeed for  https://agency.crmrecoverysoftware.com/api/agency/*
//
//  Wire-up:  in Program.cs, after the rest of the endpoints, call:
//      AgencyPortal.Map(app, connStr);
// ─────────────────────────────────────────────────────────────────────
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
    // Hardcoded for now — the /manage password gate
    private const string MANAGE_PASSWORD = "crmrs@kc.12";

    // Where new tenant logos land on disk. Served by OLS via the
    // /agency-uploads/ static context on the agency vhost.
    private const string LOGO_DIR = "/opt/vkapi/agency-uploads";

    // Server secret used to derive each tenant's DB password deterministically.
    // Override in env (TENANT_DB_SECRET) for production.
    private static readonly string TenantDbSecret =
        Environment.GetEnvironmentVariable("TENANT_DB_SECRET")
        ?? "crmrs-tenant-secret-rotate-me-2026";

    /// <summary>
    /// Connection string to crm_master, available to any endpoint after Map()
    /// has run (i.e. after app startup). Used by code outside this file that
    /// needs to read/write the cross-agency app_user_registry without
    /// re-deriving the connection string from env.
    /// </summary>
    public static string MasterConn { get; private set; } = "";

    // India Standard Time zone — resolved once. The server runs on UTC, so we
    // convert build timestamps to IST for the manage portal's "built ..."
    // labels (admins are in India and found the UTC times confusing).
    private static readonly TimeZoneInfo IstZone = ResolveIst();
    private static TimeZoneInfo ResolveIst()
    {
        foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "IST", "IST");
    }

    // "yyyy-MM-dd HH:mm IST" for a file's last-write time, or "" if missing.
    private static string BuiltAtIst(string path)
    {
        if (!File.Exists(path)) return "";
        var ist = TimeZoneInfo.ConvertTimeFromUtc(File.GetLastWriteTimeUtc(path), IstZone);
        return ist.ToString("yyyy-MM-dd HH:mm 'IST'");
    }

    // Reads ticket header rows into mutable dictionaries (so callers can attach
    // a "messages" thread). whereOrder is the WHERE/ORDER tail of the query.
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

    // Loads the message thread for a ticket (sender + body + time + id).
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

    // Lazily ensures the central client-error log table (crm_master). The desktop
    // app POSTs every failure here so all "unsuccessful things" are captured
    // centrally; the manage portal reads them. CREATE IF NOT EXISTS keeps it
    // migration-free.
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

    // Reads { body } (or { message }) from a JSON request body → trimmed string.
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

    // Verifies the desktop's agt1 agency Bearer token and returns (id, slug).
    // Used by the desktop self-profile endpoints, which live under
    // /api/agency/* and are therefore skipped by the tenant-routing
    // middleware — so we read and verify the token here ourselves.
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
        // ── Connection strings to crm_master ─────────────────────────
        // crm_master_app   →  CRUD on crm_master only
        // crm_provisioner  →  full privileges, used ONLY at approve time
        // Secrets (passwords, SMTP key, tenant secret) come from the systemd
        // service environment on the server — NOT committed here. Usernames /
        // hostnames / ports are fine to keep as defaults.
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

        // ── SMTP (Brevo) — sends the OTP emails ──────────────────────
        var smtp = new SmtpConfig {
            Host     = Env("SMTP_HOST",      "smtp-relay.brevo.com"),
            Port     = int.Parse(Env("SMTP_PORT", "587")),
            User     = Env("SMTP_USER",      "9a47c5001@smtp-brevo.com"),
            Pass     = Env("SMTP_PASS",      "SET_VIA_ENV"),
            FromAddr = Env("SMTP_FROM",      "team@crmrecoverysoftware.com"),
            FromName = Env("SMTP_FROM_NAME", "CRMS TEAM"),
        };

        // Best-effort — deploy.sh creates this with the right ownership.
        try { Directory.CreateDirectory(LOGO_DIR); } catch { }

        // =============================================================
        //   OTP — send & verify
        // =============================================================
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
            // Mark the most recent matching OTP consumed
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

        // =============================================================
        //   Final registration (multipart: text fields + logo)
        // =============================================================
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

            // Both emails must have a recently-consumed verification OTP
            if (!await WasRecentlyVerified(conn, email1))
                return Results.BadRequest(new { message = "Primary email is not verified — verify the OTP first." });
            if (!string.IsNullOrEmpty(email2) && !await WasRecentlyVerified(conn, email2))
                return Results.BadRequest(new { message = "Secondary email is not verified." });

            // Email1 must be unique
            await using (var dup = new MySqlCommand("SELECT COUNT(*) FROM agencies WHERE email1 = @e", conn))
            {
                dup.Parameters.AddWithValue("@e", email1);
                var c = Convert.ToInt32(await dup.ExecuteScalarAsync());
                if (c > 0)
                    return Results.BadRequest(new { message = "An agency with this primary email already exists." });
            }

            string slug = await GenerateUniqueSlug(conn, name);

            // Save the logo (already client-side compressed to ~JPEG)
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

            // Insert pending agency
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

        // =============================================================
        //   /manage  password gate
        // =============================================================
        // Where the admin OTP for the manage page is sent. Override in env.
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

        // Step 1 of the 2-step admin gate: verify the manage password, then
        // email a 6-digit OTP to the administrator address.
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

        // Step 2: verify the OTP. On success, issue the same manage token the
        // /manage/login endpoint hands out.
        app.MapPost("/api/agency/manage/otp/verify", async (HttpRequest req) =>
        {
            var dto = await ReadJsonAsync(req);
            string code = (dto.GetValueOrDefault("code") ?? "").Trim();
            if (code.Length != 6)
                return Results.BadRequest(new { message = "Enter the 6-digit code." });

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            // Consume the most recent matching, unconsumed, unexpired manage OTP.
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

        // =============================================================
        //   /manage  list / approve / reject
        // =============================================================
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
                  FROM agencies " + where + " ORDER BY created_at DESC LIMIT 500;", conn);
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
            string dbUser = "tu_"   + slug;     // tenant-user (≤16 chars usually fine)
            if (dbUser.Length > 32) dbUser = dbUser.Substring(0, 32);
            string dbPass = DeriveTenantPassword(slug);

            // Provision the tenant DB + user, then apply the schema template
            try
            {
                await ProvisionTenant(provConn, mysqlHost, mysqlPort, dbName, dbUser, dbPass);
            }
            catch (Exception ex)
            {
                return Results.Problem("Provisioning failed: " + ex.Message);
            }

            // Save dbName + dbUser onto the agency row; mark approved
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

            // Notify the agency by email — best-effort, non-fatal
            try
            {
                await SendApprovedEmail(smtp, email1!, name!);
            } catch { /* ignore */ }

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

        // =============================================================
        //   Manage Agency — read + update agency profile (multi-contact)
        //   Admin opens "Manage" on an agency row in manage.html and edits
        //   its primary mobile, secondary mobile, and up to ~20 extra
        //   contacts (mobiles_extra TEXT, newline-separated).
        // =============================================================
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

            // Split the newline-separated extras into a clean array — empty
            // lines and duplicates of mobile1/2 stripped so the admin UI
            // doesn't double-show them.
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

        // PATCH semantics — body fields that are present overwrite; missing
        // fields are left alone. The extras list is a full replacement
        // (sending an empty array clears all extras).
        app.MapPost("/api/agency/manage/agency/{id:int}", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);

            // Read raw JSON since the body is a flexible field set.
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            string? S(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

            var sets   = new List<string>();
            var args   = new List<(string, object?)> { ("@id", id) };
            void Maybe(string col, string? val)
            {
                if (val == null) return;          // field absent → leave alone
                sets.Add($"{col}=@{col}");
                args.Add(($"@{col}", string.IsNullOrWhiteSpace(val) ? (object?)DBNull.Value : val.Trim()));
            }
            Maybe("name",    S("name"));
            Maybe("address", S("address"));
            Maybe("mobile1", S("mobile1"));
            Maybe("mobile2", S("mobile2"));

            // Extras come as an array of strings — collapse to newline-separated.
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

        // =============================================================
        //   Desktop agency self-profile — read + update own agency
        //   The WPF desktop signs in via /api/agency/desktop/login and gets
        //   an agt1 Bearer token. These endpoints let that signed-in agency
        //   read and edit its OWN crm_master.agencies row (name, address,
        //   primary/secondary mobile, up to 20 extra contacts). Because the
        //   mobile app's Agency panel reads the same crm_master.agencies row
        //   (MobileRepository.GetAgencyInfoAsync), edits here flow through to
        //   the mobile app automatically. Auth is the agency Bearer token —
        //   NOT the manage-portal token — so the agency edits only itself.
        // =============================================================
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

        // =============================================================
        //   Support tickets
        //   Agency (desktop, agt1 Bearer) raises a ticket with an optional
        //   screenshot; the super-admin sees every agency's tickets in
        //   manage.html (manage token) and replies / sets status. The agency
        //   then sees the reply + status back in the app. All rows live in
        //   crm_master.support_tickets. Screenshots are saved under
        //   /opt/vkapi/agency-uploads/tickets and served via /agency-uploads/.
        // =============================================================
        const string TICKETS_DIR = "/opt/vkapi/agency-uploads/tickets";
        try { Directory.CreateDirectory(TICKETS_DIR); } catch { }

        // Agency raises a ticket. Body: { subject, message, screenshotBase64? }
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

            // Look up the agency name for display in manage.
            string agencyName = me.slug;
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            await using (var nc = new MySqlCommand("SELECT name FROM agencies WHERE id=@id LIMIT 1", conn))
            {
                nc.Parameters.AddWithValue("@id", me.id);
                if (await nc.ExecuteScalarAsync() is string n) agencyName = n;
            }

            // Optional screenshot — decode base64 → file under tickets dir.
            string? shotPath = null;
            if (!string.IsNullOrEmpty(shotB64))
            {
                try
                {
                    var raw = shotB64.Contains(',') ? shotB64[(shotB64.IndexOf(',') + 1)..] : shotB64;
                    var bytes = Convert.FromBase64String(raw);
                    if (bytes.Length > 0 && bytes.Length <= 8 * 1024 * 1024) // cap 8 MB
                    {
                        var fn = $"ticket_{me.slug}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.jpg";
                        await File.WriteAllBytesAsync(Path.Combine(TICKETS_DIR, fn), bytes);
                        shotPath = "tickets/" + fn;
                    }
                }
                catch { /* bad image → save the ticket without it */ }
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

        // Agency lists its own tickets, each WITH its full message thread.
        app.MapGet("/api/agency/desktop/tickets", async (HttpContext ctx) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();

            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            var tickets = await ReadTicketHeaders(conn,
                "WHERE agency_slug=@s ORDER BY id DESC LIMIT 200", false, ("@s", me.slug));
            foreach (var t in tickets) t["messages"] = await LoadMessages(conn, (int)t["id"]);
            return Results.Ok(tickets);
        });

        // Agency posts a follow-up message on its own ticket.
        app.MapPost("/api/agency/desktop/tickets/{id:int}/messages", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            var who = VerifyAgencyBearer(ctx);
            if (who is not { } me) return Results.Unauthorized();
            var body = await ReadBody(req);
            if (body.Length < 1) return Results.BadRequest(new { message = "Empty message" });
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            // Ownership check — agency can only post on its own ticket.
            await using (var chk = new MySqlCommand("SELECT agency_slug FROM support_tickets WHERE id=@id", conn))
            {
                chk.Parameters.AddWithValue("@id", id);
                if (await chk.ExecuteScalarAsync() as string != me.slug)
                    return Results.NotFound(new { message = "Ticket not found" });
            }
            await AddMessage(conn, id, "agency", body);
            return Results.Ok(new { ok = true });
        });

        // ── Client error log ────────────────────────────────────────────────
        // Desktop app reports ANY failure here (upload errors, crashes, etc.) so
        // every unsuccessful operation is captured centrally and visible in the
        // manage portal — no more guessing at vague "error sending request".
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

        // Manage: list recent client errors (all agencies, or one via ?agency=slug).
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

        // Manage: list ALL agencies' tickets (headers only — modal loads thread).
        app.MapGet("/api/agency/manage/tickets", async (HttpContext ctx) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            var tickets = await ReadTicketHeaders(conn,
                "ORDER BY (status='resolved'), id DESC LIMIT 500", true);
            return Results.Ok(tickets);
        });

        // Manage: full message thread for one ticket.
        app.MapGet("/api/agency/manage/tickets/{id:int}/messages", async (HttpContext ctx, int id) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            await using var conn = new MySqlConnection(masterConn);
            await conn.OpenAsync();
            return Results.Ok(await LoadMessages(conn, id));
        });

        // Manage: post an admin message on a ticket (any number, any time).
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

        // Manage: set status only. Body { status }
        app.MapPost("/api/agency/manage/tickets/{id:int}", async (HttpContext ctx, int id, HttpRequest req) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx))
                return Results.Json(new { message = "Unauthorized" }, statusCode: 401);
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            // Back-compat: an "adminReply" in the body is treated as a new message.
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

        // =============================================================
        //   Per-agency Android builds — list + download
        //   The portal serves white-labeled APK / AAB files for each
        //   approved agency. Files live at:
        //       /opt/vkapi/agency-apps/<flavor>/app.apk
        //       /opt/vkapi/agency-apps/<flavor>/app.aab
        //   <flavor> = slug with underscores stripped (matches Gradle).
        //   Both endpoints are gated by the manage token.
        // =============================================================
        const string AGENCY_APPS_ROOT = "/opt/vkapi/agency-apps";

        app.MapGet("/api/agency/manage/apps", async (HttpContext ctx) =>
        {
            if (!await IsManageTokenValid(masterConn, ctx)) return Results.Unauthorized();

            // Source of truth for which agencies exist is the master DB.
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
                // Gradle flavor name strips underscores from the slug.
                string flavor = slug.Replace("_", "");
                string pkg    = $"com.crmrecoverysoftware.{flavor}";
                string apk      = Path.Combine(AGENCY_APPS_ROOT, flavor, "app.apk");
                string aab      = Path.Combine(AGENCY_APPS_ROOT, flavor, "app.aab");
                string setup    = Path.Combine(AGENCY_APPS_ROOT, flavor, "setup.exe");
                string portable = Path.Combine(AGENCY_APPS_ROOT, flavor, "portable.zip");
                // Resolve logo URL — logo_path is something like
                // "/agency-uploads/rk_enterprises.jpg" stored at registration.
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

        // GET /api/agency/manage/apps/{flavor}/download/{type}   type=apk|aab
        // Token can be passed as ?token=... so plain <a download> links work
        // (browsers don't send custom headers on direct downloads).
        app.MapGet("/api/agency/manage/apps/{flavor}/download/{type}", async (HttpContext ctx, string flavor, string type) =>
        {
            // Accept token via header OR query string for direct-link downloads.
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

            // Hard sanitize — flavor must be lowercase alphanumeric only.
            // type ∈ { apk, aab, setup } maps to a fixed file under the
            // flavor dir; anything else is a path-traversal attempt.
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

            // PhysicalFile streams without loading into memory.
            return Results.File(path, mime, downloadName);
        });

        // =============================================================
        //   Desktop app login — email + password → agency session token
        //   The returned token is an HMAC-signed AgencyToken; the desktop
        //   sends it as a Bearer header and the routing middleware uses it
        //   to serve every request from the agency's own tenant database.
        // =============================================================
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
                    "rejected"  => "Your agency registration was not approved. Please contact CRMS support.",
                    "suspended" => "Your agency account has been suspended. Please contact CRMS support.",
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
    }

    // ─────────────────────────────────────────────────────────────────
    //   Helpers
    // ─────────────────────────────────────────────────────────────────

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
        // Cryptographic random 6-digit code (000000–999999)
        var bytes = RandomNumberGenerator.GetBytes(4);
        int v = BitConverter.ToInt32(bytes, 0) & 0x7FFFFFFF;
        return (v % 1000000).ToString("D6");
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes);
    }

    // PBKDF2-SHA256, 100k iterations. Format: pbkdf2$<iter>$<salt-b64>$<hash-b64>
    private static string HashPassword(string password)
    {
        const int iter = 100_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        using var kdf = new Rfc2898DeriveBytes(password, salt, iter, HashAlgorithmName.SHA256);
        var hash = kdf.GetBytes(32);
        return $"pbkdf2${iter}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    // Used by future Login endpoint (batch 2).
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

    // Deterministic per-tenant DB password from a server secret + slug — so
    // we never have to store the cleartext (decryptable) DB password.
    public static string DeriveTenantPassword(string slug)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TenantDbSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes("tenant:" + slug));
        // Use URL-safe base64, trim to a comfortable 28 chars, prepend a marker
        return "T1!" + Convert.ToBase64String(bytes).Replace('+','-').Replace('/','_').Substring(0, 25);
    }

    private static async Task<bool> WasRecentlyVerified(MySqlConnection conn, string email)
    {
        // The most recent consumed OTP for this email must be within last 15 min.
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
        // Slugify: lowercase alphanumerics + underscore. Strip everything else.
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
        // Validate identifiers — defence in depth. Only [a-z0-9_]+
        if (!Regex.IsMatch(dbName, "^[a-z0-9_]+$") || !Regex.IsMatch(dbUser, "^[a-z0-9_]+$"))
            throw new Exception("Internal: invalid identifier in provisioning.");

        // 1) CREATE DATABASE + CREATE USER + GRANT (using crm_provisioner)
        await using (var conn = new MySqlConnection(provConn))
        {
            await conn.OpenAsync();

            // The single SQL block does the trio. Using STATEMENT for each so a partial
            // failure can be cleanly diagnosed in the log.
            await Exec(conn, $"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;");
            await Exec(conn, $"CREATE USER IF NOT EXISTS `{dbUser}`@`localhost` IDENTIFIED BY @pwd;", ("@pwd", dbPass));
            await Exec(conn, $"GRANT ALL PRIVILEGES ON `{dbName}`.* TO `{dbUser}`@`localhost`;");
            await Exec(conn, "FLUSH PRIVILEGES;");
        }

        // 2) Apply the schema template via a fresh connection AS THE TENANT USER
        string tenantConn =
            $"server={mysqlHost};port={mysqlPort};database={dbName};" +
            $"uid={dbUser};pwd={dbPass};" +
             "Pooling=false;AllowUserVariables=true;DefaultCommandTimeout=120;";

        // tenant_template.sql (schema) + tenant_seed.sql (default column types
        // / mappings) ship alongside the published app. Both are applied on the
        // same connection, so every new tenant DB is born seeded — with no
        // runtime communication with vkre_db1 or any other database.
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

    // Locates a file shipped under dbschema/ next to the API binary, with a
    // dev-run fallback to the repo's ../dbschema/ folder.
    private static string? ResolveSchemaFile(string name, bool required)
    {
        string p = Path.Combine(AppContext.BaseDirectory, "dbschema", name);
        if (!File.Exists(p))
            p = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "dbschema", name));
        if (File.Exists(p)) return p;
        if (required) throw new Exception($"{name} not found alongside the API binary.");
        return null;
    }

    // Executes a multi-statement SQL script (a mysqldump --no-data file that
    // may also carry stored routines). MySqlConnector has no MySqlScript and
    // rejects the mysql-client `DELIMITER` directive, so we parse it ourselves:
    //   • track the active statement delimiter (a DELIMITER line switches it),
    //   • skip blank / "--" lines only when between statements,
    //   • strip mysqldump's `DEFINER=` clause — the tenant user can't set a
    //     routine's definer to another account, so the routine ends up owned
    //     by the tenant user instead.
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
        // mysqldump tags routines with the source server's DEFINER account.
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

    // ── Email senders ─────────────────────────────────────────────────
    private sealed class SmtpConfig
    {
        public string Host = ""; public int Port;
        public string User = ""; public string Pass = "";
        public string FromAddr = ""; public string FromName = "";
    }

    private static async Task SendOtpEmail(SmtpConfig s, string to, string code)
    {
        string subject = $"Your CRMS verification code: {code}";
        string html = $@"
<div style=""font-family:Inter,Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;padding:32px;color:#0F172A;background:#F6F8FB;"">
  <div style=""background:#fff;border-radius:14px;border:1px solid #E4E9F0;padding:32px;"">
    <h2 style=""margin:0 0 8px;font-size:20px;font-weight:800;color:#4F46E5;"">CRMS Agency Portal</h2>
    <p style=""margin:0 0 22px;color:#64748B;font-size:14px;"">Use the code below to verify your email address. It is valid for 10 minutes.</p>
    <div style=""font-size:36px;font-weight:800;letter-spacing:.18em;text-align:center;padding:18px;background:#EEF2FF;color:#4338CA;border-radius:10px;"">{code}</div>
    <p style=""margin:22px 0 0;color:#64748B;font-size:12.5px;"">If you did not request this code, you can safely ignore this email.</p>
  </div>
  <p style=""text-align:center;color:#94A3B8;font-size:11px;margin-top:14px;"">© CRMS · team@crmrecoverysoftware.com</p>
</div>";
        await SendMail(s, to, subject, html);
    }

    private static async Task SendApprovedEmail(SmtpConfig s, string to, string agencyName)
    {
        string subject = "Your CRMS agency has been approved";
        string html = $@"
<div style=""font-family:Inter,Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;padding:32px;color:#0F172A;background:#F6F8FB;"">
  <div style=""background:#fff;border-radius:14px;border:1px solid #E4E9F0;padding:32px;"">
    <h2 style=""margin:0 0 8px;font-size:22px;font-weight:800;color:#10B981;"">You're approved 🎉</h2>
    <p style=""margin:0 0 18px;color:#0F172A;font-size:15px;""><strong>{System.Net.WebUtility.HtmlEncode(agencyName)}</strong>, your CRMS agency account is now active.</p>
    <p style=""margin:0 0 18px;color:#64748B;font-size:14px;"">You can sign in to the desktop application using your primary email and the password you set during registration.</p>
    <p style=""margin:0;color:#64748B;font-size:13px;"">Your agency has its own private workspace — your data is fully isolated from every other agency.</p>
  </div>
  <p style=""text-align:center;color:#94A3B8;font-size:11px;margin-top:14px;"">© CRMS · team@crmrecoverysoftware.com</p>
</div>";
        await SendMail(s, to, subject, html);
    }

    private static async Task SendManageOtpEmail(SmtpConfig s, string to, string code)
    {
        string subject = $"CRMS admin sign-in code: {code}";
        string html = $@"
<div style=""font-family:Inter,Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;padding:32px;color:#0F172A;background:#F6F8FB;"">
  <div style=""background:#fff;border-radius:14px;border:1px solid #E4E9F0;padding:32px;"">
    <h2 style=""margin:0 0 8px;font-size:20px;font-weight:800;color:#EA580C;"">CRMS Administrator</h2>
    <p style=""margin:0 0 22px;color:#64748B;font-size:14px;"">Use the code below to finish signing in to the manage page. It is valid for 10 minutes.</p>
    <div style=""font-size:36px;font-weight:800;letter-spacing:.18em;text-align:center;padding:18px;background:#FFF3EA;color:#9A3412;border-radius:10px;"">{code}</div>
    <p style=""margin:22px 0 0;color:#64748B;font-size:12.5px;"">If you did not request this code, someone may have tried to access the admin page — you can safely ignore the email.</p>
  </div>
  <p style=""text-align:center;color:#94A3B8;font-size:11px;margin-top:14px;"">© CRMS · admin sign-in</p>
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
            Credentials = new NetworkCredential(s.User, s.Pass),
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        await client.SendMailAsync(msg);
    }
}
