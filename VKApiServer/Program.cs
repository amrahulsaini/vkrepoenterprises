using System.Data;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using MySqlConnector;
using VKApiServer;
using VKApiServer.Models;

LocalEnv.LoadBestEffort();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddMemoryCache();
builder.WebHost.ConfigureKestrel(opts => opts.Limits.MaxRequestBodySize = 200 * 1024 * 1024); // 200 MB for bulk uploads

builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true; // Cloudflare terminates TLS; origin is HTTP anyway, but safe to enable
    opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    opts.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/json", "application/ndjson", "text/plain" });
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(opts =>
    opts.Level = CompressionLevel.Fastest); // Fastest = still 8-10x smaller, zero CPU bottleneck

// Legacy single-tenant database (vkre_db1). Agency requests are routed to
// their own tenant DB per-request by the middleware below; see TenantContext.
TenantContext.DefaultConn = new MySqlConnectionStringBuilder
{
    Server   = Environment.GetEnvironmentVariable("MYSQL_HOST")     ?? "127.0.0.1",
    UserID   = Environment.GetEnvironmentVariable("MYSQL_USER")     ?? "vkre_db1",
    Password = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? "db1",
    Database = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "vkre_db1",
    Port     = uint.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var p) ? p : 3306u,
    SslMode  = MySqlSslMode.None,
    Pooling  = true,
    MaximumPoolSize       = 20,
    ConnectionTimeout     = 10,
    DefaultCommandTimeout = 30,
    AllowLoadLocalInfile  = true
}.ConnectionString;

var desktopLoginPassword = Environment.GetEnvironmentVariable("DESKTOP_LOGIN_PASSWORD") ?? "vk@kunal.admin";
var privateKey = Environment.GetEnvironmentVariable("PRIVATEKEY") ?? "vk_enterprises_local_jwt_key";
var port = Environment.GetEnvironmentVariable("PORT") ?? "5002";

// MySQL host/port — used for the default DB connection and per-tenant routing.
var mysqlHost = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? "127.0.0.1";
var mysqlPort = int.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var mysqlPortParsed) ? mysqlPortParsed : 3306;

var app = builder.Build();
app.UseResponseCompression(); // must be first — compresses everything below it
app.UseCors();

// ── Multi-tenant request routing ─────────────────────────────────────────────
// A valid CRMS agency Bearer token (issued by /api/agency/desktop/login) routes
// this request — for its whole async lifetime — to that agency's own isolated
// database. No token, or a legacy desktop token → the default vkre_db1 database.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    // /api/agency/* endpoints manage their own auth and always use crm_master.
    if (!path.StartsWith("/api/agency", StringComparison.OrdinalIgnoreCase))
    {
        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        string? token = authHeader != null
            && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader.Substring(7).Trim()
            : null;
        if (AgencyToken.LooksLikeAgencyToken(token))
        {
            var parsed = AgencyToken.Verify(token);
            if (parsed is not { } agency)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new { message = "Session expired — please sign in again." });
                return;
            }
            TenantContext.Conn = TenantContext.BuildTenantConn(mysqlHost, mysqlPort, agency.slug);
            TenantContext.Key  = agency.slug;
        }
    }
    await next();
});

// Shared HttpClient for forwarding mobile requests to VKmobileapi (port 5001)
var mobileHttp = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5001/"),
    Timeout     = TimeSpan.FromSeconds(60)
};

// ── Public health / status endpoint ─────────────────────────────────────────
// Powers the "System Status" page. No auth — it only reports up/down + latency
// per component, nothing sensitive. Each check is bounded so a hung dependency
// can't hang the status check itself.
app.MapGet("/api/health", async () =>
{
    var components = new List<object>();
    bool allOk = true;

    // Database — any successful connection + SELECT 1 proves MariaDB is reachable.
    var dbSw = Stopwatch.StartNew();
    bool dbOk;
    try
    {
        await using var conn = new MySqlConnection(TenantContext.DefaultConn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT 1", conn) { CommandTimeout = 5 };
        await cmd.ExecuteScalarAsync();
        dbOk = true;
    }
    catch { dbOk = false; }
    dbSw.Stop();
    allOk &= dbOk;
    components.Add(new { name = "Database", status = dbOk ? "operational" : "down", latencyMs = (long)dbSw.ElapsedMilliseconds });

    // Mobile API (VKmobileapi on :5001) — hit a light public endpoint.
    var mSw = Stopwatch.StartNew();
    bool mOk;
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var r = await mobileHttp.GetAsync("api/mobile/agencies", cts.Token);
        mOk = r.IsSuccessStatusCode;
    }
    catch { mOk = false; }
    mSw.Stop();
    allOk &= mOk;
    components.Add(new { name = "Mobile API", status = mOk ? "operational" : "down", latencyMs = (long)mSw.ElapsedMilliseconds });

    // Web API (this service) — if it's answering, it's up.
    components.Add(new { name = "Web API", status = "operational", latencyMs = 0L });

    return Results.Ok(new
    {
        overall   = allOk ? "operational" : "degraded",
        checkedAt = DateTime.UtcNow.ToString("o"),
        components
    });
});

app.MapPost("/api/AppUsers/Login", async (LoginRequest request) =>
{
    if (!Regex.IsMatch(request.mobileno ?? string.Empty, @"^\d{10}$"))
        return Results.BadRequest(new { message = "Please enter a valid 10-digit mobile number." });

    if (!string.Equals(request.password, desktopLoginPassword, StringComparison.Ordinal))
        return Results.BadRequest(new { message = "Invalid mobile number or password." });

    int appUserId = 0;
    string fullName = "CRMS ADMIN";

    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, name FROM users WHERE mobile = @m AND status = 'ACTIVE' LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@m", request.mobileno);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0)) appUserId = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) fullName = reader.GetString(1);
        }
    }
    catch { }

    if (string.IsNullOrWhiteSpace(fullName)) fullName = "CRMS ADMIN";
    var nameParts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var tokenPayload = $"{request.mobileno}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:{privateKey}";

    return Results.Ok(new SignedAppUser
    {
        AppUserId = appUserId,
        MobileNo  = request.mobileno ?? string.Empty,
        FirstName = nameParts.FirstOrDefault() ?? "VK",
        LastName  = nameParts.Length > 1 ? nameParts[1] : "ADMIN",
        IsActive  = true,
        IsAdmin   = true,
        Token     = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tokenPayload))
    });
});

app.MapGet("/api/Overview", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "home-dashboard",
        () => DashboardRepository.BuildHomeDashboardAsync(TenantContext.Conn), 30)));

app.MapGet("/api/Records/Search", async (string? q, string? mode) =>
    Results.Ok(await DashboardRepository.BuildVehicleSearchAsync(TenantContext.Conn, q, mode)));

app.MapPost("/api/Records/MarkReleased/{id}", async (string id) =>
{
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var getCmd = new MySqlCommand(
            "SELECT is_released FROM vehicle_records WHERE id = @id LIMIT 1", conn);
        getCmd.Parameters.AddWithValue("@id", id);
        var current = (await getCmd.ExecuteScalarAsync())?.ToString()?.ToUpperInvariant();
        var newStatus = current == "YES" ? "NO" : "YES";
        await using var upd = new MySqlCommand(
            "UPDATE vehicle_records SET is_released = @s, updated_at = @d WHERE id = @id", conn);
        upd.Parameters.AddWithValue("@s", newStatus);
        upd.Parameters.AddWithValue("@d", DateTime.UtcNow);
        upd.Parameters.AddWithValue("@id", id);
        await upd.ExecuteNonQueryAsync();
        return Results.Ok(new { status = newStatus });
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapDelete("/api/Records/Delete/{id}", async (string id) =>
{
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM vehicle_records WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        return Results.Ok();
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapPost("/api/Records/PostRecordsFile", async (HttpRequest req) =>
{
    try
    {
        if (!req.HasFormContentType)
            return Results.BadRequest(new { message = "Request must have form content type." });

        var form = await req.ReadFormAsync();
        var file = form.Files.GetFile("RecordsFile");
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { message = "No file uploaded." });

        var branchIdStr = req.Query["BranchId"].ToString();
        if (string.IsNullOrWhiteSpace(branchIdStr))
            return Results.BadRequest(new { message = "BranchId is required." });

        using var reader = new StreamReader(file.OpenReadStream());
        var csvContent = await reader.ReadToEndAsync();
        var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            return Results.BadRequest(new { message = "CSV file is empty." });

        int successCount = 0;
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        foreach (var line in lines)
        {
            var f = line.Split('|');
            if (f.Length < 32) continue;
            try
            {
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO records
                        (rc_no,chassis_no,model,engine_no,agreement_no,customer_name,customer_address,
                         region,area,bucket,gv,od,branch_name,level1,level1_contact,level2,level2_contact,
                         level3,level3_contact,level4,level4_contact,sec9_available,sec17_available,
                         tbr_flag,seasoning,sender_mail_id1,sender_mail_id2,executive_name,pos,toss,
                         customer_contact_nos,remark,branch_id,status,created_at)
                    VALUES
                        (@f0,@f1,@f2,@f3,@f4,@f5,@f6,@f7,@f8,@f9,@f10,@f11,@f12,@f13,@f14,@f15,@f16,
                         @f17,@f18,@f19,@f20,@f21,@f22,@f23,@f24,@f25,@f26,@f27,@f28,@f29,@f30,@f31,
                         @bid,'uploaded',@now)", conn);
                for (int i = 0; i < 32; i++)
                    cmd.Parameters.AddWithValue($"@f{i}", f[i] ?? "");
                cmd.Parameters.AddWithValue("@bid", branchIdStr);
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync();
                successCount++;
            }
            catch { }
        }

        int fileId = 0;
        try
        {
            await using var meta = new MySqlCommand(@"
                INSERT INTO file_info (file_name,file_size,branch_id,records_count,upload_status,uploaded_at,uploaded_by)
                VALUES (@fn,@fs,@bid,@rc,'completed',@ua,'desktop_app');
                SELECT LAST_INSERT_ID();", conn);
            meta.Parameters.AddWithValue("@fn", file.FileName);
            meta.Parameters.AddWithValue("@fs", file.Length);
            meta.Parameters.AddWithValue("@bid", branchIdStr);
            meta.Parameters.AddWithValue("@rc", successCount);
            meta.Parameters.AddWithValue("@ua", DateTime.UtcNow);
            fileId = Convert.ToInt32(await meta.ExecuteScalarAsync());
        }
        catch { }

        return Results.Ok(new
        {
            message = "Records uploaded successfully.",
            recordsInserted = successCount,
            fileId = fileId.ToString(),
            timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Error uploading records: " + ex.Message });
    }
});

app.MapGet("/api/Mapping/GetMappingDetails", () => Results.Ok(new
{
    mappings = new List<object>(),
    columnTypes = new List<object>
    {
        new { columnTypeId = 1,  columnTypeName = "Vehicle No" },
        new { columnTypeId = 2,  columnTypeName = "Chasis No" },
        new { columnTypeId = 3,  columnTypeName = "Model" },
        new { columnTypeId = 4,  columnTypeName = "Engine No" },
        new { columnTypeId = 5,  columnTypeName = "Agreement No" },
        new { columnTypeId = 6,  columnTypeName = "Customer Name" },
        new { columnTypeId = 7,  columnTypeName = "Customer Address" },
        new { columnTypeId = 8,  columnTypeName = "Region" },
        new { columnTypeId = 9,  columnTypeName = "Area" },
        new { columnTypeId = 10, columnTypeName = "Bucket" },
        new { columnTypeId = 11, columnTypeName = "GV" },
        new { columnTypeId = 12, columnTypeName = "OD" },
        new { columnTypeId = 13, columnTypeName = "Branch" },
        new { columnTypeId = 14, columnTypeName = "Level 1" },
        new { columnTypeId = 15, columnTypeName = "Level 1 Contact No" },
        new { columnTypeId = 16, columnTypeName = "Level 2" },
        new { columnTypeId = 17, columnTypeName = "Level 2 Contact No" },
        new { columnTypeId = 18, columnTypeName = "Level 3" },
        new { columnTypeId = 19, columnTypeName = "Level 3 Contact No" },
        new { columnTypeId = 20, columnTypeName = "Level 4" },
        new { columnTypeId = 21, columnTypeName = "Level 4 Contact No" },
        new { columnTypeId = 22, columnTypeName = "Sec 9 Available" },
        new { columnTypeId = 23, columnTypeName = "Sec 17 Available" },
        new { columnTypeId = 24, columnTypeName = "TBR Flag" },
        new { columnTypeId = 25, columnTypeName = "Seasoning" },
        new { columnTypeId = 28, columnTypeName = "Executive Name" },
        new { columnTypeId = 29, columnTypeName = "POS" },
        new { columnTypeId = 30, columnTypeName = "TOSS" },
        new { columnTypeId = 31, columnTypeName = "Customer Contact Nos" },
        new { columnTypeId = 32, columnTypeName = "Remark" }
    }
}));

app.MapPost("/api/Mapping/UnMap", () => Results.Ok(new { message = "Unmapped." }));
app.MapPost("/api/Mapping/CreateMapping", () => Results.Ok(new { mappingId = 999, columnTypeId = 1, name = "mapped" }));

app.MapGet("/api/Branches/GetBranches/{financeId}", async (int financeId) =>
{
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, name, contact1, contact2 FROM branches WHERE finance_id = @fid AND is_active = 1 ORDER BY name LIMIT 100",
            conn);
        cmd.Parameters.AddWithValue("@fid", financeId);
        var branches = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            branches.Add(new
            {
                branchId      = reader.GetInt32(0).ToString(),
                branchName    = reader.IsDBNull(1) ? "" : reader.GetString(1),
                headOfficeName = ""
            });
        }
        return Results.Ok(branches);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Error loading branches: " + ex.Message });
    }
});

app.MapGet("/api/Finances", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "finance-dashboard",
        () => DashboardRepository.BuildFinanceDashboardAsync(TenantContext.Conn), 45)));

app.MapGet("/api/AppUsers", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "users-dashboard",
        () => DashboardRepository.BuildUsersDashboardAsync(TenantContext.Conn), 45)));

app.MapGet("/api/Uploads", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "uploads-dashboard",
        () => DashboardRepository.BuildUploadsDashboardAsync(TenantContext.Conn), 45)));

app.MapGet("/api/DetailsViews", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "details-dashboard",
        () => DashboardRepository.BuildDetailsDashboardAsync(TenantContext.Conn), 30)));

app.MapGet("/api/OTPs", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "otp-dashboard",
        () => DashboardRepository.BuildOtpDashboardAsync(TenantContext.Conn), 30)));

app.MapGet("/api/Reports", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "reports-dashboard",
        () => DashboardRepository.BuildReportsDashboardAsync(TenantContext.Conn), 60)));

app.MapGet("/api/Payments", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "payments-dashboard",
        () => DashboardRepository.BuildPaymentsDashboardAsync(TenantContext.Conn), 45)));

app.MapGet("/api/PaymentMethods", async (IMemoryCache cache) =>
    Results.Ok((await GetCachedAsync(cache, "payments-dashboard",
        () => DashboardRepository.BuildPaymentsDashboardAsync(TenantContext.Conn), 45)).PaymentMethods));

app.MapPost("/api/Confirmations", async (ConfirmationRequest req) =>
{
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            INSERT INTO repoconformations
                (vehicle_no,chassis_no,model,engine_no,customer_name,customer_contact_nos,
                 customer_address,finance_name,branch_name,branch_contact_1,branch_contact_2,
                 branch_contact_3,seizer_id,seizer_name,vehicle_contains_load,load_description,
                 confirm_by,status,yard,apply_amt_credited,amount_credited,created_at,updated_at)
            VALUES
                (@vn,@cn,@m,@en,@cname,@ccontact,@caddr,@fname,@bname,@bc1,@bc2,@bc3,
                 @sid,@sname,@vcl,@ld,@cb,@st,@yard,@aac,@ac,@now,@now);
            SELECT LAST_INSERT_ID();", conn);
        cmd.Parameters.AddWithValue("@vn",   req.VehicleNo);
        cmd.Parameters.AddWithValue("@cn",   req.ChassisNo);
        cmd.Parameters.AddWithValue("@m",    req.Model);
        cmd.Parameters.AddWithValue("@en",   req.EngineNo);
        cmd.Parameters.AddWithValue("@cname",req.CustomerName);
        cmd.Parameters.AddWithValue("@ccontact", req.CustomerContactNos);
        cmd.Parameters.AddWithValue("@caddr",req.CustomerAddress);
        cmd.Parameters.AddWithValue("@fname",req.FinanceName);
        cmd.Parameters.AddWithValue("@bname",req.BranchName);
        cmd.Parameters.AddWithValue("@bc1",  req.BranchFirstContactDetails);
        cmd.Parameters.AddWithValue("@bc2",  req.BranchSecondContactDetails);
        cmd.Parameters.AddWithValue("@bc3",  req.BranchThirdContactDetails);
        cmd.Parameters.AddWithValue("@sid",  req.SeizerId);
        cmd.Parameters.AddWithValue("@sname",req.SeizerName);
        cmd.Parameters.AddWithValue("@vcl",  req.VehicleContainsLoad);
        cmd.Parameters.AddWithValue("@ld",   req.LoadDescription);
        cmd.Parameters.AddWithValue("@cb",   req.ConfirmBy);
        cmd.Parameters.AddWithValue("@st",   req.Status);
        cmd.Parameters.AddWithValue("@yard", req.Yard);
        cmd.Parameters.AddWithValue("@aac",  req.ApplyAmtCredited);
        cmd.Parameters.AddWithValue("@ac",   (double)req.AmountCredited);
        cmd.Parameters.AddWithValue("@now",  DateTime.UtcNow);
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return Results.Ok(new { id = id.ToString() });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

// Legacy "fetch every row" endpoint — kept for back-compat with any external
// caller, but the desktop now uses /api/Confirmations/paged below. Capped at
// 5000 rows so an over-grown table doesn't blow up the legacy caller either.
app.MapGet("/api/Confirmations", async () =>
{
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id,vehicle_no,chassis_no,model,seizer_name,status,created_at FROM repoconformations ORDER BY created_at DESC LIMIT 5000",
            conn);
        var results = new List<ConfirmationResponseItem>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ConfirmationResponseItem
            {
                Id          = reader.GetInt32(0).ToString(),
                VehicleNo   = reader.IsDBNull(1) ? "" : reader.GetString(1),
                ChassisNo   = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Model       = reader.IsDBNull(3) ? "" : reader.GetString(3),
                SeizerName  = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Status      = reader.IsDBNull(5) ? "" : reader.GetString(5),
                ConfirmedOn = reader.IsDBNull(6) ? "" : reader.GetDateTime(6).ToLocalTime().ToString("dd-MM-yyyy")
            });
        }
        return Results.Ok(results);
    }
    catch { return Results.Ok(new List<ConfirmationResponseItem>()); }
});

// Paginated + filterable confirmations endpoint. Replaces the desktop's
// previous "load every row on page open" behaviour — older agencies' tables
// grew large enough that the unpaginated fetch was timing out and freezing
// the WPF grid for 10+ seconds on Confirmations tab open.
//
// Query params (all optional):
//   page = 0-based page index   (default 0)
//   size = rows per page        (default 200, max 1000)
//   q    = search text          (matches vehicle_no / chassis_no)
//   from = YYYY-MM-DD inclusive (filter on created_at)
//   to   = YYYY-MM-DD inclusive
app.MapGet("/api/Confirmations/paged", async (HttpContext ctx) =>
{
    int page = int.TryParse(ctx.Request.Query["page"], out var p) ? Math.Max(0, p) : 0;
    int size = int.TryParse(ctx.Request.Query["size"], out var s) ? Math.Clamp(s, 1, 1000) : 200;
    string q    = (ctx.Request.Query["q"].FirstOrDefault() ?? "").Trim();
    string from = (ctx.Request.Query["from"].FirstOrDefault() ?? "").Trim();
    string to   = (ctx.Request.Query["to"].FirstOrDefault() ?? "").Trim();

    var where  = new List<string>();
    var ps     = new List<MySqlParameter>();
    if (!string.IsNullOrEmpty(q))
    {
        where.Add("(vehicle_no LIKE @q OR chassis_no LIKE @q)");
        ps.Add(new MySqlParameter("@q", $"%{q}%"));
    }
    if (DateTime.TryParse(from, out var fd))
    {
        where.Add("created_at >= @from");
        ps.Add(new MySqlParameter("@from", fd.Date));
    }
    if (DateTime.TryParse(to, out var td))
    {
        where.Add("created_at < @to");
        ps.Add(new MySqlParameter("@to", td.Date.AddDays(1)));
    }
    string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        long total;
        await using (var cnt = new MySqlCommand($"SELECT COUNT(*) FROM repoconformations {whereSql}", conn) { CommandTimeout = 30 })
        {
            foreach (var par in ps) cnt.Parameters.Add(par);
            total = Convert.ToInt64(await cnt.ExecuteScalarAsync());
        }

        var rows = new List<ConfirmationResponseItem>();
        var sql = $@"SELECT id,vehicle_no,chassis_no,model,seizer_name,status,created_at
                     FROM repoconformations {whereSql}
                     ORDER BY created_at DESC
                     LIMIT {size} OFFSET {page * size}";
        await using (var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 30 })
        {
            // Rebuild param list — same names but a parameter object can only
            // belong to one MySqlCommand.
            foreach (var par in ps) cmd.Parameters.Add(new MySqlParameter(par.ParameterName, par.Value));
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                rows.Add(new ConfirmationResponseItem
                {
                    Id          = rdr.GetInt32(0).ToString(),
                    VehicleNo   = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                    ChassisNo   = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    Model       = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                    SeizerName  = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                    Status      = rdr.IsDBNull(5) ? "" : rdr.GetString(5),
                    ConfirmedOn = rdr.IsDBNull(6) ? "" : rdr.GetDateTime(6).ToLocalTime().ToString("dd-MM-yyyy")
                });
            }
        }

        return Results.Ok(new
        {
            total,
            page,
            size,
            hasMore = (long)(page + 1) * size < total,
            rows
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/Modules/{moduleKey}", async (string moduleKey, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, $"module-{moduleKey}",
        () => DashboardRepository.BuildModuleStatusAsync(TenantContext.Conn, moduleKey), 45)));

app.MapGet("/", () => Results.Ok(new
{
    name = "CRMS API Server",
    mode = "mysql",
    port
}));

// ── Desktop Manager endpoints (/api/mgr/*) ──────────────────────────────────
// All SQL runs server-side at loopback speed — eliminates WAN round-trips
// for every query the desktop previously issued directly to MySQL.

static bool MgrAuth(HttpContext ctx, string key) =>
    ctx.Request.Headers.TryGetValue("X-Api-Key", out var v) && v == key;

static async Task MgrExec(string sql, MySqlConnection c, int timeout = 30,
    params (string n, object v)[] ps)
{
    await using var cmd = new MySqlCommand(sql, c) { CommandTimeout = timeout };
    foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
    await cmd.ExecuteNonQueryAsync();
}

// ── Finances ──────────────────────────────────────────────────────────────

app.MapGet("/api/mgr/finances", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        // Head offices listed A→Z. Record counts come from the maintained
        // branches.total_records column (SUM) — NOT a COUNT over vehicle_records.
        // The old COUNT(vr.id) join scanned millions of rows (~730 ms); summing
        // the per-branch counter is sub-millisecond.
        const string sql = @"
            SELECT f.id, f.name,
                   COALESCE(b.branch_cnt, 0) AS branch_count,
                   COALESCE(b.record_cnt, 0) AS total_records
            FROM finances f
            LEFT JOIN (
                SELECT finance_id,
                       COUNT(*)                       AS branch_cnt,
                       COALESCE(SUM(total_records),0) AS record_cnt
                FROM   branches
                WHERE  is_active = 1
                GROUP BY finance_id
            ) b ON b.finance_id = f.id
            ORDER BY f.name";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 60 };
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new { id = rdr.GetInt32(0), name = rdr.GetString(1),
                           branchCount = rdr.IsDBNull(2) ? 0L : rdr.GetInt64(2),
                           totalRecords = rdr.IsDBNull(3) ? 0L : rdr.GetInt64(3) });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/finances", async (HttpContext ctx, MgrCreateFinanceDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO finances (name,description) VALUES (@n,@d); SELECT LAST_INSERT_ID();", conn);
        cmd.Parameters.AddWithValue("@n", dto.Name);
        cmd.Parameters.AddWithValue("@d", dto.Description ?? "");
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return Results.Ok(new { id });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/finances/{id:int}", async (HttpContext ctx, int id, MgrUpdateNameDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("UPDATE finances SET name=@n WHERE id=@id", conn, 30, ("@n", dto.Name), ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapDelete("/api/mgr/finances/{id:int}", async (HttpContext ctx, int id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("SET foreign_key_checks=0", conn);
        // Multi-table DELETE: removes rc_info + chassis_info + vehicle_records for all branches of this finance
        await MgrExec(@"DELETE vr, rc, ci
            FROM vehicle_records vr
            INNER JOIN branches b ON vr.branch_id = b.id
            LEFT JOIN rc_info rc ON rc.vehicle_record_id = vr.id
            LEFT JOIN chassis_info ci ON ci.vehicle_record_id = vr.id
            WHERE b.finance_id = @id", conn, 300, ("@id", id));
        await MgrExec("DELETE FROM branches WHERE finance_id=@id", conn, 30, ("@id", id));
        await MgrExec("DELETE FROM finances WHERE id=@id", conn, 30, ("@id", id));
        await MgrExec("SET foreign_key_checks=1", conn);
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Branches ──────────────────────────────────────────────────────────────

app.MapGet("/api/mgr/branches", async (HttpContext ctx, int? financeId) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        string sql; MySqlCommand cmd;
        if (financeId.HasValue)
        {
            sql = @"SELECT b.id, b.name,
                           COALESCE(b.contact1,'') AS c1, COALESCE(b.contact2,'') AS c2,
                           COALESCE(b.contact3,'') AS c3, COALESCE(b.address,'')  AS addr,
                           b.total_records AS total_records,
                           IFNULL(DATE_FORMAT(b.uploaded_at,'%d %b %y %h:%i %p'),'') AS up,
                           '' AS finance_name,
                           b.finance_id
                    FROM branches b
                    WHERE b.finance_id=@fid AND b.is_active=1
                    ORDER BY total_records DESC";
            cmd = new MySqlCommand(sql, conn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@fid", financeId.Value);
        }
        else
        {
            sql = @"SELECT b.id, b.name,
                           COALESCE(b.contact1,'') AS c1, COALESCE(b.contact2,'') AS c2,
                           COALESCE(b.contact3,'') AS c3, COALESCE(b.address,'')  AS addr,
                           b.total_records AS total_records,
                           IFNULL(DATE_FORMAT(b.uploaded_at,'%d %b %y %h:%i %p'),'') AS up,
                           COALESCE(f.name,'') AS finance_name,
                           b.finance_id
                    FROM branches b LEFT JOIN finances f ON f.id=b.finance_id
                    WHERE b.is_active=1 ORDER BY b.uploaded_at DESC, b.name";
            cmd = new MySqlCommand(sql, conn) { CommandTimeout = 60 };
        }
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new { id = rdr.GetInt32(0), name = rdr.GetString(1),
                           contact1 = rdr.GetString(2), contact2 = rdr.GetString(3),
                           contact3 = rdr.GetString(4), address = rdr.GetString(5),
                           totalRecords = rdr.IsDBNull(6) ? 0L : rdr.GetInt64(6),
                           uploadedAt = rdr.GetString(7), financeName = rdr.GetString(8),
                           financeId = rdr.IsDBNull(9) ? 0 : rdr.GetInt32(9) });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/branches/{id:int}", async (HttpContext ctx, int id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id,name,COALESCE(contact1,''),COALESCE(contact2,''),COALESCE(contact3,''),COALESCE(address,''),COALESCE(branch_code,'') FROM branches WHERE id=@id LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return Results.NotFound();
        return Results.Ok(new { id = rdr.GetInt32(0), name = rdr.GetString(1),
                                contact1 = rdr.GetString(2), contact2 = rdr.GetString(3),
                                contact3 = rdr.GetString(4), address = rdr.GetString(5),
                                branchCode = rdr.GetString(6) });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/branches", async (HttpContext ctx, MgrCreateBranchDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            INSERT INTO branches (finance_id,name,contact1,contact2,contact3,address,branch_code,city,state,postal_code,notes)
            VALUES (@fid,@n,@c1,@c2,@c3,@addr,@bcode,@city,@state,@postal,@notes);
            SELECT LAST_INSERT_ID();", conn);
        cmd.Parameters.AddWithValue("@fid",   dto.FinanceId);
        cmd.Parameters.AddWithValue("@n",     dto.Name);
        cmd.Parameters.AddWithValue("@c1",    dto.Contact1  ?? "");
        cmd.Parameters.AddWithValue("@c2",    dto.Contact2  ?? "");
        cmd.Parameters.AddWithValue("@c3",    dto.Contact3  ?? "");
        cmd.Parameters.AddWithValue("@addr",  dto.Address   ?? "");
        cmd.Parameters.AddWithValue("@bcode", dto.BranchCode ?? "");
        cmd.Parameters.AddWithValue("@city",  dto.City  ?? "");
        cmd.Parameters.AddWithValue("@state", dto.State ?? "");
        cmd.Parameters.AddWithValue("@postal",dto.Postal ?? "");
        cmd.Parameters.AddWithValue("@notes", dto.Notes  ?? "");
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return Results.Ok(new { id });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/branches/{id:int}", async (HttpContext ctx, int id, MgrUpdateBranchDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE branches SET name=@n,contact1=@c1,contact2=@c2,contact3=@c3,address=@addr,branch_code=@bcode WHERE id=@id", conn);
        cmd.Parameters.AddWithValue("@n",    dto.Name);
        cmd.Parameters.AddWithValue("@c1",   dto.Contact1   ?? "");
        cmd.Parameters.AddWithValue("@c2",   dto.Contact2   ?? "");
        cmd.Parameters.AddWithValue("@c3",   dto.Contact3   ?? "");
        cmd.Parameters.AddWithValue("@addr", dto.Address    ?? "");
        cmd.Parameters.AddWithValue("@bcode",dto.BranchCode ?? "");
        cmd.Parameters.AddWithValue("@id",   id);
        await cmd.ExecuteNonQueryAsync();
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Clear: single multi-table DELETE — no chunking, fast with FK checks off
app.MapPost("/api/mgr/branches/{id:int}/clear", async (HttpContext ctx, int id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        int deletedCount = 0;
        await using (var cnt = new MySqlCommand(
            "SELECT COUNT(*) FROM vehicle_records WHERE branch_id=@id", conn) { CommandTimeout = 10 })
        {
            cnt.Parameters.AddWithValue("@id", id);
            deletedCount = Convert.ToInt32(await cnt.ExecuteScalarAsync());
        }

        await MgrExec("SET foreign_key_checks=0", conn);
        await MgrExec(@"DELETE vr, rc, ci
            FROM vehicle_records vr
            LEFT JOIN rc_info rc ON rc.vehicle_record_id = vr.id
            LEFT JOIN chassis_info ci ON ci.vehicle_record_id = vr.id
            WHERE vr.branch_id = @id", conn, 300, ("@id", id));
        await MgrExec("SET foreign_key_checks=1", conn);
        return Results.Ok(new { deletedCount });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Delete: single multi-table DELETE then drop branch — no chunking
app.MapDelete("/api/mgr/branches/{id:int}", async (HttpContext ctx, int id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("SET foreign_key_checks=0", conn);
        await MgrExec(@"DELETE vr, rc, ci
            FROM vehicle_records vr
            LEFT JOIN rc_info rc ON rc.vehicle_record_id = vr.id
            LEFT JOIN chassis_info ci ON ci.vehicle_record_id = vr.id
            WHERE vr.branch_id = @id", conn, 300, ("@id", id));
        await MgrExec("DELETE FROM branches WHERE id=@id", conn, 30, ("@id", id));
        await MgrExec("SET foreign_key_checks=1", conn);
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── App Users ─────────────────────────────────────────────────────────────

app.MapGet("/api/mgr/users", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        const string statsSql = @"
            SELECT COUNT(*) AS total,
                   SUM(is_active) AS active,
                   SUM(is_admin) AS admins,
                   (SELECT COUNT(DISTINCT user_id) FROM subscriptions WHERE end_date >= CURDATE()) AS with_sub
            FROM app_users";
        int total = 0, active = 0, admins = 0, withSub = 0;
        await using (var cmd = new MySqlCommand(statsSql, conn) { CommandTimeout = 10 })
        {
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                total   = rdr.GetInt32(0);
                active  = rdr.IsDBNull(1) ? 0 : Convert.ToInt32(rdr.GetValue(1));
                admins  = rdr.IsDBNull(2) ? 0 : Convert.ToInt32(rdr.GetValue(2));
                withSub = rdr.GetInt32(3);
            }
        }

        // Balance is the total of every subscription ever bought for the user
        // — sum of subscriptions.amount. The legacy u.balance column is no
        // longer used so what the admin sees matches the Subscriptions tab.
        const string usersSql = @"
            SELECT u.id, u.name, u.mobile, u.address, u.pincode,
                   u.pfp, u.device_id, u.is_active, u.is_admin,
                   COALESCE((SELECT SUM(s.amount) FROM subscriptions s
                              WHERE s.user_id = u.id), 0) AS balance,
                   u.created_at,
                   (SELECT MAX(s.end_date) FROM subscriptions s WHERE s.user_id = u.id) AS sub_end,
                   COALESCE(u.is_stopped,0), COALESCE(u.is_blacklisted,0)
            FROM app_users u ORDER BY u.created_at DESC";
        var users = new List<object>();
        string baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        await using (var cmd = new MySqlCommand(usersSql, conn) { CommandTimeout = 30 })
        {
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var pfpRaw = rdr.IsDBNull(5) ? null : rdr.GetString(5);
                // If pfp looks like a relative file path (no whitespace, contains '/'
                // and no '+'/'=' typical of base64), turn it into a full URL that
                // LiteSpeed serves from /uploads/. Legacy base64 entries pass through.
                string? pfpOut = pfpRaw;
                if (!string.IsNullOrEmpty(pfpRaw)
                    && pfpRaw.Length < 256
                    && pfpRaw.Contains('/')
                    && !pfpRaw.Contains('+')
                    && !pfpRaw.Contains('='))
                {
                    pfpOut = $"{baseUrl}/uploads/{pfpRaw.TrimStart('/')}";
                }
                users.Add(new
                {
                    id            = rdr.GetInt64(0),
                    name          = rdr.GetString(1),
                    mobile        = rdr.GetString(2),
                    address       = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                    pincode       = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                    pfpBase64     = pfpOut,
                    deviceId      = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                    isActive      = rdr.GetBoolean(7),
                    isAdmin       = rdr.GetBoolean(8),
                    balance       = rdr.GetDecimal(9),
                    createdAt     = rdr.GetDateTime(10),
                    subEndDate    = rdr.IsDBNull(11) ? null : rdr.GetDateTime(11).ToString("yyyy-MM-dd"),
                    isStopped     = rdr.GetBoolean(12),
                    isBlacklisted = rdr.GetBoolean(13),
                });
            }
        }
        return Results.Ok(new { stats = new { total, active, admins, withSub }, users });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Lightweight user list for picker — no pfp, fast load
app.MapGet("/api/mgr/users/picker", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, name, mobile, COALESCE(address,'') AS address, is_active
            FROM app_users ORDER BY name ASC";
        var list = new List<object>();
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id       = rdr.GetInt64(0),
                name     = rdr.GetString(1),
                mobile   = rdr.GetString(2),
                address  = rdr.GetString(3),
                isActive = rdr.GetBoolean(4)
            });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/users/stats", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT COUNT(*) AS total,
                   SUM(is_active) AS active,
                   SUM(is_admin) AS admins,
                   (SELECT COUNT(DISTINCT user_id) FROM subscriptions WHERE end_date >= CURDATE()) AS with_sub
            FROM app_users";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return Results.Ok(new { total = 0, active = 0, admins = 0, withSub = 0 });
        return Results.Ok(new
        {
            total   = rdr.GetInt32(0),
            active  = rdr.IsDBNull(1) ? 0 : Convert.ToInt32(rdr.GetValue(1)),
            admins  = rdr.IsDBNull(2) ? 0 : Convert.ToInt32(rdr.GetValue(2)),
            withSub = rdr.GetInt32(3),
        });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapMethods("/api/mgr/users/{id:long}/active", new[] { "PATCH" }, async (HttpContext ctx, long id, MgrSetActiveDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("UPDATE app_users SET is_active=@v WHERE id=@id", conn, 10,
            ("@v", dto.Active ? 1 : 0), ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapMethods("/api/mgr/users/{id:long}/admin", new[] { "PATCH" }, async (HttpContext ctx, long id, MgrSetAdminDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("UPDATE app_users SET is_admin=@v WHERE id=@id", conn, 10,
            ("@v", dto.Admin ? 1 : 0), ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/users/{id:long}/reset-device", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("UPDATE app_users SET device_id=NULL WHERE id=@id", conn, 10, ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapMethods("/api/mgr/users/{id:long}/stopped", new[] { "PATCH" }, async (HttpContext ctx, long id, MgrSetStoppedDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("UPDATE app_users SET is_stopped=@v WHERE id=@id", conn, 10,
            ("@v", dto.Stopped ? 1 : 0), ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapMethods("/api/mgr/users/{id:long}/blacklisted", new[] { "PATCH" }, async (HttpContext ctx, long id, MgrSetBlacklistedDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("UPDATE app_users SET is_blacklisted=@v, is_stopped=@v WHERE id=@id", conn, 10,
            ("@v", dto.Blacklisted ? 1 : 0), ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// DELETE /api/mgr/users/{id} — fully remove a user from the current agency.
// Atomically drops the tenant row (cascades to subscriptions, KYC, finance
// restrictions, etc. via existing FK CASCADE) AND releases the cross-agency
// claim on this mobile/device in crm_master.app_user_registry. Without the
// master cleanup, the same mobile / device cannot register at any other
// agency — they would forever appear "already registered".
app.MapDelete("/api/mgr/users/{id:long}", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        // Read mobile + device BEFORE deleting so we can release them in master.
        string? mobile = null, deviceId = null;
        await using (var sel = new MySqlCommand(
            "SELECT mobile, COALESCE(device_id,'') FROM app_users WHERE id=@id", conn))
        {
            sel.Parameters.AddWithValue("@id", id);
            await using var rdr = await sel.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                mobile   = rdr.GetString(0);
                deviceId = rdr.GetString(1);
            }
        }
        if (mobile == null) return Results.NotFound(new { message = "User not found" });

        // Drop the tenant row — FK CASCADE wipes subscriptions, kyc, finance
        // restrictions, device-change requests, etc. in one shot.
        await MgrExec("DELETE FROM app_users WHERE id=@id", conn, 15, ("@id", id));

        // Release the cross-agency claim. We scope by (mobile, agency_slug) so
        // a different agency's claim on the same mobile (which shouldn't exist
        // by the registry's UNIQUE(mobile), but we're defensive) is untouched.
        if (!string.IsNullOrEmpty(AgencyPortal.MasterConn))
        {
            await using var mconn = new MySqlConnection(AgencyPortal.MasterConn);
            await mconn.OpenAsync();
            await using var mcmd = new MySqlCommand(@"
                DELETE FROM app_user_registry
                 WHERE agency_slug = @s
                   AND (mobile = @m OR (@d <> '' AND device_id = @d))", mconn);
            mcmd.Parameters.AddWithValue("@s", TenantContext.Key);
            mcmd.Parameters.AddWithValue("@m", mobile);
            mcmd.Parameters.AddWithValue("@d", deviceId ?? "");
            await mcmd.ExecuteNonQueryAsync();
        }

        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/users/{id:long}/finance-restrictions", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT finance_id FROM user_finance_restrictions WHERE user_id=@uid", conn);
        cmd.Parameters.AddWithValue("@uid", id);
        var ids = new List<int>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) ids.Add(rdr.GetInt32(0));
        return Results.Ok(ids);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/users/{id:long}/finance-restrictions", async (HttpContext ctx, long id, MgrSetFinanceRestrictionsDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("DELETE FROM user_finance_restrictions WHERE user_id=@uid", conn, 10, ("@uid", id));
        foreach (var fid in dto.FinanceIds)
            await MgrExec(
                "INSERT INTO user_finance_restrictions (user_id, finance_id) VALUES (@uid, @fid)",
                conn, 10, ("@uid", id), ("@fid", fid));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Admin (Control Panel) password ──────────────────────────────────────────
// Sets the per-admin Control Panel password. Desktop calls this from the
// Users page for any admin user.
app.MapMethods("/api/mgr/users/{id:long}/admin-pass", new[] { "PATCH" },
    async (HttpContext ctx, long id, MgrSetAdminPassDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("UPDATE app_users SET admin_pass=@p WHERE id=@id", conn, 10,
            ("@p", string.IsNullOrWhiteSpace(dto.Password) ? (object)DBNull.Value : dto.Password),
            ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Returns whether a user has an admin password set (so the desktop can show
// "set" vs "not set" without exposing the password).
app.MapGet("/api/mgr/users/{id:long}/admin-pass", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT admin_pass FROM app_users WHERE id=@id LIMIT 1", conn) { CommandTimeout = 5 };
        cmd.Parameters.AddWithValue("@id", id);
        var v = await cmd.ExecuteScalarAsync() as string;
        return Results.Ok(new { isSet = !string.IsNullOrWhiteSpace(v) });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── KYC documents ────────────────────────────────────────────────────────────
// Returns the user's KYC document URLs (aadhaar_front, aadhaar_back, pan_front).
// Mobile API stores them as relative paths like "kyc/42/aadhaar_front.jpg".
// We build full URLs that LiteSpeed serves via the /uploads/ static context.
app.MapGet("/api/mgr/users/{id:long}/kyc", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT aadhaar_front, aadhaar_back, pan_front FROM user_kyc WHERE user_id=@uid", conn);
        cmd.Parameters.AddWithValue("@uid", id);
        await using var rdr = await cmd.ExecuteReaderAsync();

        string? af = null, ab = null, pf = null;
        if (await rdr.ReadAsync())
        {
            af = rdr.IsDBNull(0) ? null : rdr.GetString(0);
            ab = rdr.IsDBNull(1) ? null : rdr.GetString(1);
            pf = rdr.IsDBNull(2) ? null : rdr.GetString(2);
        }

        string ToUrl(string? rel) => string.IsNullOrEmpty(rel)
            ? ""
            : $"{ctx.Request.Scheme}://{ctx.Request.Host}/uploads/{rel.TrimStart('/')}";

        return Results.Ok(new
        {
            aadhaarFront = ToUrl(af),
            aadhaarBack  = ToUrl(ab),
            panFront     = ToUrl(pf)
        });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Clears a single KYC document from the DB and best-effort deletes the file.
// docType must be one of: aadhaar_front, aadhaar_back, pan_front.
app.MapDelete("/api/mgr/users/{id:long}/kyc/{docType}", async (HttpContext ctx, long id, string docType) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (docType != "aadhaar_front" && docType != "aadhaar_back" && docType != "pan_front")
        return Results.BadRequest(new { message = "invalid docType" });
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        // Read the current file path so we can remove the file on disk too.
        await using var sel = new MySqlCommand(
            $"SELECT {docType} FROM user_kyc WHERE user_id=@uid", conn);
        sel.Parameters.AddWithValue("@uid", id);
        var rel = (await sel.ExecuteScalarAsync()) as string;

        // Clear the column.
        await MgrExec(
            $"UPDATE user_kyc SET {docType}=NULL WHERE user_id=@uid",
            conn, 10, ("@uid", id));

        // Best-effort file delete — owned by www-data (VKmobileapi); may fail
        // depending on cross-service file permissions. DB is the source of truth.
        if (!string.IsNullOrEmpty(rel))
        {
            try
            {
                var fullPath = Path.Combine("/opt/vkmobileapi/uploads", rel.TrimStart('/'));
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch { /* file may be locked or unwritable — DB cleared, that's enough */ }
        }

        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/blacklist", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, name, mobile, COALESCE(address,'') AS address, created_at
            FROM app_users WHERE is_blacklisted=1 ORDER BY name";
        var list = new List<object>();
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new { id = rdr.GetInt64(0), name = rdr.GetString(1), mobile = rdr.GetString(2),
                           address = rdr.GetString(3), createdAt = rdr.GetDateTime(4) });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/users/all-simple", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, name, mobile, COALESCE(address,'') AS address,
                   is_active, is_admin, COALESCE(is_stopped,0), COALESCE(is_blacklisted,0)
            FROM app_users ORDER BY name ASC";
        var list = new List<object>();
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new { id = rdr.GetInt64(0), name = rdr.GetString(1), mobile = rdr.GetString(2),
                           address = rdr.GetString(3), isActive = rdr.GetBoolean(4), isAdmin = rdr.GetBoolean(5),
                           isStopped = rdr.GetBoolean(6), isBlacklisted = rdr.GetBoolean(7) });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/settings/subs-password", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `value` FROM app_settings WHERE `key`='subs_password' LIMIT 1", conn);
        var val = await cmd.ExecuteScalarAsync();
        return Results.Ok(new { password = val?.ToString() ?? "1234" });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/settings/subs-password", async (HttpContext ctx, MgrSetSubsPasswordDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec(
            "INSERT INTO app_settings (`key`, `value`) VALUES ('subs_password', @v) ON DUPLICATE KEY UPDATE `value`=@v",
            conn, 10, ("@v", dto.Password));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Common Control Panel password (one per agency, replaces per-user) ───────
// Stored in app_settings under 'control_panel_password'. The mobile app's
// Control Panel verify endpoint now checks this single value instead of each
// user's old app_users.admin_pass column.
app.MapGet("/api/mgr/settings/control-password", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `value` FROM app_settings WHERE `key`='control_panel_password' LIMIT 1", conn);
        var val = await cmd.ExecuteScalarAsync();
        return Results.Ok(new { password = val?.ToString() ?? "" });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/settings/control-password", async (HttpContext ctx, MgrSetSubsPasswordDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec(
            "INSERT INTO app_settings (`key`, `value`) VALUES ('control_panel_password', @v) ON DUPLICATE KEY UPDATE `value`=@v",
            conn, 10, ("@v", dto.Password));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/users/{id:long}/subscriptions", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, start_date, end_date, amount, notes, created_at
            FROM subscriptions WHERE user_id=@uid ORDER BY created_at DESC";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@uid", id);
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id        = rdr.GetInt64(0),
                startDate = rdr.GetDateTime(1).ToString("yyyy-MM-dd"),
                endDate   = rdr.GetDateTime(2).ToString("yyyy-MM-dd"),
                amount    = rdr.IsDBNull(3) ? 0m : rdr.GetDecimal(3),
                notes     = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                createdAt = rdr.GetDateTime(5),
            });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/users/{id:long}/subscriptions", async (HttpContext ctx, long id, MgrAddSubscriptionDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO subscriptions (user_id,start_date,end_date,amount,notes) VALUES (@uid,@s,@e,@a,@n)",
            conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@uid", id);
        cmd.Parameters.AddWithValue("@s",   dto.StartDate);
        cmd.Parameters.AddWithValue("@e",   dto.EndDate);
        cmd.Parameters.AddWithValue("@a",   dto.Amount);
        cmd.Parameters.AddWithValue("@n",   (object?)dto.Notes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();

        // Invalidate the mobile API's 5-min subscription cache so the Android
        // app sees the new plan on its very next search instead of after TTL.
        _ = mobileHttp.PostAsync($"api/mobile/cache/invalidate-sub/{id}", null);

        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapDelete("/api/mgr/subscriptions/{id:long}", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        // Look up which user this sub belongs to so we can invalidate their cache.
        long userId = 0;
        await using (var sel = new MySqlCommand(
            "SELECT user_id FROM subscriptions WHERE id=@id LIMIT 1", conn) { CommandTimeout = 5 })
        {
            sel.Parameters.AddWithValue("@id", id);
            var v = await sel.ExecuteScalarAsync();
            if (v != null && v != DBNull.Value) userId = Convert.ToInt64(v);
        }

        await MgrExec("DELETE FROM subscriptions WHERE id=@id", conn, 10, ("@id", id));

        if (userId > 0)
            _ = mobileHttp.PostAsync($"api/mobile/cache/invalidate-sub/{userId}", null);

        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Vehicle search (desktop) ───────────────────────────────────────────────

app.MapGet("/api/mgr/search", async (HttpContext ctx, string? q, string? mode) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(q)) return Results.Ok(new List<object>());
    try
    {
        var isChassis = string.Equals(mode, "chassis", StringComparison.OrdinalIgnoreCase);
        const string fields = @"
            vr.id, vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model,
            vr.agreement_no, vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
            vr.sec9_available, vr.sec17_available, vr.customer_name, vr.customer_address, vr.customer_contact,
            vr.region, vr.area, vr.branch_name_raw,
            vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
            vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
            vr.sender_mail1, vr.sender_mail2, vr.executive_name, vr.pos, vr.toss, vr.remark,
            COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y %h:%i %p'),'') AS created_on,
            b.name AS branch_name,
            COALESCE(f.name,'') AS financer,
            COALESCE(b.contact1,'') AS b_c1,
            COALESCE(b.contact2,'') AS b_c2,
            COALESCE(b.contact3,'') AS b_c3,
            COALESCE(b.address,'') AS b_addr";

        var sql = isChassis
            ? $@"SELECT {fields}
                 FROM chassis_info ci
                 INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
                 INNER JOIN branches b ON b.id = vr.branch_id
                 LEFT  JOIN finances f ON f.id = b.finance_id
                 WHERE ci.last5 = @q
                 ORDER BY b.name, vr.chassis_no"
            : $@"SELECT {fields}
                 FROM rc_info ri
                 INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
                 INNER JOIN branches b ON b.id = vr.branch_id
                 LEFT  JOIN finances f ON f.id = b.finance_id
                 WHERE ri.last4 = @q
                 ORDER BY b.name, vr.vehicle_no";

        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@q", q.ToUpper().Trim());
        await using var rdr = await cmd.ExecuteReaderAsync();

        var results = new List<object>();
        while (await rdr.ReadAsync())
        {
            string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
            results.Add(new
            {
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
        return Results.Ok(results);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Skinny search list ──────────────────────────────────────────────────────
// Returns ONLY what the result list + "found in finances" chooser need (id,
// vehicle/chassis no, model, branch, finance, date) — NOT the ~40 heavy columns.
// Shrinks a search response from ~300 KB to a few KB → instant even online. The
// full record is fetched on tap via /api/mgr/record/{id}.
app.MapGet("/api/mgr/search/list", async (HttpContext ctx, string? q, string? mode) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(q)) return Results.Ok(new List<object>());
    try
    {
        var isChassis = string.Equals(mode, "chassis", StringComparison.OrdinalIgnoreCase);
        const string lite = @"
            vr.id, vr.vehicle_no, vr.chassis_no, vr.model,
            b.name AS branch_name, COALESCE(f.name,'') AS financer,
            COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y %h:%i %p'),'') AS created_on";
        var sql = isChassis
            ? $@"SELECT {lite} FROM chassis_info ci
                 INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
                 INNER JOIN branches b ON b.id = vr.branch_id
                 LEFT  JOIN finances f ON f.id = b.finance_id
                 WHERE ci.last5 = @q ORDER BY b.name, vr.chassis_no"
            : $@"SELECT {lite} FROM rc_info ri
                 INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
                 INNER JOIN branches b ON b.id = vr.branch_id
                 LEFT  JOIN finances f ON f.id = b.finance_id
                 WHERE ri.last4 = @q ORDER BY b.name, vr.vehicle_no";

        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@q", q.ToUpper().Trim());
        await using var rdr = await cmd.ExecuteReaderAsync();
        var results = new List<object>();
        while (await rdr.ReadAsync())
        {
            string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
            results.Add(new
            {
                Id = rdr.GetInt64(0).ToString(),
                VehicleNo = S(1), ChassisNo = S(2), Model = S(3),
                BranchName = S(4), Financer = S(5), CreatedOn = S(6)
            });
        }
        return Results.Ok(results);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Full record detail by id (fetched only when a search result is opened) ──
app.MapGet("/api/mgr/record/{id:long}", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        const string fields = @"
            vr.id, vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model,
            vr.agreement_no, vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
            vr.sec9_available, vr.sec17_available, vr.customer_name, vr.customer_address, vr.customer_contact,
            vr.region, vr.area, vr.branch_name_raw,
            vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
            vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
            vr.sender_mail1, vr.sender_mail2, vr.executive_name, vr.pos, vr.toss, vr.remark,
            COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y %h:%i %p'),'') AS created_on,
            b.name AS branch_name,
            COALESCE(f.name,'') AS financer,
            COALESCE(b.contact1,'') AS b_c1,
            COALESCE(b.contact2,'') AS b_c2,
            COALESCE(b.contact3,'') AS b_c3,
            COALESCE(b.address,'') AS b_addr";
        var sql = $@"SELECT {fields}
                     FROM vehicle_records vr
                     INNER JOIN branches b ON b.id = vr.branch_id
                     LEFT  JOIN finances f ON f.id = b.finance_id
                     WHERE vr.id = @id LIMIT 1";
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return Results.NotFound();
        string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
        return Results.Ok(new
        {
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

// ── Dashboard quick stats ──────────────────────────────────────────────────

app.MapGet("/api/mgr/dashboard-stats", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        // totalRecords summed from the maintained branches.total_records counter
        // instead of COUNT(*) over the multi-million-row vehicle_records table
        // (~410 ms → sub-ms). CAST keeps it a BIGINT for GetInt64.
        const string sql = @"
            SELECT
                (SELECT CAST(COALESCE(SUM(total_records),0) AS SIGNED) FROM branches WHERE is_active=1),
                (SELECT COUNT(*) FROM finances),
                (SELECT COUNT(*) FROM branches WHERE is_active=1)";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return Results.Ok(new { totalRecords = 0L, totalFinances = 0, totalBranches = 0 });
        return Results.Ok(new
        {
            totalRecords  = rdr.IsDBNull(0) ? 0L : rdr.GetInt64(0),
            totalFinances = rdr.IsDBNull(1) ? 0  : rdr.GetInt32(1),
            totalBranches = rdr.IsDBNull(2) ? 0  : rdr.GetInt32(2),
        });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Device change requests ─────────────────────────────────────────────────

app.MapGet("/api/mgr/device-requests", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, user_id, user_name, user_mobile, new_device_id,
                   DATE_FORMAT(requested_at,'%d %b %H:%i') AS req_at
            FROM device_change_requests ORDER BY requested_at DESC LIMIT 100";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id          = rdr.GetInt64(0),
                userId      = rdr.GetInt64(1),
                userName    = rdr.GetString(2),
                userMobile  = rdr.GetString(3),
                newDeviceId = rdr.GetString(4),
                requestedAt = rdr.GetString(5),
            });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/device-requests/{id:long}/approve", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        long userId = 0; string newDev = "";
        await using (var sel = new MySqlCommand(
            "SELECT user_id, new_device_id FROM device_change_requests WHERE id=@id LIMIT 1", conn))
        {
            sel.Parameters.AddWithValue("@id", id);
            await using var rdr = await sel.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return Results.NotFound();
            userId = rdr.GetInt64(0);
            newDev = rdr.GetString(1);
        }
        await MgrExec("UPDATE app_users SET device_id=@d WHERE id=@uid", conn, 10,
            ("@d", newDev), ("@uid", userId));
        await MgrExec("DELETE FROM device_change_requests WHERE id=@id", conn, 10, ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapDelete("/api/mgr/device-requests/{id:long}", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("DELETE FROM device_change_requests WHERE id=@id", conn, 10, ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Live users (active in last 15 min) ────────────────────────────────────

app.MapGet("/api/mgr/live-users", async (HttpContext ctx, string? since) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        // since = "HH:mm" 24h — show all users seen after that time today
        // omitted = last 15 minutes
        string where;
        MySqlCommand cmd;
        if (!string.IsNullOrWhiteSpace(since) &&
            System.Text.RegularExpressions.Regex.IsMatch(since.Trim(), @"^\d{2}:\d{2}$"))
        {
            const string sql = @"
                SELECT u.id, u.name, u.mobile,
                       DATE_FORMAT(u.last_seen,'%H:%i • %d %b') AS seen,
                       COALESCE(NULLIF(u.last_lat,0), (SELECT sl.lat FROM search_logs sl WHERE sl.user_id=u.id AND sl.lat IS NOT NULL AND sl.lat<>0 ORDER BY sl.server_time DESC LIMIT 1)) AS lat,
                       COALESCE(NULLIF(u.last_lng,0), (SELECT sl.lng FROM search_logs sl WHERE sl.user_id=u.id AND sl.lng IS NOT NULL AND sl.lng<>0 ORDER BY sl.server_time DESC LIMIT 1)) AS lng,
                       COALESCE(u.pfp,'') AS pfp
                FROM app_users u
                WHERE u.last_seen >= CONCAT(CURDATE(), ' ', @since, ':00')
                ORDER BY u.last_seen DESC LIMIT 500";
            cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
            cmd.Parameters.AddWithValue("@since", since.Trim());
        }
        else
        {
            const string sql = @"
                SELECT u.id, u.name, u.mobile,
                       DATE_FORMAT(u.last_seen,'%H:%i • %d %b') AS seen,
                       COALESCE(NULLIF(u.last_lat,0), (SELECT sl.lat FROM search_logs sl WHERE sl.user_id=u.id AND sl.lat IS NOT NULL AND sl.lat<>0 ORDER BY sl.server_time DESC LIMIT 1)) AS lat,
                       COALESCE(NULLIF(u.last_lng,0), (SELECT sl.lng FROM search_logs sl WHERE sl.user_id=u.id AND sl.lng IS NOT NULL AND sl.lng<>0 ORDER BY sl.server_time DESC LIMIT 1)) AS lng,
                       COALESCE(u.pfp,'') AS pfp
                FROM app_users u
                WHERE u.last_seen >= NOW() - INTERVAL 15 MINUTE
                ORDER BY u.last_seen DESC LIMIT 200";
            cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        }

        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id       = rdr.GetInt64(0),
                name     = rdr.GetString(1),
                mobile   = rdr.GetString(2),
                lastSeen = rdr.GetString(3),
                lat      = rdr.IsDBNull(4) ? (double?)null : rdr.GetDouble(4),
                lng      = rdr.IsDBNull(5) ? (double?)null : rdr.GetDouble(5),
                pfp      = rdr.IsDBNull(6) ? "" : rdr.GetString(6),
            });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Search logs (vehicle views from mobile agents) ────────────────────────────
app.MapGet("/api/mgr/search-logs", async (HttpContext ctx,
    string? fromDate, string? toDate, long? userId, string? q, bool? export) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        var sql = @"
            SELECT sl.id, sl.user_id, u.name, u.mobile,
                   sl.vehicle_no, sl.chassis_no, sl.model,
                   sl.lat, sl.lng, sl.address,
                   COALESCE(u.address,'') AS user_address,
                   DATE_FORMAT(CONVERT_TZ(sl.device_time,'+00:00','+05:30'), '%Y-%m-%d %H:%i:%s') AS device_time,
                   DATE_FORMAT(sl.server_time, '%Y-%m-%d %H:%i:%s') AS server_time
            FROM search_logs sl
            JOIN app_users u ON u.id = sl.user_id
            WHERE 1=1";

        var cmd = new MySqlCommand();
        if (!string.IsNullOrWhiteSpace(fromDate))
        {
            sql += " AND DATE(sl.server_time) >= @fd";
            cmd.Parameters.AddWithValue("@fd", fromDate);
        }
        if (!string.IsNullOrWhiteSpace(toDate))
        {
            sql += " AND DATE(sl.server_time) <= @td";
            cmd.Parameters.AddWithValue("@td", toDate);
        }
        if (userId.HasValue)
        {
            sql += " AND sl.user_id = @uid";
            cmd.Parameters.AddWithValue("@uid", userId.Value);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += " AND (sl.vehicle_no LIKE @q OR sl.chassis_no LIKE @q)";
            cmd.Parameters.AddWithValue("@q", $"%{q.Trim()}%");
        }
        sql += export == true
            ? " ORDER BY sl.server_time DESC"
            : " ORDER BY sl.server_time DESC LIMIT 5000";

        cmd.CommandText    = sql;
        cmd.Connection     = conn;
        cmd.CommandTimeout = 15;

        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id          = rdr.GetInt64(0),
                userId      = rdr.GetInt64(1),
                userName    = rdr.GetString(2),
                userMobile  = rdr.GetString(3),
                vehicleNo   = rdr.GetString(4),
                chassisNo   = rdr.GetString(5),
                model       = rdr.GetString(6),
                lat         = rdr.IsDBNull(7)  ? (double?)null : rdr.GetDouble(7),
                lng         = rdr.IsDBNull(8)  ? (double?)null : rdr.GetDouble(8),
                address     = rdr.IsDBNull(9)  ? null : rdr.GetString(9),
                userAddress = rdr.GetString(10),
                deviceTime  = rdr.GetString(11),
                serverTime  = rdr.GetString(12)
            });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Column types & mappings (Excel column mapping config) ─────────────────────
app.MapGet("/api/mgr/column-mappings", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        var types = new List<object>();
        await using (var cmd = new MySqlCommand(
            "SELECT id, name FROM column_types ORDER BY sort_order, id", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                types.Add(new { id = r.GetInt32(0), name = r.GetString(1) });

        var maps = new List<object>();
        await using (var cmd = new MySqlCommand(
            "SELECT id, column_type_id, name FROM column_mappings ORDER BY column_type_id, name", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                maps.Add(new { id = r.GetInt32(0), columnTypeId = r.GetInt32(1), name = r.GetString(2) });

        return Results.Ok(new { columnTypes = types, mappings = maps });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/column-mappings", async (HttpContext ctx, MgrCreateMappingDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(dto.RawName, "[^A-Za-z0-9]", "").ToLower();
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO column_mappings (column_type_id, name) VALUES (@tid, @name); SELECT LAST_INSERT_ID();", conn);
        cmd.Parameters.AddWithValue("@tid",  dto.ColumnTypeId);
        cmd.Parameters.AddWithValue("@name", normalized);
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return Results.Ok(new { id, columnTypeId = dto.ColumnTypeId, name = normalized });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapDelete("/api/mgr/column-mappings/{id:int}", async (HttpContext ctx, int id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("DELETE FROM column_mappings WHERE id=@id", conn, 10, ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/column-types", async (HttpContext ctx, MgrCreateColumnTypeDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO column_types (name) VALUES (@name); SELECT LAST_INSERT_ID();", conn);
        cmd.Parameters.AddWithValue("@name", dto.Name.Trim());
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return Results.Ok(new { id, name = dto.Name.Trim() });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Bulk upload records from desktop app (streaming ndjson progress) ──────────
// Wire format in: gzip-compressed UTF-8 text — line 0 = branchId, rest = 32 pipe-delimited fields
// Wire format out: newline-delimited JSON  {"pct":N,"msg":"..."} … {"pct":100,"msg":"…","inserted":N,"elapsedSeconds":N}
app.MapPost("/api/mgr/records/upload", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();

    // Chunked-upload mode (for resilience on weak client networks the desktop
    // splits a big branch into several small POSTs, each retried on failure):
    //   replace (default / single-shot) → clear old records, insert, finalize
    //   begin  → clear old records, insert this chunk, DON'T finalize
    //   append → insert this chunk only (no clear, no finalize)
    //   finish → insert this chunk, then finalize (branch stats + index rebuild)
    // No param → "replace" → exactly the original one-shot behaviour.
    string mode     = (ctx.Request.Query["mode"].FirstOrDefault() ?? "replace").ToLowerInvariant();
    bool   doClear  = mode is "replace" or "begin";
    bool   doFinal  = mode is "replace" or "finish";

    // ── 1. Decompress + parse BEFORE touching the response ────────────────
    string text;
    try
    {
        await using var gz = new GZipStream(ctx.Request.Body, CompressionMode.Decompress);
        using var rdr = new System.IO.StreamReader(gz, System.Text.Encoding.UTF8);
        text = await rdr.ReadToEndAsync();
    }
    catch (Exception ex) { return Results.BadRequest(new { message = "Decompress failed: " + ex.Message }); }

    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    if (lines.Length < 2 || !int.TryParse(lines[0].Trim(), out int branchId) || branchId <= 0)
        return Results.BadRequest(new { message = "Invalid payload." });

    // ── 2. Switch to streaming ndjson ─────────────────────────────────────
    ctx.Response.StatusCode  = 200;
    ctx.Response.ContentType = "application/x-ndjson";
    ctx.Response.Headers["Cache-Control"]      = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"]  = "no";

    async Task Push(int pct, string msg, int? inserted = null, double? elapsed = null)
    {
        string line;
        if (inserted.HasValue)
            line = $"{{\"pct\":{pct},\"msg\":\"{msg}\",\"inserted\":{inserted.Value},\"elapsedSeconds\":{elapsed!.Value:F2}}}";
        else
            line = $"{{\"pct\":{pct},\"msg\":\"{msg}\"}}";
        await ctx.Response.WriteAsync(line + "\n");
        await ctx.Response.Body.FlushAsync();
    }

    try
    {
        // ── 3. Build DataTable ────────────────────────────────────────────
        var sw = Stopwatch.StartNew();   // covers entire upload, not just BulkCopy
        await Push(5, $"Parsing {lines.Length - 1:N0} records…");

        static string Tr(string v, int max) => v.Length > max ? v[..max] : v;

        var dt = new DataTable();
        dt.Columns.Add("branch_id",        typeof(int));
        dt.Columns.Add("vehicle_no",       typeof(string));
        dt.Columns.Add("chassis_no",       typeof(string));
        dt.Columns.Add("engine_no",        typeof(string));
        dt.Columns.Add("model",            typeof(string));
        dt.Columns.Add("agreement_no",     typeof(string));
        dt.Columns.Add("bucket",           typeof(string));
        dt.Columns.Add("gv",               typeof(string));
        dt.Columns.Add("od",               typeof(string));
        dt.Columns.Add("seasoning",        typeof(string));
        dt.Columns.Add("tbr_flag",         typeof(string));
        dt.Columns.Add("sec9_available",   typeof(string));
        dt.Columns.Add("sec17_available",  typeof(string));
        dt.Columns.Add("customer_name",    typeof(string));
        dt.Columns.Add("customer_address", typeof(string));
        dt.Columns.Add("customer_contact", typeof(string));
        dt.Columns.Add("region",           typeof(string));
        dt.Columns.Add("area",             typeof(string));
        dt.Columns.Add("branch_name_raw",  typeof(string));
        dt.Columns.Add("level1",           typeof(string));
        dt.Columns.Add("level1_contact",   typeof(string));
        dt.Columns.Add("level2",           typeof(string));
        dt.Columns.Add("level2_contact",   typeof(string));
        dt.Columns.Add("level3",           typeof(string));
        dt.Columns.Add("level3_contact",   typeof(string));
        dt.Columns.Add("level4",           typeof(string));
        dt.Columns.Add("level4_contact",   typeof(string));
        dt.Columns.Add("sender_mail1",     typeof(string));
        dt.Columns.Add("sender_mail2",     typeof(string));
        dt.Columns.Add("executive_name",   typeof(string));
        dt.Columns.Add("pos",              typeof(string));
        dt.Columns.Add("toss",             typeof(string));
        dt.Columns.Add("remark",           typeof(string));
        dt.MinimumCapacity = lines.Length - 1;
        dt.BeginLoadData();   // suppress per-row change events — faster for bulk population

        int skippedRows = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            var f = lines[i].TrimEnd('\r').Split('|', 32);
            if (f.Length < 32) { skippedRows++; continue; }
            dt.Rows.Add(
                branchId,
                Tr(f[0],50),  Tr(f[1],100), Tr(f[2],100), Tr(f[3],200),
                Tr(f[4],100), Tr(f[5],50),  Tr(f[6],50),  Tr(f[7],50),
                Tr(f[8],50),  Tr(f[9],20),  Tr(f[10],20), Tr(f[11],20),
                Tr(f[12],200),f[13],         Tr(f[14],100),
                Tr(f[15],100),Tr(f[16],100), Tr(f[17],200),
                Tr(f[18],200),Tr(f[19],100),
                Tr(f[20],200),Tr(f[21],100),
                Tr(f[22],200),Tr(f[23],100),
                Tr(f[24],200),Tr(f[25],100),
                Tr(f[26],200),Tr(f[27],200),
                Tr(f[28],200),Tr(f[29],100), Tr(f[30],100),
                f[31]);
        }
        dt.EndLoadData();
        int parsedRows = dt.Rows.Count;
        if (skippedRows > 0)
            Console.WriteLine($"[Upload] branch={branchId} WARNING: {skippedRows} rows had fewer than 32 fields and were skipped");

        // ── 4. Open DB, clear old records ─────────────────────────────────
        await Push(15, "Connecting to database…");
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("SET foreign_key_checks = 0", conn);
        await MgrExec("SET unique_checks = 0", conn);

        // Clear the branch's existing records only on the first (begin) or a
        // single-shot (replace) upload — append/finish chunks add to the set
        // the begin chunk already started.
        if (doClear)
        {
            await Push(20, "Checking existing records…");
            await using var existCmd = new MySqlCommand(
                "SELECT EXISTS(SELECT 1 FROM vehicle_records WHERE branch_id = @bid LIMIT 1)", conn);
            existCmd.Parameters.AddWithValue("@bid", branchId);
            if (Convert.ToInt64(await existCmd.ExecuteScalarAsync()) == 1)
            {
                await Push(25, "Clearing old records…");
                await MgrExec(@"
                    DELETE vr, ri, ci
                    FROM   vehicle_records vr
                    LEFT JOIN rc_info      ri ON ri.vehicle_record_id = vr.id
                    LEFT JOIN chassis_info ci ON ci.vehicle_record_id = vr.id
                    WHERE  vr.branch_id = @bid",
                    conn, 300, ("@bid", branchId));
            }
        }

        // ── 5. BulkCopy with real-time progress ──────────────────────────
        int totalRows = parsedRows;
        await Push(35, $"0 / {totalRows:N0}");

        var bc = new MySqlBulkCopy(conn)
        {
            DestinationTableName = "vehicle_records",
            BulkCopyTimeout      = 600,
            NotifyAfter          = Math.Max(1000, totalRows / 20)  // ~20 events regardless of size
        };
        for (int i = 0; i < dt.Columns.Count; i++)
            bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, dt.Columns[i].ColumnName));

        // MySqlRowsCopied fires on the BulkCopy thread — queue it, drain in the poll loop
        var copiedQueue = new System.Collections.Concurrent.ConcurrentQueue<long>();
        bc.MySqlRowsCopied += (_, e) => copiedQueue.Enqueue(e.RowsCopied);

        var bcTask = bc.WriteToServerAsync(dt);
        while (!bcTask.IsCompleted)
        {
            await Task.Delay(300);
            if (copiedQueue.TryDequeue(out long copied))
            {
                int pct = 35 + (int)((double)copied / totalRows * 50); // 35 → 85
                await Push(Math.Min(pct, 85), $"{copied:N0} / {totalRows:N0}");
            }
        }
        var bcResult = await bcTask;  // re-throws on failure
        dt.Dispose();
        long bcInserted = bcResult.RowsInserted;

        // ── 6. Verify actual DB count ─────────────────────────────────────
        await Push(88, "Verifying insert count…");
        long dbCount;
        await using (var verifCmd = new MySqlCommand(
            "SELECT COUNT(*) FROM vehicle_records WHERE branch_id = @bid", conn) { CommandTimeout = 30 })
        {
            verifCmd.Parameters.AddWithValue("@bid", branchId);
            dbCount = Convert.ToInt64(await verifCmd.ExecuteScalarAsync());
        }

        Console.WriteLine($"[Upload] branch={branchId} sent={totalRows} skipped={skippedRows} bcInserted={bcInserted} dbCount={dbCount}");

        // ── 7. Finalize — only on the last (finish) or single-shot (replace)
        //       chunk. Intermediate begin/append chunks skip the branch-stats
        //       write; the finish chunk records the authoritative total. ─────
        if (doFinal)
        {
            await Push(92, "Updating branch stats…");
            await MgrExec("UPDATE branches SET total_records=@cnt, uploaded_at=NOW() WHERE id=@bid",
                conn, 30, ("@cnt", dbCount), ("@bid", branchId));
        }
        // Re-enable per-connection checks for every chunk (they were disabled
        // at the top of THIS request's connection).
        await MgrExec("SET foreign_key_checks = 1", conn);
        await MgrExec("SET unique_checks = 1", conn);

        int inserted = (int)dbCount;
        await Push(100, $"Done", inserted, sw.Elapsed.TotalSeconds);

        // ── 8. Background index rebuild ───────────────────────────────────
        // Runs only after the final/single chunk, when every vehicle_record for
        // the branch is present — it rebuilds rc_info/chassis_info for the whole
        // branch in one pass, so running it per-chunk would be wasted work.
        // DELETE before INSERT makes each task idempotent: if a previous upload's
        // background task is still running when a new upload starts, the stale INSERT
        // targets the same vehicle_record rows and would double rc_info/chassis_info
        // (unique_checks=0 lets duplicates slip through). Deleting first ensures only
        // one set of rows exists regardless of overlapping executions.
        if (doFinal)
        _ = Task.WhenAll(
            Task.Run(async () =>
            {
                await using var c = new MySqlConnection(TenantContext.Conn);
                await c.OpenAsync();
                await MgrExec("SET foreign_key_checks=0", c); await MgrExec("SET unique_checks=0", c);
                await MgrExec(@"DELETE ri FROM rc_info ri
                    INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
                    WHERE vr.branch_id = @bid", c, 300, ("@bid", branchId));
                // last4 = the right-most 4-digit cluster (e.g. MH-12-AB-1234 → "1234",
                // HR-73-6546 → "6546", 22-BH-2271-E → "2271").
                // Only insert rows whose stripped vehicle_no is a recognised Indian RC
                // format — prevents junk like "AF1234" or bare digit strings from
                // polluting rc_info and showing up in searches.
                await MgrExec(@"INSERT INTO rc_info (vehicle_record_id,rc_number,model,last4)
                    SELECT id, vehicle_no, COALESCE(model,''),
                           LEFT(REGEXP_SUBSTR(vehicle_no,'[0-9]{4}[^0-9]*$'), 4)
                    FROM vehicle_records
                    WHERE branch_id=@bid
                      AND vehicle_no IS NOT NULL AND vehicle_no!=''
                      AND REGEXP_REPLACE(UPPER(vehicle_no),'[^A-Z0-9]','') REGEXP
                          '^([A-Z]{2}[0-9]{1,3}[A-Z]{1,3}[0-9]{4}|[A-Z]{2}[0-9]{5,7}|[0-9]{2}BH[0-9]{4}[A-Z]{1,2})$'",
                    c, 300, ("@bid", branchId));
                await MgrExec("SET foreign_key_checks=1", c); await MgrExec("SET unique_checks=1", c);
            }),
            Task.Run(async () =>
            {
                await using var c = new MySqlConnection(TenantContext.Conn);
                await c.OpenAsync();
                await MgrExec("SET foreign_key_checks=0", c); await MgrExec("SET unique_checks=0", c);
                await MgrExec(@"DELETE ci FROM chassis_info ci
                    INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
                    WHERE vr.branch_id = @bid", c, 300, ("@bid", branchId));
                await MgrExec(@"INSERT INTO chassis_info (vehicle_record_id,chassis_number,model,last5)
                    SELECT id,chassis_no,COALESCE(model,''),RIGHT(chassis_no,5)
                    FROM vehicle_records WHERE branch_id=@bid AND chassis_no IS NOT NULL AND chassis_no!=''",
                    c, 300, ("@bid", branchId));
                await MgrExec("SET foreign_key_checks=1", c); await MgrExec("SET unique_checks=1", c);
            }));
    }
    catch (Exception ex)
    {
        await ctx.Response.WriteAsync($"{{\"pct\":-1,\"msg\":\"{ex.Message.Replace("\"", "'")}\"}}\n");
        await ctx.Response.Body.FlushAsync();
    }

    // Response already written above; returning Ok() is a no-op once HasStarted=true
    return Results.Ok();
});

// ── Export endpoints ──────────────────────────────────────────────────────────

app.MapGet("/api/mgr/export/users", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        // Balance = sum of all subscription amounts ever bought for the user.
        // The legacy u.balance column is ignored.
        const string sql = @"
            SELECT u.id, u.name, u.mobile, u.address, u.pincode,
                   u.is_active, u.is_admin, u.is_stopped, u.is_blacklisted,
                   COALESCE((SELECT SUM(s.amount) FROM subscriptions s
                              WHERE s.user_id = u.id), 0) AS balance,
                   DATE_FORMAT(u.created_at,'%Y-%m-%d %H:%i:%s') AS created_at,
                   (SELECT DATE_FORMAT(MAX(s.end_date),'%Y-%m-%d')
                    FROM subscriptions s WHERE s.user_id = u.id) AS sub_end
            FROM app_users u
            ORDER BY u.id";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 60 };
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id             = rdr.GetInt64(0),
                name           = rdr.GetString(1),
                mobile         = rdr.GetString(2),
                address        = rdr.IsDBNull(3)  ? null : rdr.GetString(3),
                pincode        = rdr.IsDBNull(4)  ? null : rdr.GetString(4),
                isActive       = rdr.GetBoolean(5),
                isAdmin        = rdr.GetBoolean(6),
                isStopped      = rdr.GetBoolean(7),
                isBlacklisted  = rdr.GetBoolean(8),
                balance        = rdr.GetDecimal(9),
                createdAt      = rdr.GetString(10),
                subEnd         = rdr.IsDBNull(11) ? null : rdr.GetString(11),
            });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/export/subscriptions", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT s.id, s.user_id, u.name, u.mobile,
                   DATE_FORMAT(s.start_date,'%Y-%m-%d') AS start_date,
                   DATE_FORMAT(s.end_date,'%Y-%m-%d')   AS end_date,
                   s.amount, s.notes,
                   DATE_FORMAT(s.created_at,'%Y-%m-%d %H:%i:%s') AS created_at
            FROM subscriptions s
            JOIN app_users u ON u.id = s.user_id
            ORDER BY s.id";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 60 };
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id         = rdr.GetInt64(0),
                userId     = rdr.GetInt64(1),
                userName   = rdr.GetString(2),
                userMobile = rdr.GetString(3),
                startDate  = rdr.GetString(4),
                endDate    = rdr.GetString(5),
                amount     = rdr.GetDecimal(6),
                notes      = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                createdAt  = rdr.GetString(8),
            });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/export/vehicle-records", async (HttpContext ctx) =>
{
    int page = int.TryParse(ctx.Request.Query["page"], out var _p1) ? _p1 : 0;
    int size = int.TryParse(ctx.Request.Query["size"], out var _s1) ? _s1 : 5000;
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        long total;
        await using (var cntCmd = new MySqlCommand("SELECT COUNT(*) FROM vehicle_records", conn) { CommandTimeout = 30 })
            total = Convert.ToInt64(await cntCmd.ExecuteScalarAsync());

        const string fields = @"
            vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model, vr.agreement_no,
            vr.customer_name, vr.customer_contact, vr.customer_address,
            COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name,
            vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
            vr.sec9_available, vr.sec17_available,
            vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
            vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
            vr.sender_mail1, vr.sender_mail2, vr.executive_name,
            vr.pos, vr.toss, vr.remark, vr.region, vr.area,
            COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on";
        var sql = $@"SELECT {fields}
            FROM vehicle_records vr
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            ORDER BY vr.id
            LIMIT {size} OFFSET {page * size}";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 120 };
        var rows = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
        while (await rdr.ReadAsync())
            rows.Add(new
            {
                vehicleNo=S(0),chassisNo=S(1),engineNo=S(2),model=S(3),agreementNo=S(4),
                customerName=S(5),customerContact=S(6),customerAddress=S(7),
                financer=S(8),branchName=S(9),
                bucket=S(10),gv=S(11),od=S(12),seasoning=S(13),tbrFlag=S(14),
                sec9=S(15),sec17=S(16),
                level1=S(17),level1Contact=S(18),level2=S(19),level2Contact=S(20),
                level3=S(21),level3Contact=S(22),level4=S(23),level4Contact=S(24),
                senderMail1=S(25),senderMail2=S(26),executiveName=S(27),
                pos=S(28),toss=S(29),remark=S(30),region=S(31),area=S(32),createdOn=S(33)
            });
        return Results.Ok(new { total, page, size, hasMore = (page + 1) * size < total, rows });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/export/rc-records", async (HttpContext ctx) =>
{
    int page = int.TryParse(ctx.Request.Query["page"], out var _p2) ? _p2 : 0;
    int size = int.TryParse(ctx.Request.Query["size"], out var _s2) ? _s2 : 5000;
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        long total;
        await using (var cntCmd = new MySqlCommand("SELECT COUNT(*) FROM rc_info", conn) { CommandTimeout = 30 })
            total = Convert.ToInt64(await cntCmd.ExecuteScalarAsync());

        // The RC export pulls the RC number from rc_info (the dedicated
        // lookup table) — that's the whole point of having this endpoint
        // separate from /export/vehicle-records.
        const string fields = @"
            ri.rc_number AS vehicle_no, vr.chassis_no, vr.engine_no, ri.model, vr.agreement_no,
            vr.customer_name, vr.customer_contact, vr.customer_address,
            COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name,
            vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
            vr.sec9_available, vr.sec17_available,
            vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
            vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
            vr.sender_mail1, vr.sender_mail2, vr.executive_name,
            vr.pos, vr.toss, vr.remark, vr.region, vr.area,
            COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on";
        var sql = $@"SELECT {fields}
            FROM rc_info ri
            INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            ORDER BY ri.id
            LIMIT {size} OFFSET {page * size}";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 120 };
        var rows = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
        while (await rdr.ReadAsync())
            rows.Add(new
            {
                vehicleNo=S(0),chassisNo=S(1),engineNo=S(2),model=S(3),agreementNo=S(4),
                customerName=S(5),customerContact=S(6),customerAddress=S(7),
                financer=S(8),branchName=S(9),
                bucket=S(10),gv=S(11),od=S(12),seasoning=S(13),tbrFlag=S(14),
                sec9=S(15),sec17=S(16),
                level1=S(17),level1Contact=S(18),level2=S(19),level2Contact=S(20),
                level3=S(21),level3Contact=S(22),level4=S(23),level4Contact=S(24),
                senderMail1=S(25),senderMail2=S(26),executiveName=S(27),
                pos=S(28),toss=S(29),remark=S(30),region=S(31),area=S(32),createdOn=S(33)
            });
        return Results.Ok(new { total, page, size, hasMore = (page + 1) * size < total, rows });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/export/chassis-records", async (HttpContext ctx) =>
{
    int page = int.TryParse(ctx.Request.Query["page"], out var _p3) ? _p3 : 0;
    int size = int.TryParse(ctx.Request.Query["size"], out var _s3) ? _s3 : 5000;
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        long total;
        await using (var cntCmd = new MySqlCommand("SELECT COUNT(*) FROM chassis_info", conn) { CommandTimeout = 30 })
            total = Convert.ToInt64(await cntCmd.ExecuteScalarAsync());

        // The chassis export pulls the chassis number from chassis_info (the
        // dedicated lookup table) — separate from /export/vehicle-records on
        // purpose.
        const string fields = @"
            vr.vehicle_no, ci.chassis_number AS chassis_no, vr.engine_no, ci.model, vr.agreement_no,
            vr.customer_name, vr.customer_contact, vr.customer_address,
            COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name,
            vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
            vr.sec9_available, vr.sec17_available,
            vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
            vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
            vr.sender_mail1, vr.sender_mail2, vr.executive_name,
            vr.pos, vr.toss, vr.remark, vr.region, vr.area,
            COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on";
        var sql = $@"SELECT {fields}
            FROM chassis_info ci
            INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            ORDER BY ci.id
            LIMIT {size} OFFSET {page * size}";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 120 };
        var rows = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
        while (await rdr.ReadAsync())
            rows.Add(new
            {
                vehicleNo=S(0),chassisNo=S(1),engineNo=S(2),model=S(3),agreementNo=S(4),
                customerName=S(5),customerContact=S(6),customerAddress=S(7),
                financer=S(8),branchName=S(9),
                bucket=S(10),gv=S(11),od=S(12),seasoning=S(13),tbrFlag=S(14),
                sec9=S(15),sec17=S(16),
                level1=S(17),level1Contact=S(18),level2=S(19),level2Contact=S(20),
                level3=S(21),level3Contact=S(22),level4=S(23),level4Contact=S(24),
                senderMail1=S(25),senderMail2=S(26),executiveName=S(27),
                pos=S(28),toss=S(29),remark=S(30),region=S(31),area=S(32),createdOn=S(33)
            });
        return Results.Ok(new { total, page, size, hasMore = (page + 1) * size < total, rows });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Per-branch export — replaces the desktop's old direct-MySQL ExportRepository
app.MapGet("/api/mgr/export/branch-records", async (HttpContext ctx) =>
{
    int branchId = int.TryParse(ctx.Request.Query["branchId"], out var _bid) ? _bid : 0;
    int page = int.TryParse(ctx.Request.Query["page"], out var _pb) ? _pb : 0;
    int size = int.TryParse(ctx.Request.Query["size"], out var _sb) ? _sb : 5000;
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (branchId <= 0) return Results.BadRequest(new { message = "branchId required" });
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        long total;
        await using (var cntCmd = new MySqlCommand(
            "SELECT COUNT(*) FROM vehicle_records WHERE branch_id = @bid", conn) { CommandTimeout = 30 })
        {
            cntCmd.Parameters.AddWithValue("@bid", branchId);
            total = Convert.ToInt64(await cntCmd.ExecuteScalarAsync());
        }

        // Branch export = vehicle records for one branch. All fields come
        // straight from vehicle_records. RC/chassis lookup tables are reserved
        // for the dedicated /export/rc-records and /export/chassis-records.
        const string fields = @"
            vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model, vr.agreement_no,
            vr.customer_name, vr.customer_contact, vr.customer_address,
            COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name,
            vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
            vr.sec9_available, vr.sec17_available,
            vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
            vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
            vr.sender_mail1, vr.sender_mail2, vr.executive_name,
            vr.pos, vr.toss, vr.remark, vr.region, vr.area,
            COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on";
        // ORDER BY vr.id (primary key) instead of vr.vehicle_no — vehicle_no
        // isn't always indexed and the sort was forcing a full filesort of
        // millions of rows on every page fetch. PK ordering is stable, fast,
        // and OFFSET pagination on it is O(log N + size) instead of O(N).
        var sql = $@"SELECT {fields}
            FROM vehicle_records vr
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE vr.branch_id = @bid
            ORDER BY vr.id
            LIMIT {size} OFFSET {page * size}";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@bid", branchId);
        var rows = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
        while (await rdr.ReadAsync())
            rows.Add(new
            {
                vehicleNo=S(0),chassisNo=S(1),engineNo=S(2),model=S(3),agreementNo=S(4),
                customerName=S(5),customerContact=S(6),customerAddress=S(7),
                financer=S(8),branchName=S(9),
                bucket=S(10),gv=S(11),od=S(12),seasoning=S(13),tbrFlag=S(14),
                sec9=S(15),sec17=S(16),
                level1=S(17),level1Contact=S(18),level2=S(19),level2Contact=S(20),
                level3=S(21),level3Contact=S(22),level4=S(23),level4Contact=S(24),
                senderMail1=S(25),senderMail2=S(26),executiveName=S(27),
                pos=S(28),toss=S(29),remark=S(30),region=S(31),area=S(32),createdOn=S(33)
            });
        return Results.Ok(new { total, page, size, hasMore = (page + 1) * size < total, rows });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Per-finance export — every branch under one finance
app.MapGet("/api/mgr/export/finance-records", async (HttpContext ctx) =>
{
    int financeId = int.TryParse(ctx.Request.Query["financeId"], out var _fid) ? _fid : 0;
    int page = int.TryParse(ctx.Request.Query["page"], out var _pf) ? _pf : 0;
    int size = int.TryParse(ctx.Request.Query["size"], out var _sf) ? _sf : 5000;
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (financeId <= 0) return Results.BadRequest(new { message = "financeId required" });
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        long total;
        await using (var cntCmd = new MySqlCommand(
            @"SELECT COUNT(*) FROM vehicle_records vr
              INNER JOIN branches b ON b.id = vr.branch_id
              WHERE b.finance_id = @fid", conn) { CommandTimeout = 30 })
        {
            cntCmd.Parameters.AddWithValue("@fid", financeId);
            total = Convert.ToInt64(await cntCmd.ExecuteScalarAsync());
        }

        // Finance export = vehicle records across all branches of one finance.
        // All fields straight from vehicle_records — see /export/branch-records.
        const string fields = @"
            vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model, vr.agreement_no,
            vr.customer_name, vr.customer_contact, vr.customer_address,
            COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name,
            vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
            vr.sec9_available, vr.sec17_available,
            vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
            vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
            vr.sender_mail1, vr.sender_mail2, vr.executive_name,
            vr.pos, vr.toss, vr.remark, vr.region, vr.area,
            COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on";
        // Same perf rationale as /export/branch-records: ORDER BY vr.id (PK)
        // makes large-OFFSET pagination near-instant. The legacy
        // ORDER BY b.name, vr.vehicle_no was forcing a full filesort across
        // every branch under the finance on every page fetch.
        var sql = $@"SELECT {fields}
            FROM vehicle_records vr
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE b.finance_id = @fid
            ORDER BY vr.id
            LIMIT {size} OFFSET {page * size}";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@fid", financeId);
        var rows = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        string S(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i);
        while (await rdr.ReadAsync())
            rows.Add(new
            {
                vehicleNo=S(0),chassisNo=S(1),engineNo=S(2),model=S(3),agreementNo=S(4),
                customerName=S(5),customerContact=S(6),customerAddress=S(7),
                financer=S(8),branchName=S(9),
                bucket=S(10),gv=S(11),od=S(12),seasoning=S(13),tbrFlag=S(14),
                sec9=S(15),sec17=S(16),
                level1=S(17),level1Contact=S(18),level2=S(19),level2Contact=S(20),
                level3=S(21),level3Contact=S(22),level4=S(23),level4Contact=S(24),
                senderMail1=S(25),senderMail2=S(26),executiveName=S(27),
                pos=S(28),toss=S(29),remark=S(30),region=S(31),area=S(32),createdOn=S(33)
            });
        return Results.Ok(new { total, page, size, hasMore = (page + 1) * size < total, rows });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Instant streaming .xlsx exports ─────────────────────────────────────────
// These generate the Excel file ON THE SERVER and stream it straight to the
// client as a download. The desktop just saves the bytes — no paginated JSON
// fetch, no client-side workbook assembly. A MySqlDataReader feeds rows
// directly into a zipped OpenXML worksheet (XlsxStream), so even a few hundred
// thousand records use almost no memory and start downloading immediately.
//
// Shared 34-column schema — must match XLSX_HEADERS order below.
const string XLSX_FIELDS = @"
    vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model, vr.agreement_no,
    vr.customer_name, vr.customer_contact, vr.customer_address,
    COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name,
    vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
    vr.sec9_available, vr.sec17_available,
    vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
    vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
    vr.sender_mail1, vr.sender_mail2, vr.executive_name,
    vr.pos, vr.toss, vr.remark, vr.region, vr.area,
    COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on";

string[] XLSX_HEADERS = {
    "Vehicle No","Chassis No","Engine No","Model","Agreement No",
    "Customer Name","Customer Contact","Customer Address",
    "Finance Name","Branch Name","Bucket","GV","OD","Seasoning",
    "TBR Flag","Sec9 Available","Sec17 Available",
    "Level1","Level1 Contact","Level2","Level2 Contact",
    "Level3","Level3 Contact","Level4","Level4 Contact",
    "Sender Mail 1","Sender Mail 2","Executive Name",
    "POS","TOSS","Remark","Region","Area","Created On" };

// Token via header OR ?key= so a plain browser/HttpClient download works.
static bool MgrAuthFlexible(HttpContext ctx, string key) =>
    (ctx.Request.Headers.TryGetValue("X-Api-Key", out var v) && v == key) ||
    (ctx.Request.Query.TryGetValue("key", out var q) && q == key);

async Task StreamVehicleXlsx(HttpContext ctx, string whereSql, string sheetName,
                             string downloadName, long offset, int limit, params (string, object)[] ps)
{
    // ZipArchive + StreamWriter do synchronous writes to the response body,
    // which Kestrel blocks by default ("Synchronous operations are
    // disallowed"). Opt this one response into sync IO — required for the
    // streamed-zip writer and standard practice for file-generation endpoints.
    var bodyControl = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
    if (bodyControl != null) bodyControl.AllowSynchronousIO = true;

    await using var conn = new MySqlConnection(TenantContext.Conn);
    await conn.OpenAsync();
    // When limit > 0 we stream just one slice (used for the "parts" export:
    // each part is its own file of `limit` rows starting at `offset`). When
    // limit <= 0 we stream the whole result set in one file.
    var slice = limit > 0 ? $" LIMIT {limit} OFFSET {offset}" : "";
    var sql = $"SELECT {XLSX_FIELDS} FROM vehicle_records vr " +
              "INNER JOIN branches b ON b.id = vr.branch_id " +
              "LEFT JOIN finances f ON f.id = b.finance_id " +
              $"WHERE {whereSql} ORDER BY vr.id{slice}";
    await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 600 };
    foreach (var (n, val) in ps) cmd.Parameters.AddWithValue(n, val);

    ctx.Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{downloadName}\"";

    await using var rdr = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
    await XlsxStream.WriteAsync(ctx.Response.Body, sheetName, XLSX_HEADERS, rdr,
                               XLSX_HEADERS.Length, ctx.RequestAborted);
}

// offset/limit are optional: omit for one full file, or pass them for a
// single "part" (e.g. offset=0&limit=100000 = first 1-lakh part).
app.MapGet("/api/mgr/export/finance-records.xlsx", async (HttpContext ctx) =>
{
    if (!MgrAuthFlexible(ctx, desktopLoginPassword)) { ctx.Response.StatusCode = 401; return; }
    int fid = int.TryParse(ctx.Request.Query["financeId"], out var f) ? f : 0;
    if (fid <= 0) { ctx.Response.StatusCode = 400; return; }
    var name = (ctx.Request.Query["name"].FirstOrDefault() ?? $"finance_{fid}");
    long offset = long.TryParse(ctx.Request.Query["offset"], out var o) ? o : 0;
    int  limit  = int.TryParse(ctx.Request.Query["limit"], out var l) ? l : 0;
    try
    {
        await StreamVehicleXlsx(ctx, "b.finance_id = @fid", name,
            $"{name}.xlsx", offset, limit, ("@fid", fid));
    }
    catch (Exception ex) when (!ctx.Response.HasStarted)
    { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsync(ex.Message); }
});

app.MapGet("/api/mgr/export/branch-records.xlsx", async (HttpContext ctx) =>
{
    if (!MgrAuthFlexible(ctx, desktopLoginPassword)) { ctx.Response.StatusCode = 401; return; }
    int bid = int.TryParse(ctx.Request.Query["branchId"], out var b) ? b : 0;
    if (bid <= 0) { ctx.Response.StatusCode = 400; return; }
    var name = (ctx.Request.Query["name"].FirstOrDefault() ?? $"branch_{bid}");
    long offset = long.TryParse(ctx.Request.Query["offset"], out var o) ? o : 0;
    int  limit  = int.TryParse(ctx.Request.Query["limit"], out var l) ? l : 0;
    try
    {
        await StreamVehicleXlsx(ctx, "vr.branch_id = @bid", name,
            $"{name}.xlsx", offset, limit, ("@bid", bid));
    }
    catch (Exception ex) when (!ctx.Response.HasStarted)
    { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsync(ex.Message); }
});

// ── Generic streamed-xlsx exports (Reports records + Search Logs) ───────────
// Same instant server-stream approach as Finances: build the .xlsx from a
// DataReader and stream it. Reports' vehicle/RC/chassis exports get
// offset/limit (so the parts dialog works); Search Logs streams the whole
// filtered set.
async Task StreamSelectXlsx(HttpContext ctx, string sql, string sheetName,
                            string downloadName, string[] headers, params (string, object)[] ps)
{
    var bc = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
    if (bc != null) bc.AllowSynchronousIO = true;
    await using var conn = new MySqlConnection(TenantContext.Conn);
    await conn.OpenAsync();
    await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 600 };
    foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
    ctx.Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{downloadName}\"";
    await using var rdr = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
    await XlsxStream.WriteAsync(ctx.Response.Body, sheetName, headers, rdr, headers.Length, ctx.RequestAborted);
}

// Vehicle / RC / Chassis global record exports (offset/limit = one part).
foreach (var spec in new[]
{
    ("vehicle-records", "Vehicle Records", $"SELECT {XLSX_FIELDS} FROM vehicle_records vr INNER JOIN branches b ON b.id=vr.branch_id LEFT JOIN finances f ON f.id=b.finance_id ORDER BY vr.id"),
    ("rc-records",      "RC Records",      $"SELECT ri.rc_number AS vehicle_no, vr.chassis_no, vr.engine_no, ri.model, vr.agreement_no, vr.customer_name, vr.customer_contact, vr.customer_address, COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name, vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag, vr.sec9_available, vr.sec17_available, vr.level1, vr.level1_contact, vr.level2, vr.level2_contact, vr.level3, vr.level3_contact, vr.level4, vr.level4_contact, vr.sender_mail1, vr.sender_mail2, vr.executive_name, vr.pos, vr.toss, vr.remark, vr.region, vr.area, COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on FROM rc_info ri INNER JOIN vehicle_records vr ON vr.id=ri.vehicle_record_id INNER JOIN branches b ON b.id=vr.branch_id LEFT JOIN finances f ON f.id=b.finance_id ORDER BY ri.id"),
    ("chassis-records", "Chassis Records", $"SELECT vr.vehicle_no, ci.chassis_number AS chassis_no, vr.engine_no, ci.model, vr.agreement_no, vr.customer_name, vr.customer_contact, vr.customer_address, COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name, vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag, vr.sec9_available, vr.sec17_available, vr.level1, vr.level1_contact, vr.level2, vr.level2_contact, vr.level3, vr.level3_contact, vr.level4, vr.level4_contact, vr.sender_mail1, vr.sender_mail2, vr.executive_name, vr.pos, vr.toss, vr.remark, vr.region, vr.area, COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on FROM chassis_info ci INNER JOIN vehicle_records vr ON vr.id=ci.vehicle_record_id INNER JOIN branches b ON b.id=vr.branch_id LEFT JOIN finances f ON f.id=b.finance_id ORDER BY ci.id"),
})
{
    var (route, sheet, baseSql) = spec;
    app.MapGet($"/api/mgr/export/{route}.xlsx", async (HttpContext ctx) =>
    {
        if (!MgrAuthFlexible(ctx, desktopLoginPassword)) { ctx.Response.StatusCode = 401; return; }
        long offset = long.TryParse(ctx.Request.Query["offset"], out var o) ? o : 0;
        int  limit  = int.TryParse(ctx.Request.Query["limit"], out var l) ? l : 0;
        var name = (ctx.Request.Query["name"].FirstOrDefault() ?? sheet.Replace(' ', '_'));
        var sql  = baseSql + (limit > 0 ? $" LIMIT {limit} OFFSET {offset}" : "");
        try { await StreamSelectXlsx(ctx, sql, sheet, $"{name}.xlsx", XLSX_HEADERS); }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsync(ex.Message); }
    });
}

// Search Logs — streamed xlsx with the same filters as /api/mgr/search-logs.
string[] SEARCHLOG_HEADERS = { "User", "Mobile", "Vehicle No", "Chassis No", "Model", "Search Location", "User Address", "Device Time", "Server Time" };
app.MapGet("/api/mgr/search-logs.xlsx", async (HttpContext ctx) =>
{
    if (!MgrAuthFlexible(ctx, desktopLoginPassword)) { ctx.Response.StatusCode = 401; return; }
    var fromDate = ctx.Request.Query["fromDate"].FirstOrDefault();
    var toDate   = ctx.Request.Query["toDate"].FirstOrDefault();
    var userIdQ  = ctx.Request.Query["userId"].FirstOrDefault();
    var q        = ctx.Request.Query["q"].FirstOrDefault();

    var where = new System.Text.StringBuilder(" WHERE 1=1");
    var ps    = new List<(string, object)>();
    if (!string.IsNullOrWhiteSpace(fromDate)) { where.Append(" AND DATE(sl.server_time) >= @fd"); ps.Add(("@fd", fromDate)); }
    if (!string.IsNullOrWhiteSpace(toDate))   { where.Append(" AND DATE(sl.server_time) <= @td"); ps.Add(("@td", toDate)); }
    if (long.TryParse(userIdQ, out var uid))  { where.Append(" AND sl.user_id = @uid"); ps.Add(("@uid", uid)); }
    if (!string.IsNullOrWhiteSpace(q))        { where.Append(" AND (sl.vehicle_no LIKE @q OR sl.chassis_no LIKE @q)"); ps.Add(("@q", $"%{q.Trim()}%")); }

    var sql = @"SELECT u.name, u.mobile, sl.vehicle_no, sl.chassis_no, sl.model,
                       COALESCE(sl.address,''), COALESCE(u.address,''),
                       DATE_FORMAT(CONVERT_TZ(sl.device_time,'+00:00','+05:30'),'%Y-%m-%d %H:%i:%s'),
                       DATE_FORMAT(sl.server_time,'%Y-%m-%d %H:%i:%s')
                FROM search_logs sl JOIN app_users u ON u.id = sl.user_id"
              + where + " ORDER BY sl.server_time DESC";
    try { await StreamSelectXlsx(ctx, sql, "Search Logs", $"SearchLogs_{DateTime.Now:yyyyMMdd}.xlsx", SEARCHLOG_HEADERS, ps.ToArray()); }
    catch (Exception ex) when (!ctx.Response.HasStarted)
    { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsync(ex.Message); }
});

// ── Forward /api/mobile/* → VKmobileapi on port 5001 ─────────────────────────
app.Map("/api/mobile/{**rest}", async (HttpContext ctx) =>
{
    var target = (ctx.Request.Path.Value ?? "/") + ctx.Request.QueryString.Value;
    var req    = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), target);

    foreach (var (k, v) in ctx.Request.Headers)
    {
        if (k.Equals("Host",              StringComparison.OrdinalIgnoreCase)) continue;
        if (k.Equals("Content-Length",    StringComparison.OrdinalIgnoreCase)) continue;
        if (k.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
        req.Headers.TryAddWithoutValidation(k, (IEnumerable<string?>)v);
    }

    if (ctx.Request.ContentLength > 0)
    {
        req.Content = new StreamContent(ctx.Request.Body);
        if (ctx.Request.ContentType is { } ct)
            req.Content.Headers.TryAddWithoutValidation("Content-Type", ct);
    }

    using var resp = await mobileHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
    ctx.Response.StatusCode = (int)resp.StatusCode;

    foreach (var (k, v) in resp.Headers)
        ctx.Response.Headers[k] = v.ToArray();
    foreach (var (k, v) in resp.Content.Headers)
        if (!k.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            ctx.Response.Headers[k] = v.ToArray();
    ctx.Response.Headers.Remove("Transfer-Encoding");

    await resp.Content.CopyToAsync(ctx.Response.Body);
});

// ── Client download page ─────────────────────────────────────────────────────
// deploy.sh creates /opt/vkapi/downloads and copies the installer there.
// Clients visit: https://api.characterverse.tech/download
var downloadsPath = Path.Combine(app.Environment.ContentRootPath, "downloads");
if (Directory.Exists(downloadsPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(downloadsPath),
        RequestPath  = "/downloads",
        ServeUnknownFileTypes = true
    });
    app.MapGet("/download", () => Results.Redirect("/downloads/index.html"));
}

// ── Agency uploads (logos) — served as /agency-uploads/ static files ─────────
// Defensive: never let a missing/unwritable folder crash startup.
var agencyUploads = "/opt/vkapi/agency-uploads";
try { Directory.CreateDirectory(agencyUploads); } catch { /* deploy.sh creates it */ }
if (Directory.Exists(agencyUploads))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(agencyUploads),
        RequestPath  = "/agency-uploads",
        ServeUnknownFileTypes = true
    });
}

// ── Public assets (map_live.html, etc.) — served as /public/ ────────────────
// Bundled with the deploy in VKApiServer/public/. Used e.g. by the WPF
// admin "live map" feature which opens /public/map_live.html in a browser.
var publicPath = Path.Combine(app.Environment.ContentRootPath, "public");
if (Directory.Exists(publicPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(publicPath),
        RequestPath  = "/public",
        ServeUnknownFileTypes = true
    });
}

// ── Mobile uploads (PFP + KYC) — served as /uploads/ ────────────────────────
// VKmobileapi writes app_user PFP photos and KYC scans to
// /opt/vkmobileapi/uploads/. The mobile API's own MobileController.AbsUrl()
// builds public URLs like https://api.crmrecoverysoftware.com/uploads/pfp/
// user_10.jpg. Without this static-file mapping every "My Account" image
// in the mobile app and every KYC thumbnail in the WPF admin returned 404.
const string mobileUploadsPath = "/opt/vkmobileapi/uploads";
if (Directory.Exists(mobileUploadsPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(mobileUploadsPath),
        RequestPath  = "/uploads",
        ServeUnknownFileTypes = true
    });
}

// ── Webhook endpoints (/api/webhooks/*) ──────────────────────────────────────
// Banks POST vehicle data to /api/webhooks/provider/HDB with:
//   Authorization: Basic base64(username:password)
//   X-Agency-Slug: <agency-slug>
//   Body: { fileInfo: { fileName, vehicleType, uploadedBy, uploadDate, bankName, fileGUID? },
//           data: [ { col1: val1, ... }, ... ] }
// Desktop reads files via /api/webhooks/files (MgrAuth).

var webhookFilesRoot = Path.Combine(app.Environment.ContentRootPath, "webhook-files");
Directory.CreateDirectory(webhookFilesRoot);

app.MapPost("/api/webhooks/provider/HDB", async (HttpContext ctx) =>
{
    // ── 1. Decode Basic auth ──────────────────────────────────────────────
    var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault() ?? "";
    if (!authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();
    string username, password;
    try
    {
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authHeader[6..].Trim()));
        var colon = decoded.IndexOf(':');
        if (colon < 0) return Results.Unauthorized();
        username = decoded[..colon];
        password = decoded[(colon + 1)..];
    }
    catch { return Results.Unauthorized(); }

    // ── 2. Resolve agency DB ──────────────────────────────────────────────
    var slug = ctx.Request.Headers["X-Agency-Slug"].FirstOrDefault()?.Trim().ToLowerInvariant();
    if (string.IsNullOrEmpty(slug))
        return Results.BadRequest(new { message = "X-Agency-Slug header required" });
    var tenantConn = TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug);

    // ── 3. Verify credentials against webhook_users ───────────────────────
    // Supports both bcrypt hashes (imported from legacy Node.js system, start with $2a$/$2b$)
    // and SHA-256 hex (new credentials created via the desktop app).
    try
    {
        await using var authConn = new MySqlConnection(tenantConn);
        await authConn.OpenAsync();
        await using var authCmd = new MySqlCommand(
            "SELECT password_hash FROM webhook_users WHERE username=@u LIMIT 1", authConn);
        authCmd.Parameters.AddWithValue("@u", username);
        var storedHash = await authCmd.ExecuteScalarAsync() as string;
        if (storedHash == null) return Results.Unauthorized();

        bool valid;
        if (storedHash.StartsWith("$2a$") || storedHash.StartsWith("$2b$"))
            valid = BCrypt.Net.BCrypt.Verify(password, storedHash);
        else
        {
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
            valid = sha == storedHash;
        }
        if (!valid) return Results.Unauthorized();
    }
    catch { return Results.Problem("DB error during auth"); }

    // ── 4. Parse body ─────────────────────────────────────────────────────
    WebhookProviderRequest? body;
    try { body = await ctx.Request.ReadFromJsonAsync<WebhookProviderRequest>(); }
    catch { return Results.BadRequest(new { message = "Invalid JSON body" }); }
    if (body == null || body.FileInfo == null || body.Data == null || body.Data.Count == 0)
        return Results.BadRequest(new { message = "fileInfo and data[] required" });
    var fi = body.FileInfo;

    // ── 5. Write CSV ──────────────────────────────────────────────────────
    var safeSlug    = System.Text.RegularExpressions.Regex.Replace(slug, "[^a-z0-9_-]", "");
    var slotDir     = Path.Combine(webhookFilesRoot, safeSlug);
    Directory.CreateDirectory(slotDir);
    var csvName     = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{(fi.FileName ?? "data").Replace(" ", "_")}.csv";
    var csvPath     = Path.Combine(slotDir, csvName);
    var relPath     = Path.Combine("webhook-files", safeSlug, csvName);
    var fields      = body.Data[0].Keys.ToList();
    var totalRows   = body.Data.Count;
    try
    {
        await using var sw = new StreamWriter(csvPath, false, System.Text.Encoding.UTF8);
        await sw.WriteLineAsync(string.Join(",", fields.Select(f => $"\"{f.Replace("\"", "\"\"")}\"")));
        foreach (var row in body.Data)
        {
            var cells = fields.Select(f =>
            {
                var v = row.TryGetValue(f, out var val) ? val?.ToString() ?? "" : "";
                return $"\"{v.Replace("\"", "\"\"")}\"";
            });
            await sw.WriteLineAsync(string.Join(",", cells));
        }
    }
    catch (Exception ex) { return Results.Problem($"CSV write failed: {ex.Message}"); }

    // ── 6. Upsert bank + insert file record ───────────────────────────────
    try
    {
        await using var conn = new MySqlConnection(tenantConn);
        await conn.OpenAsync();

        var bankName = (fi.BankName ?? "UNKNOWN").Trim().ToUpperInvariant();
        await using var bankCmd = new MySqlCommand(@"
            INSERT INTO webhook_banks (bank_name) VALUES (@n)
            ON DUPLICATE KEY UPDATE bank_name=bank_name;
            SELECT id FROM webhook_banks WHERE bank_name=@n LIMIT 1;", conn);
        bankCmd.Parameters.AddWithValue("@n", bankName);
        var bankId = Convert.ToInt32(await bankCmd.ExecuteScalarAsync());

        await using var fileCmd = new MySqlCommand(@"
            INSERT INTO webhook_files
                (bank_id, file_name, file_path, vehicle_type, uploaded_by, uploaded_date, file_guid, total_records)
            VALUES (@bid, @fn, @fp, @vt, @ub, @ud, @fg, @tr)", conn);
        fileCmd.Parameters.AddWithValue("@bid", bankId);
        fileCmd.Parameters.AddWithValue("@fn",  fi.FileName    ?? "");
        fileCmd.Parameters.AddWithValue("@fp",  relPath);
        fileCmd.Parameters.AddWithValue("@vt",  fi.VehicleType ?? "");
        fileCmd.Parameters.AddWithValue("@ub",  fi.UploadedBy  ?? "");
        fileCmd.Parameters.AddWithValue("@ud",  fi.UploadDate  ?? "");
        fileCmd.Parameters.AddWithValue("@fg",  fi.FileGUID    ?? (object)DBNull.Value);
        fileCmd.Parameters.AddWithValue("@tr",  totalRows);
        await fileCmd.ExecuteNonQueryAsync();
    }
    catch (Exception ex) { return Results.Problem($"DB insert failed: {ex.Message}"); }

    return Results.Ok(new { message = "Data successfully uploaded", records = totalRows });
});

// List webhook files — for the Desktop "Direct Data" page
app.MapGet("/api/webhooks/files", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"
            SELECT wf.id, wb.bank_name, wf.file_name, wf.vehicle_type,
                   wf.uploaded_by, wf.uploaded_date, wf.total_records,
                   DATE_FORMAT(wf.created_at,'%d %b %Y %h:%i %p') AS received_at,
                   wf.file_guid
            FROM webhook_files wf
            INNER JOIN webhook_banks wb ON wb.id = wf.bank_id
            ORDER BY wf.id DESC LIMIT 500";
        var list = new List<object>();
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id           = rdr.GetInt32(0),
                bankName     = rdr.GetString(1),
                fileName     = rdr.GetString(2),
                vehicleType  = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                uploadedBy   = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                uploadedDate = rdr.IsDBNull(5) ? "" : rdr.GetString(5),
                totalRecords = rdr.GetInt32(6),
                receivedAt   = rdr.GetString(7),
                fileGuid     = rdr.IsDBNull(8) ? "" : rdr.GetString(8),
            });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Download a webhook CSV file
app.MapGet("/api/webhooks/files/{id:int}/download", async (HttpContext ctx, int id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT file_path, file_name FROM webhook_files WHERE id=@id LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return Results.NotFound(new { message = "File not found" });
        var relPath  = rdr.GetString(0);
        var fileName = rdr.GetString(1);
        await rdr.CloseAsync();

        var fullPath = Path.Combine(app.Environment.ContentRootPath, relPath.TrimStart('/', '\\'));
        if (!File.Exists(fullPath))
            return Results.NotFound(new { message = "CSV file missing on disk" });

        var bytes = await File.ReadAllBytesAsync(fullPath);
        var safeName = Path.GetFileName(relPath);
        ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{safeName}\"";
        return Results.File(bytes, "text/csv");
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// List webhook users (for the "Credentials" tab in Direct Data)
app.MapGet("/api/webhooks/users", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"SELECT id, username, DATE_FORMAT(created_at,'%d %b %Y') AS created_at
                              FROM webhook_users ORDER BY id DESC";
        var list = new List<object>();
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new { id = rdr.GetInt32(0), username = rdr.GetString(1), createdAt = rdr.GetString(2) });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Create webhook user — password stored as SHA-256 hex
app.MapPost("/api/webhooks/users", async (HttpContext ctx, WebhookCreateUserDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest(new { message = "Username and password required" });
    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(dto.Password))).ToLowerInvariant();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO webhook_users (username, password_hash) VALUES (@u, @h)", conn);
        cmd.Parameters.AddWithValue("@u", dto.Username.Trim());
        cmd.Parameters.AddWithValue("@h", hash);
        await cmd.ExecuteNonQueryAsync();
        return Results.Ok(new { message = "User created" });
    }
    catch (MySqlException ex) when (ex.Number == 1062)
    { return Results.BadRequest(new { message = "Username already exists" }); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// Delete webhook user
app.MapDelete("/api/webhooks/users/{id:int}", async (HttpContext ctx, int id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM webhook_users WHERE id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── CRMS Agency Portal endpoints (/api/agency/*) ────────────────────────────
AgencyPortal.Map(app, mysqlHost, mysqlPort);

app.Run($"http://localhost:{port}");

// Local functions must appear before type declarations (CS8803)
static async Task<T> GetCachedAsync<T>(IMemoryCache cache, string key, Func<Task<T>> factory, int seconds)
{
    // Tenant-scope the cache key so one agency's cached dashboard is never
    // served to another agency (or to the legacy default tenant).
    key = TenantContext.Key + ":" + key;
    if (cache.TryGetValue(key, out T? cached) && cached is not null)
        return cached;
    var result = await factory();
    cache.Set(key, result, TimeSpan.FromSeconds(seconds));
    return result;
}

// ── DTOs ──────────────────────────────────────────────────────────────────

record MgrCreateFinanceDto(string Name, string? Description);
record MgrUpdateNameDto(string Name);
record MgrCreateBranchDto(int FinanceId, string Name,
    string? Contact1, string? Contact2, string? Contact3,
    string? Address, string? BranchCode,
    string? City, string? State, string? Postal, string? Notes);
record MgrUpdateBranchDto(string Name,
    string? Contact1, string? Contact2, string? Contact3,
    string? Address, string? BranchCode);
record MgrSetActiveDto(bool Active);
record MgrSetAdminDto(bool Admin);
record MgrAddSubscriptionDto(string StartDate, string EndDate, decimal Amount, string? Notes);
record MgrCreateMappingDto(int ColumnTypeId, string RawName);
record MgrCreateColumnTypeDto(string Name);
record MgrSetStoppedDto(bool Stopped);
record MgrSetBlacklistedDto(bool Blacklisted);
record MgrSetFinanceRestrictionsDto(List<int> FinanceIds);
record MgrSetSubsPasswordDto(string Password);
record MgrSetAdminPassDto(string Password);
record WebhookCreateUserDto(string Username, string Password);
record WebhookFileInfoDto(
    string? FileName, string? VehicleType, string? UploadedBy,
    string? UploadDate, string? BankName, string? FileGUID);
record WebhookProviderRequest(
    WebhookFileInfoDto? FileInfo,
    List<Dictionary<string, object?>>? Data);
