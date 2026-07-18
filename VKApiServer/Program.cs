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
builder.WebHost.ConfigureKestrel(opts => opts.Limits.MaxRequestBodySize = 200 * 1024 * 1024);

builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    opts.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/json", "application/ndjson", "text/plain" });
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(opts =>
    opts.Level = CompressionLevel.Fastest);

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

var mysqlHost = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? "127.0.0.1";
var mysqlPort = int.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var mysqlPortParsed) ? mysqlPortParsed : 3306;

var app = builder.Build();
app.UseResponseCompression();
app.UseCors();

app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
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

var mobileHttp = new HttpClient
{
    BaseAddress = new Uri("http://localhost:5001/"),
    Timeout     = TimeSpan.FromSeconds(60)
};

app.MapGet("/api/health", async () =>
{
    var components = new List<object>();
    bool allOk = true;

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
            "SELECT id, name, contact1, contact2 FROM branches WHERE finance_id = @fid AND is_active = 1 ORDER BY name",
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

app.MapGet("/api/Confirmations", async () =>
{
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id,vehicle_no,chassis_no,model,seizer_name,status,created_at FROM repoconformations ORDER BY created_at DESC",
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


static bool MgrAuth(HttpContext ctx, string key) =>
    ctx.Request.Headers.TryGetValue("X-Api-Key", out var v) && v == key;

static async Task MgrExec(string sql, MySqlConnection c, int timeout = 30,
    params (string n, object v)[] ps)
{
    await using var cmd = new MySqlCommand(sql, c) { CommandTimeout = timeout };
    foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
    await cmd.ExecuteNonQueryAsync();
}

static async Task SetMemberFinances(MySqlConnection c, long memberId, List<int> financeIds)
{
    await MgrExec("DELETE FROM billing_member_finances WHERE member_id=@id", c, 20, ("@id", memberId));
    foreach (var fid in financeIds.Distinct())
        await MgrExec("INSERT IGNORE INTO billing_member_finances (member_id, finance_id) VALUES (@m,@f)",
            c, 20, ("@m", memberId), ("@f", fid));
}

static async Task<string?> FindFinanceConflict(MySqlConnection c, List<int> financeIds, long excludeMemberId)
{
    var ids = financeIds.Distinct().Where(v => v > 0).ToList();
    if (ids.Count == 0) return null;
    var sql = $@"SELECT f.name
                   FROM billing_member_finances bmf
                   JOIN finances f ON f.id = bmf.finance_id
                  WHERE bmf.finance_id IN ({string.Join(",", ids)})
                    AND bmf.member_id <> @ex
                  LIMIT 1";
    await using var cmd = new MySqlCommand(sql, c) { CommandTimeout = 20 };
    cmd.Parameters.AddWithValue("@ex", excludeMemberId);
    var r = await cmd.ExecuteScalarAsync();
    return r as string;
}

static async Task<IResult> SaveBillingImage(HttpContext ctx, string? base64, string kind, int financeId)
{
    if (string.IsNullOrWhiteSpace(base64)) return Results.BadRequest(new { message = "No image provided." });
    try
    {
        var bytes = Convert.FromBase64String(base64);
        var slug = TenantContext.Key;
        var dir = Path.Combine("/opt/vkapi/agency-uploads", "billing");
        Directory.CreateDirectory(dir);
        var file = $"{slug}_{financeId}_{kind}.jpg";
        await File.WriteAllBytesAsync(Path.Combine(dir, file), bytes);
        var rel = $"billing/{file}";
        var col = kind == "letterhead" ? "letterhead_path" : "background_path";
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec($"INSERT INTO billing_settings (finance_id, {col}) VALUES (@fid, @p) ON DUPLICATE KEY UPDATE {col}=VALUES({col})",
            conn, 10, ("@fid", financeId), ("@p", rel));
        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        return Results.Ok(new { url = $"{baseUrl}/agency-uploads/{rel}" });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
}


app.MapGet("/api/mgr/finances", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
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

static string Cap(string? v, int max) =>
    string.IsNullOrEmpty(v) ? "" : (v.Length > max ? v[..max] : v);

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
        cmd.Parameters.AddWithValue("@n",     Cap(dto.Name, 255));
        cmd.Parameters.AddWithValue("@c1",    Cap(dto.Contact1, 255));
        cmd.Parameters.AddWithValue("@c2",    Cap(dto.Contact2, 255));
        cmd.Parameters.AddWithValue("@c3",    Cap(dto.Contact3, 255));
        cmd.Parameters.AddWithValue("@addr",  dto.Address   ?? "");
        cmd.Parameters.AddWithValue("@bcode", Cap(dto.BranchCode, 64));
        cmd.Parameters.AddWithValue("@city",  Cap(dto.City, 128));
        cmd.Parameters.AddWithValue("@state", Cap(dto.State, 128));
        cmd.Parameters.AddWithValue("@postal",Cap(dto.Postal, 32));
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
        cmd.Parameters.AddWithValue("@n",    Cap(dto.Name, 255));
        cmd.Parameters.AddWithValue("@c1",   Cap(dto.Contact1, 255));
        cmd.Parameters.AddWithValue("@c2",   Cap(dto.Contact2, 255));
        cmd.Parameters.AddWithValue("@c3",   Cap(dto.Contact3, 255));
        cmd.Parameters.AddWithValue("@addr", dto.Address    ?? "");
        cmd.Parameters.AddWithValue("@bcode",Cap(dto.BranchCode, 64));
        cmd.Parameters.AddWithValue("@id",   id);
        await cmd.ExecuteNonQueryAsync();
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

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
        await MgrExec("UPDATE branches SET total_records=0, uploaded_at=NULL WHERE id=@id",
            conn, 30, ("@id", id));
        await MgrExec("SET foreign_key_checks=1", conn);
        return Results.Ok(new { deletedCount });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

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

        const string usersSql = @"
            SELECT u.id, u.name, u.mobile, u.address, u.pincode,
                   u.pfp, u.device_id, u.is_active, u.is_admin,
                   COALESCE((SELECT SUM(s.amount) FROM subscriptions s
                              WHERE s.user_id = u.id), 0) AS balance,
                   u.created_at,
                   (SELECT MAX(s.end_date) FROM subscriptions s WHERE s.user_id = u.id) AS sub_end,
                   COALESCE(u.is_stopped,0), COALESCE(u.is_blacklisted,0),
                   (SELECT bt.demand FROM user_billing_targets bt
                     WHERE bt.user_id=u.id AND bt.year=YEAR(CURDATE()) AND bt.month=MONTH(CURDATE())) AS billing_demand,
                   (SELECT bt.target FROM user_billing_targets bt
                     WHERE bt.user_id=u.id AND bt.year=YEAR(CURDATE()) AND bt.month=MONTH(CURDATE())) AS billing_target,
                   (SELECT COUNT(*) FROM repo_submissions rs
                     WHERE rs.submitted_by_user_id = u.id AND rs.bill_status='billed'
                       AND rs.billed_at IS NOT NULL
                       AND YEAR(rs.billed_at)=YEAR(CURDATE()) AND MONTH(rs.billed_at)=MONTH(CURDATE())) AS billed_month
            FROM app_users u ORDER BY u.created_at DESC";
        var users = new List<object>();
        string baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        await using (var cmd = new MySqlCommand(usersSql, conn) { CommandTimeout = 30 })
        {
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var pfpRaw = rdr.IsDBNull(5) ? null : rdr.GetString(5);
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
                    billingDemand = rdr.IsDBNull(14) ? (int?)null : Convert.ToInt32(rdr.GetValue(14)),
                    billingTarget = rdr.IsDBNull(15) ? (int?)null : Convert.ToInt32(rdr.GetValue(15)),
                    billedThisMonth = rdr.IsDBNull(16) ? 0 : Convert.ToInt32(rdr.GetValue(16)),
                });
            }
        }
        return Results.Ok(new { stats = new { total, active, admins, withSub }, users });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

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
        if (dto.Active)
        {
            try
            {
                await using var chk = new MySqlCommand(
                    "SELECT COALESCE(kyc_status,'success') FROM app_users WHERE id=@id", conn);
                chk.Parameters.AddWithValue("@id", id);
                var st = (await chk.ExecuteScalarAsync()) as string ?? "success";
                if (!string.Equals(st, "success", StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { message = "Verify the agent's KYC before activating their account." });
            }
            catch { }
        }
        await MgrExec("UPDATE app_users SET is_active=@v WHERE id=@id", conn, 10,
            ("@v", dto.Active ? 1 : 0), ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapMethods("/api/mgr/users/{id:long}/kyc-status", new[] { "PATCH" }, async (HttpContext ctx, long id, MgrSetKycStatusDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    var status = (dto.Status ?? "").Trim().ToLowerInvariant();
    if (status != "success" && status != "failed" && status != "pending")
        return Results.BadRequest(new { message = "status must be success, failed or pending" });
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        if (status == "failed")
            await MgrExec("UPDATE app_users SET kyc_status='failed', kyc_reject_note=@n, is_active=0 WHERE id=@id",
                conn, 10, ("@n", (object?)dto.Note ?? DBNull.Value), ("@id", id));
        else
            await MgrExec("UPDATE app_users SET kyc_status=@s, kyc_reject_note=NULL WHERE id=@id",
                conn, 10, ("@s", status), ("@id", id));
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

app.MapMethods("/api/mgr/users/{id:long}/billing-targets", new[] { "PATCH" }, async (HttpContext ctx, long id, MgrSetBillingTargetsDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (dto.Year <= 0 || dto.Month < 1 || dto.Month > 12) return Results.BadRequest("Invalid month.");
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec(@"INSERT INTO user_billing_targets (user_id, year, month, demand, target)
                        VALUES (@id, @y, @m, @d, @t)
                        ON DUPLICATE KEY UPDATE demand=VALUES(demand), target=VALUES(target)", conn, 10,
            ("@id", id), ("@y", dto.Year), ("@m", dto.Month),
            ("@d", (object?)dto.Demand ?? DBNull.Value),
            ("@t", (object?)dto.Target ?? DBNull.Value));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/users/{id:long}/billing-targets", async (HttpContext ctx, long id, int year, int month) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (year <= 0 || month < 1 || month > 12) return Results.BadRequest("Invalid month.");
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        int? demand = null, target = null;
        await using (var cmd = new MySqlCommand(
            "SELECT demand, target FROM user_billing_targets WHERE user_id=@id AND year=@y AND month=@m LIMIT 1",
            conn) { CommandTimeout = 10 })
        {
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@y", year);
            cmd.Parameters.AddWithValue("@m", month);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                demand = rdr.IsDBNull(0) ? (int?)null : rdr.GetInt32(0);
                target = rdr.IsDBNull(1) ? (int?)null : rdr.GetInt32(1);
            }
        }

        int billed;
        await using (var cmd = new MySqlCommand(@"
            SELECT COUNT(*) FROM repo_submissions
             WHERE submitted_by_user_id=@id AND bill_status='billed'
               AND billed_at IS NOT NULL AND YEAR(billed_at)=@y AND MONTH(billed_at)=@m",
            conn) { CommandTimeout = 10 })
        {
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@y", year);
            cmd.Parameters.AddWithValue("@m", month);
            billed = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        return Results.Ok(new { demand, target, billed, year, month });
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

app.MapDelete("/api/mgr/users/{id:long}", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

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

        try { await MgrExec("DELETE FROM user_kyc WHERE user_id=@id", conn, 10, ("@id", id)); }
        catch { }

        await MgrExec("DELETE FROM app_users WHERE id=@id", conn, 15, ("@id", id));

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

app.MapGet("/api/mgr/users/{id:long}/kyc", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        string? af = null, ab = null, pf = null, selfie = null, uidaiPhoto = null;
        try
        {
            await using var cmd = new MySqlCommand(
                "SELECT aadhaar_front, aadhaar_back, pan_front, selfie, aadhaar_photo FROM user_kyc WHERE user_id=@uid", conn);
            cmd.Parameters.AddWithValue("@uid", id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                af = rdr.IsDBNull(0) ? null : rdr.GetString(0);
                ab = rdr.IsDBNull(1) ? null : rdr.GetString(1);
                pf = rdr.IsDBNull(2) ? null : rdr.GetString(2);
                selfie = rdr.IsDBNull(3) ? null : rdr.GetString(3);
                uidaiPhoto = rdr.IsDBNull(4) ? null : rdr.GetString(4);
            }
        }
        catch
        {
            await using var cmd = new MySqlCommand(
                "SELECT aadhaar_front, aadhaar_back, pan_front FROM user_kyc WHERE user_id=@uid", conn);
            cmd.Parameters.AddWithValue("@uid", id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                af = rdr.IsDBNull(0) ? null : rdr.GetString(0);
                ab = rdr.IsDBNull(1) ? null : rdr.GetString(1);
                pf = rdr.IsDBNull(2) ? null : rdr.GetString(2);
            }
        }

        string ToUrl(string? rel) => string.IsNullOrEmpty(rel)
            ? ""
            : $"{ctx.Request.Scheme}://{ctx.Request.Host}/uploads/{rel.TrimStart('/')}";

        string? aaName = null, aaDob = null, aaGender = null, aaAddr = null, aaLast4 = null, aaNumber = null;
        bool aaVer = false; DateTime? aaVerAt = null;
        string? pan = null, panName = null; bool panVer = false;
        string? acct = null, ifsc = null, bankHolder = null; bool bankVer = false;
        double? regLat = null, regLng = null; string? regLoc = null;
        string kycStatus = "success"; string? rejectNote = null;
        try
        {
            await using var kc = new MySqlCommand(@"
                SELECT kyc_aadhaar_name, kyc_aadhaar_dob, kyc_aadhaar_gender,
                       kyc_aadhaar_address, kyc_aadhaar_last4, kyc_aadhaar_verified, kyc_verified_at,
                       kyc_pan, kyc_pan_name, kyc_pan_verified,
                       account_number, ifsc_code, kyc_bank_holder, kyc_bank_verified,
                       kyc_reg_lat, kyc_reg_lng, kyc_reg_location,
                       COALESCE(kyc_status,'success'), kyc_reject_note, kyc_aadhaar_number
                FROM app_users WHERE id=@uid", conn);
            kc.Parameters.AddWithValue("@uid", id);
            await using var kr = await kc.ExecuteReaderAsync();
            if (await kr.ReadAsync())
            {
                string? S(int i) => kr.IsDBNull(i) ? null : kr.GetString(i);
                bool   B(int i) => !kr.IsDBNull(i) && kr.GetInt32(i) != 0;
                aaName   = S(0); aaDob = S(1); aaGender = S(2); aaAddr = S(3); aaLast4 = S(4);
                aaVer    = B(5); aaVerAt = kr.IsDBNull(6) ? (DateTime?)null : kr.GetDateTime(6);
                pan      = S(7); panName = S(8); panVer = B(9);
                acct     = S(10); ifsc = S(11); bankHolder = S(12); bankVer = B(13);
                regLat   = kr.IsDBNull(14) ? (double?)null : kr.GetDouble(14);
                regLng   = kr.IsDBNull(15) ? (double?)null : kr.GetDouble(15);
                regLoc   = S(16);
                kycStatus = S(17) ?? "success"; rejectNote = S(18); aaNumber = S(19);
            }
        }
        catch { }

        return Results.Ok(new
        {
            aadhaarFront = ToUrl(af),
            aadhaarBack  = ToUrl(ab),
            panFront     = ToUrl(pf),
            selfie       = ToUrl(selfie),
            aadhaarPhoto = ToUrl(uidaiPhoto),
            kycStatus  = kycStatus,
            rejectNote = rejectNote,
            aadhaar = new {
                verified = aaVer, last4 = aaLast4, number = aaNumber, name = aaName, dob = aaDob,
                gender = aaGender, address = aaAddr, verifiedAt = aaVerAt
            },
            pan = new { verified = panVer, number = pan, name = panName },
            bank = new { verified = bankVer, accountNumber = acct, ifsc = ifsc, holder = bankHolder },
            location = new { lat = regLat, lng = regLng, label = regLoc }
        });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapDelete("/api/mgr/users/{id:long}/kyc/{docType}", async (HttpContext ctx, long id, string docType) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (docType != "aadhaar_front" && docType != "aadhaar_back" && docType != "pan_front"
        && docType != "selfie" && docType != "aadhaar_photo")
        return Results.BadRequest(new { message = "invalid docType" });
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        await using var sel = new MySqlCommand(
            $"SELECT {docType} FROM user_kyc WHERE user_id=@uid", conn);
        sel.Parameters.AddWithValue("@uid", id);
        var rel = (await sel.ExecuteScalarAsync()) as string;

        await MgrExec(
            $"UPDATE user_kyc SET {docType}=NULL WHERE user_id=@uid",
            conn, 10, ("@uid", id));

        if (!string.IsNullOrEmpty(rel))
        {
            try
            {
                var fullPath = Path.Combine("/opt/vkmobileapi/uploads", rel.TrimStart('/'));
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch { }
        }

        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapMethods("/api/mgr/users/{id:long}/kyc-uidai", new[] { "DELETE" }, async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        try
        {
            await using var sel = new MySqlCommand("SELECT aadhaar_photo FROM user_kyc WHERE user_id=@uid", conn);
            sel.Parameters.AddWithValue("@uid", id);
            var rel = (await sel.ExecuteScalarAsync()) as string;
            await MgrExec("UPDATE user_kyc SET aadhaar_photo=NULL WHERE user_id=@uid", conn, 10, ("@uid", id));
            if (!string.IsNullOrEmpty(rel))
            {
                try
                {
                    var fullPath = Path.Combine("/opt/vkmobileapi/uploads", rel.TrimStart('/'));
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                }
                catch { }
            }
        }
        catch { }

        await MgrExec(@"
            UPDATE app_users SET
                kyc_aadhaar_number   = NULL,
                kyc_aadhaar_last4    = NULL,
                kyc_aadhaar_name     = NULL,
                kyc_aadhaar_dob      = NULL,
                kyc_aadhaar_gender   = NULL,
                kyc_aadhaar_address  = NULL,
                kyc_aadhaar_verified = 0,
                kyc_verified_at      = NULL
            WHERE id=@uid", conn, 10, ("@uid", id));

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

app.MapGet("/api/mgr/settings/allocation-password", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `value` FROM app_settings WHERE `key`='allocation_password' LIMIT 1", conn);
        var val = await cmd.ExecuteScalarAsync();
        return Results.Ok(new { password = val?.ToString() ?? "" });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/settings/allocation-password", async (HttpContext ctx, MgrSetSubsPasswordDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec(
            "INSERT INTO app_settings (`key`, `value`) VALUES ('allocation_password', @v) ON DUPLICATE KEY UPDATE `value`=@v",
            conn, 10, ("@v", dto.Password));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/settings/superadmin-password", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `value` FROM app_settings WHERE `key`='superadmin_desktop_password' LIMIT 1", conn);
        var val = await cmd.ExecuteScalarAsync();
        return Results.Ok(new { password = val?.ToString() ?? "" });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/settings/superadmin-password", async (HttpContext ctx, MgrSetSubsPasswordDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec(
            "INSERT INTO app_settings (`key`, `value`) VALUES ('superadmin_desktop_password', @v) ON DUPLICATE KEY UPDATE `value`=@v",
            conn, 10, ("@v", dto.Password));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/settings/billing-desktop-password", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `value` FROM app_settings WHERE `key`='billing_desktop_password' LIMIT 1", conn);
        var val = await cmd.ExecuteScalarAsync();
        return Results.Ok(new { password = val?.ToString() ?? "" });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/settings/billing-desktop-password", async (HttpContext ctx, MgrSetSubsPasswordDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec(
            "INSERT INTO app_settings (`key`, `value`) VALUES ('billing_desktop_password', @v) ON DUPLICATE KEY UPDATE `value`=@v",
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


app.MapGet("/api/mgr/billing/settings", async (HttpContext ctx, int? financeId) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        int fid = financeId ?? 0;
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"SELECT agency_name, pan_no, gst_state, bank_account_name, account_no, ifsc_code,
                                    bank_branch, parking_yard, payment_name, footer_line, letterhead_path, background_path,
                                    vendor_code, last_invoice_no
                             FROM billing_settings WHERE finance_id=@fid LIMIT 1";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@fid", fid);
        await using var r = await cmd.ExecuteReaderAsync();
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        string baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        string? Url(string? rel) => string.IsNullOrEmpty(rel) ? null : $"{baseUrl}/agency-uploads/{rel.TrimStart('/')}";
        if (!await r.ReadAsync())
            return Results.Ok(new { agencyName = "", vendorCode = "", panNo = "", gstState = "", bankAccountName = "", accountNo = "",
                ifscCode = "", bankBranch = "", parkingYard = "", paymentName = "", footerLine = "",
                letterheadUrl = (string?)null, backgroundUrl = (string?)null, nextInvoiceNo = 1 });
        return Results.Ok(new
        {
            agencyName = S(0) ?? "", panNo = S(1) ?? "", gstState = S(2) ?? "", bankAccountName = S(3) ?? "",
            accountNo = S(4) ?? "", ifscCode = S(5) ?? "", bankBranch = S(6) ?? "", parkingYard = S(7) ?? "",
            paymentName = S(8) ?? "", footerLine = S(9) ?? "", letterheadUrl = Url(S(10)), backgroundUrl = Url(S(11)),
            vendorCode = S(12) ?? "", nextInvoiceNo = (r.IsDBNull(13) ? 0 : r.GetInt32(13)) + 1
        });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/billing/settings", async (HttpContext ctx, MgrBillingSettingsDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        const string sql = @"INSERT INTO billing_settings
            (finance_id, agency_name, vendor_code, pan_no, gst_state, bank_account_name, account_no, ifsc_code, bank_branch, parking_yard, payment_name, footer_line)
            VALUES (@fid,@an,@vc,@pan,@gst,@ban,@acc,@ifsc,@bb,@py,@pn,@fl)
            ON DUPLICATE KEY UPDATE agency_name=VALUES(agency_name), vendor_code=VALUES(vendor_code), pan_no=VALUES(pan_no), gst_state=VALUES(gst_state),
              bank_account_name=VALUES(bank_account_name), account_no=VALUES(account_no), ifsc_code=VALUES(ifsc_code),
              bank_branch=VALUES(bank_branch), parking_yard=VALUES(parking_yard), payment_name=VALUES(payment_name),
              footer_line=VALUES(footer_line)";
        await MgrExec(sql, conn, 10,
            ("@fid", dto.FinanceId),
            ("@an", (object?)dto.AgencyName ?? DBNull.Value), ("@vc", (object?)dto.VendorCode ?? DBNull.Value),
            ("@pan", (object?)dto.PanNo ?? DBNull.Value),
            ("@gst", (object?)dto.GstState ?? DBNull.Value), ("@ban", (object?)dto.BankAccountName ?? DBNull.Value),
            ("@acc", (object?)dto.AccountNo ?? DBNull.Value), ("@ifsc", (object?)dto.IfscCode ?? DBNull.Value),
            ("@bb", (object?)dto.BankBranch ?? DBNull.Value), ("@py", (object?)dto.ParkingYard ?? DBNull.Value),
            ("@pn", (object?)dto.PaymentName ?? DBNull.Value), ("@fl", (object?)dto.FooterLine ?? DBNull.Value));
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/billing/next-invoice", async (HttpContext ctx, MgrBillingImageDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (dto.FinanceId <= 0) return Results.BadRequest("financeId required.");
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec(@"INSERT INTO billing_settings (finance_id, last_invoice_no) VALUES (@fid, 1)
                        ON DUPLICATE KEY UPDATE last_invoice_no = last_invoice_no + 1", conn, 10,
            ("@fid", dto.FinanceId));
        await using var cmd = new MySqlCommand(
            "SELECT last_invoice_no FROM billing_settings WHERE finance_id=@fid LIMIT 1", conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@fid", dto.FinanceId);
        var n = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return Results.Ok(new { invoiceNo = n });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/billing/letterhead", async (HttpContext ctx, MgrBillingImageDto dto) =>
    (!MgrAuth(ctx, desktopLoginPassword)) ? Results.Unauthorized() : await SaveBillingImage(ctx, dto.ImageBase64, "letterhead", dto.FinanceId));

app.MapPost("/api/mgr/billing/background", async (HttpContext ctx, MgrBillingImageDto dto) =>
    (!MgrAuth(ctx, desktopLoginPassword)) ? Results.Unauthorized() : await SaveBillingImage(ctx, dto.ImageBase64, "background", dto.FinanceId));

app.MapGet("/api/mgr/billing/members", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        var members = new List<Dictionary<string, object?>>();
        var byId = new Dictionary<long, Dictionary<string, object?>>();
        await using (var cmd = new MySqlCommand(
            "SELECT id, name, mobile, email, username, password, is_active FROM billing_members ORDER BY name", conn) { CommandTimeout = 20 })
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                var m = new Dictionary<string, object?>
                {
                    ["id"]       = rdr.GetInt64(0),
                    ["name"]     = rdr.GetString(1),
                    ["mobile"]   = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    ["email"]    = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                    ["username"] = rdr.GetString(4),
                    ["password"] = rdr.IsDBNull(5) ? "" : rdr.GetString(5),
                    ["isActive"] = rdr.GetBoolean(6),
                    ["financeIds"] = new List<int>()
                };
                members.Add(m);
                byId[(long)m["id"]!] = m;
            }
        }
        if (byId.Count > 0)
        {
            await using var cmd = new MySqlCommand(
                "SELECT member_id, finance_id FROM billing_member_finances", conn) { CommandTimeout = 20 };
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                long mid = rdr.GetInt64(0);
                if (byId.TryGetValue(mid, out var m))
                    ((List<int>)m["financeIds"]!).Add(rdr.GetInt32(1));
            }
        }
        return Results.Ok(members);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/billing/members", async (HttpContext ctx, MgrBillingMemberDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest(new { message = "Name, username and password are required." });
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        var conflict = await FindFinanceConflict(conn, dto.FinanceIds ?? new List<int>(), 0);
        if (conflict != null)
            return Results.Conflict(new { message = $"\"{conflict}\" is already allocated to another member. Each finance can belong to only one member." });
        long id;
        await using (var cmd = new MySqlCommand(@"
            INSERT INTO billing_members (name, mobile, email, username, password, is_active)
            VALUES (@n,@m,@e,@u,@p,@a); SELECT LAST_INSERT_ID();", conn))
        {
            cmd.Parameters.AddWithValue("@n", dto.Name.Trim());
            cmd.Parameters.AddWithValue("@m", (object?)dto.Mobile ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@e", (object?)dto.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@u", dto.Username.Trim());
            cmd.Parameters.AddWithValue("@p", dto.Password);
            cmd.Parameters.AddWithValue("@a", dto.IsActive);
            id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        await SetMemberFinances(conn, id, dto.FinanceIds ?? new List<int>());
        return Results.Ok(new { id });
    }
    catch (MySqlException ex) when (ex.Number == 1062)
    { return Results.Conflict(new { message = "That username is already taken." }); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/billing/members/{id:long}", async (HttpContext ctx, long id, MgrBillingMemberDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        if (dto.FinanceIds != null)
        {
            var conflict = await FindFinanceConflict(conn, dto.FinanceIds, id);
            if (conflict != null)
                return Results.Conflict(new { message = $"\"{conflict}\" is already allocated to another member. Each finance can belong to only one member." });
        }
        if (string.IsNullOrWhiteSpace(dto.Password))
            await MgrExec(@"UPDATE billing_members SET name=@n, mobile=@m, email=@e, username=@u, is_active=@a WHERE id=@id", conn, 20,
                ("@n", dto.Name.Trim()), ("@m", (object?)dto.Mobile ?? DBNull.Value), ("@e", (object?)dto.Email ?? DBNull.Value),
                ("@u", dto.Username.Trim()), ("@a", dto.IsActive), ("@id", id));
        else
            await MgrExec(@"UPDATE billing_members SET name=@n, mobile=@m, email=@e, username=@u, password=@p, is_active=@a WHERE id=@id", conn, 20,
                ("@n", dto.Name.Trim()), ("@m", (object?)dto.Mobile ?? DBNull.Value), ("@e", (object?)dto.Email ?? DBNull.Value),
                ("@u", dto.Username.Trim()), ("@p", dto.Password), ("@a", dto.IsActive), ("@id", id));
        if (dto.FinanceIds != null) await SetMemberFinances(conn, id, dto.FinanceIds);
        return Results.Ok(new { success = true });
    }
    catch (MySqlException ex) when (ex.Number == 1062)
    { return Results.Conflict(new { message = "That username is already taken." }); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapDelete("/api/mgr/billing/members/{id:long}", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("DELETE FROM billing_members WHERE id=@id", conn, 20, ("@id", id));
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/billing/members/{id:long}/finances", async (HttpContext ctx, long id, MgrSetMemberFinancesDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await SetMemberFinances(conn, id, dto.FinanceIds ?? new List<int>());
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/billing/member-login", async (HttpContext ctx, MgrMemberLoginDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest(new { message = "Enter your username and password." });
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        long   mid = 0; string name = ""; bool active = false; string pass = "";
        await using (var cmd = new MySqlCommand(
            "SELECT id, name, password, is_active FROM billing_members WHERE username=@u OR email=@u LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("@u", dto.Username.Trim());
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync())
                return Results.BadRequest(new { message = "Invalid email or password." });
            mid = rdr.GetInt64(0); name = rdr.GetString(1);
            pass = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
            active = rdr.GetBoolean(3);
        }
        if (!string.Equals(pass, dto.Password, StringComparison.Ordinal))
            return Results.BadRequest(new { message = "Invalid username or password." });
        if (!active)
            return Results.Json(new { message = "This billing login has been disabled." }, statusCode: 403);

        var financeIds = new List<int>();
        await using (var cmd = new MySqlCommand(
            "SELECT finance_id FROM billing_member_finances WHERE member_id=@id", conn))
        {
            cmd.Parameters.AddWithValue("@id", mid);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) financeIds.Add(rdr.GetInt32(0));
        }
        return Results.Ok(new { id = mid, name, financeIds });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/billing/submissions", async (HttpContext ctx, string? from, string? to, string? financeIds, string? status) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        var ids = (financeIds ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : -1)
            .Where(v => v >= 0).ToList();

        var where = new List<string>();
        if (ids.Count > 0) where.Add($"finance_id IN ({string.Join(",", ids)})");
        else if (!string.IsNullOrWhiteSpace(financeIds)) where.Add("1=0");
        if (DateTime.TryParse(from, out var f)) where.Add("created_at >= @from");
        if (DateTime.TryParse(to, out var t))   where.Add("created_at < @to");
        if (status == "pending" || status == "billed") where.Add("bill_status = @st");
        string whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand($@"
            SELECT id, record_id, finance_id, finance_name, branch_name,
                   loan_no, customer_name, vehicle_no, model, chassis_no, engine_no,
                   agent_name, parking_yard_name, parking_yard_mobile, load_details,
                   addl_charges_notes, addl_charges_amount,
                   confirmation_by_name, confirmation_by_mobile, executive_name,
                   collection_update, remark,
                   billing_action, hold_until, hold_days, bill_status, billed_at,
                   submitted_by_name, created_at,
                   repo_charges, advance, courier_yn, banker_address, pod_number,
                   invoice_no, bill_file
              FROM repo_submissions {whereSql}
             ORDER BY created_at DESC LIMIT 2000", conn) { CommandTimeout = 30 };
        if (DateTime.TryParse(from, out var f2)) cmd.Parameters.AddWithValue("@from", f2.Date);
        if (DateTime.TryParse(to, out var t2))   cmd.Parameters.AddWithValue("@to", t2.Date.AddDays(1));
        if (status == "pending" || status == "billed") cmd.Parameters.AddWithValue("@st", status);

        var billBaseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        string? S(int i) => rdr.IsDBNull(i) ? null : rdr.GetString(i);
        while (await rdr.ReadAsync())
        {
            list.Add(new
            {
                id = rdr.GetInt64(0),
                recordId = rdr.IsDBNull(1) ? (long?)null : rdr.GetInt64(1),
                financeId = rdr.IsDBNull(2) ? (int?)null : rdr.GetInt32(2),
                financeName = S(3) ?? "", branchName = S(4) ?? "",
                loanNo = S(5) ?? "", customerName = S(6) ?? "", vehicleNo = S(7) ?? "",
                model = S(8) ?? "", chassisNo = S(9) ?? "", engineNo = S(10) ?? "",
                agentName = S(11) ?? "", parkingYardName = S(12) ?? "", parkingYardMobile = S(13) ?? "",
                loadDetails = S(14) ?? "", addlChargesNotes = S(15) ?? "",
                addlChargesAmount = rdr.IsDBNull(16) ? (decimal?)null : rdr.GetDecimal(16),
                confirmationByName = S(17) ?? "", confirmationByMobile = S(18) ?? "", executiveName = S(19) ?? "",
                collectionUpdate = S(20) ?? "", remark = S(21) ?? "",
                billingAction = S(22) ?? "immediate",
                holdUntil = rdr.IsDBNull(23) ? (string?)null : rdr.GetDateTime(23).ToString("yyyy-MM-dd"),
                holdDays = rdr.IsDBNull(24) ? (int?)null : rdr.GetInt32(24),
                billStatus = S(25) ?? "pending",
                billedAt = rdr.IsDBNull(26) ? (string?)null : rdr.GetDateTime(26).ToString("yyyy-MM-dd HH:mm"),
                submittedByName = S(27) ?? "",
                createdAt = rdr.GetDateTime(28).ToString("yyyy-MM-dd HH:mm"),
                repoCharges = rdr.IsDBNull(29) ? (decimal?)null : rdr.GetDecimal(29),
                advance = rdr.IsDBNull(30) ? (decimal?)null : rdr.GetDecimal(30),
                courierYn = S(31) ?? "",
                bankerAddress = S(32) ?? "",
                podNumber = S(33) ?? "",
                invoiceNo = S(34) ?? "",
                billUrl = string.IsNullOrEmpty(S(35)) ? "" : $"{billBaseUrl}/agency-uploads/{S(35)!.TrimStart('/')}"
            });
        }
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/couriers/submissions/{id:long}/update", async (HttpContext ctx, long id, MgrCourierUpdateDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

        var sets = new List<string>
        {
            "repo_charges=@rc", "advance=@adv", "courier_yn=@cy",
            "banker_address=@ba", "pod_number=@pod", "courier_updated_at=NOW()"
        };
        var ps = new List<(string, object)>
        {
            ("@rc", (object?)dto.RepoCharges ?? DBNull.Value),
            ("@adv", (object?)dto.Advance ?? DBNull.Value),
            ("@cy", (object?)dto.CourierYn ?? DBNull.Value),
            ("@ba", (object?)dto.BankerAddress ?? DBNull.Value),
            ("@pod", (object?)dto.PodNumber ?? DBNull.Value),
            ("@id", id)
        };
        if (dto.BillingAction is "immediate" or "hold" or "cancel")
        {
            sets.Add("billing_action=@ba2");
            ps.Add(("@ba2", dto.BillingAction));
        }

        await MgrExec($"UPDATE repo_submissions SET {string.Join(", ", sets)} WHERE id=@id",
            conn, 20, ps.ToArray());
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/settings/courier-password", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `value` FROM app_settings WHERE `key`='courier_desktop_password' LIMIT 1", conn);
        var val = await cmd.ExecuteScalarAsync();
        return Results.Ok(new { password = val?.ToString() ?? "" });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPut("/api/mgr/settings/courier-password", async (HttpContext ctx, MgrSetSubsPasswordDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec(
            "INSERT INTO app_settings (`key`, `value`) VALUES ('courier_desktop_password', @v) ON DUPLICATE KEY UPDATE `value`=@v",
            conn, 10, ("@v", dto.Password));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/billing/submissions/{id:long}/billed", async (HttpContext ctx, long id, MgrMarkBilledDto dto) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        string? billRel = null;
        if (!string.IsNullOrWhiteSpace(dto.BillBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(dto.BillBase64);
                var ext = string.IsNullOrWhiteSpace(dto.BillExt) ? "pdf" : dto.BillExt!.TrimStart('.');
                var dir = Path.Combine("/opt/vkapi/agency-uploads", "bills");
                Directory.CreateDirectory(dir);
                var file = $"{TenantContext.Key}_sub{id}.{ext}";
                await File.WriteAllBytesAsync(Path.Combine(dir, file), bytes);
                billRel = $"bills/{file}";
            }
            catch { }
        }

        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec(
            @"UPDATE repo_submissions
                 SET bill_status='billed', billed_at=NOW(), billed_by_member_id=@mid,
                     invoice_no=COALESCE(@inv, invoice_no),
                     bill_file=COALESCE(@bf, bill_file)
               WHERE id=@id",
            conn, 20,
            ("@mid", dto.MemberId),
            ("@inv", (object?)dto.InvoiceNo ?? DBNull.Value),
            ("@bf", (object?)billRel ?? DBNull.Value),
            ("@id", id));
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});


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

app.MapGet("/api/mgr/search/list", async (HttpContext ctx, string? q, string? mode, int? financeId) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(q)) return Results.Ok(new List<object>());
    try
    {
        var isChassis = string.Equals(mode, "chassis", StringComparison.OrdinalIgnoreCase);
        var scope = financeId is > 0 ? " AND b.finance_id = @fid" : "";
        const string lite = @"
            vr.id, vr.vehicle_no, vr.chassis_no, vr.model,
            COALESCE(NULLIF(vr.branch_name_raw,''), b.name) AS branch_name, COALESCE(f.name,'') AS financer,
            COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y %h:%i %p'),'') AS created_on";
        var sql = isChassis
            ? $@"SELECT {lite} FROM chassis_info ci
                 INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
                 INNER JOIN branches b ON b.id = vr.branch_id
                 LEFT  JOIN finances f ON f.id = b.finance_id
                 WHERE ci.last5 = @q{scope}"
            : $@"SELECT {lite} FROM rc_info ri
                 INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
                 INNER JOIN branches b ON b.id = vr.branch_id
                 LEFT  JOIN finances f ON f.id = b.finance_id
                 WHERE ri.last4 = @q{scope}";

        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@q", q.ToUpper().Trim());
        if (financeId is > 0) cmd.Parameters.AddWithValue("@fid", financeId.Value);
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


app.MapGet("/api/mgr/dashboard-stats", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
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
            FROM device_change_requests ORDER BY requested_at DESC";
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


app.MapGet("/api/mgr/live-users", async (HttpContext ctx, string? since) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();

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
                ORDER BY u.last_seen DESC";
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
                ORDER BY u.last_seen DESC";
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

        await using var cmd = new MySqlCommand();
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
        sql += " ORDER BY sl.server_time DESC";

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

app.MapGet("/api/mgr/integration-messages", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        var list = new List<object>();
        await using (var ensure = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS integration_agency_messages (
              id INT UNSIGNED NOT NULL AUTO_INCREMENT,
              integration_account_id INT NOT NULL,
              from_finance_name VARCHAR(200) DEFAULT NULL,
              from_email VARCHAR(200) DEFAULT NULL,
              message TEXT NOT NULL,
              is_read TINYINT(1) NOT NULL DEFAULT 0,
              created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
              PRIMARY KEY (id), KEY idx_iam_created (created_at), KEY idx_iam_read (is_read)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn))
            await ensure.ExecuteNonQueryAsync();
        await using (var cmd = new MySqlCommand(@"
            SELECT id, COALESCE(from_finance_name,''), COALESCE(from_email,''), message, is_read,
                   DATE_FORMAT(created_at,'%d %b %Y %h:%i %p')
            FROM integration_agency_messages ORDER BY id DESC LIMIT 500", conn) { CommandTimeout = 15 })
        await using (var rdr = await cmd.ExecuteReaderAsync())
            while (await rdr.ReadAsync())
                list.Add(new
                {
                    id = rdr.GetInt32(0), fromFinance = rdr.GetString(1), fromEmail = rdr.GetString(2),
                    message = rdr.GetString(3), isRead = rdr.GetInt32(4) != 0, createdAt = rdr.GetString(5)
                });
        int unread;
        await using (var uc = new MySqlCommand("SELECT COUNT(*) FROM integration_agency_messages WHERE is_read=0", conn))
            unread = Convert.ToInt32(await uc.ExecuteScalarAsync());
        return Results.Ok(new { messages = list, unread });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/mgr/integration-messages/read", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE integration_agency_messages SET is_read=1 WHERE is_read=0", conn);
        await cmd.ExecuteNonQueryAsync();
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

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

app.MapPost("/api/mgr/records/upload", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();

    string mode     = (ctx.Request.Query["mode"].FirstOrDefault() ?? "replace").ToLowerInvariant();
    bool   doClear  = mode is "replace" or "begin";
    bool   doFinal  = mode is "replace" or "finish";

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

    ctx.Response.StatusCode  = 200;
    ctx.Response.ContentType = "application/x-ndjson";
    ctx.Response.Headers["Cache-Control"]      = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"]  = "no";

    async Task Push(int pct, string msg, int? inserted = null, double? elapsed = null)
    {
        string line;
        if (inserted.HasValue)
        {
            var es = elapsed!.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            line = $"{{\"pct\":{pct},\"msg\":\"{msg}\",\"inserted\":{inserted.Value},\"elapsedSeconds\":{es}}}";
        }
        else
            line = $"{{\"pct\":{pct},\"msg\":\"{msg}\"}}";
        await ctx.Response.WriteAsync(line + "\n");
        await ctx.Response.Body.FlushAsync();
    }

    try
    {
        var sw = Stopwatch.StartNew();
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
        dt.BeginLoadData();

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

        await Push(15, "Connecting to database…");
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
        await MgrExec("SET foreign_key_checks = 0", conn);
        await MgrExec("SET unique_checks = 0", conn);

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

        int totalRows = parsedRows;
        await Push(35, $"0 / {totalRows:N0}");

        var bc = new MySqlBulkCopy(conn)
        {
            DestinationTableName = "vehicle_records",
            BulkCopyTimeout      = 600,
            NotifyAfter          = Math.Max(1000, totalRows / 20)
        };
        for (int i = 0; i < dt.Columns.Count; i++)
            bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, dt.Columns[i].ColumnName));

        var copiedQueue = new System.Collections.Concurrent.ConcurrentQueue<long>();
        bc.MySqlRowsCopied += (_, e) => copiedQueue.Enqueue(e.RowsCopied);

        var bcTask = bc.WriteToServerAsync(dt);
        while (!bcTask.IsCompleted)
        {
            await Task.Delay(300);
            if (copiedQueue.TryDequeue(out long copied))
            {
                int pct = 35 + (int)((double)copied / totalRows * 50);
                await Push(Math.Min(pct, 85), $"{copied:N0} / {totalRows:N0}");
            }
        }
        var bcResult = await bcTask;
        dt.Dispose();
        long bcInserted = bcResult.RowsInserted;

        await Push(88, "Verifying insert count…");
        long dbCount;
        await using (var verifCmd = new MySqlCommand(
            "SELECT COUNT(*) FROM vehicle_records WHERE branch_id = @bid", conn) { CommandTimeout = 30 })
        {
            verifCmd.Parameters.AddWithValue("@bid", branchId);
            dbCount = Convert.ToInt64(await verifCmd.ExecuteScalarAsync());
        }

        Console.WriteLine($"[Upload] branch={branchId} sent={totalRows} skipped={skippedRows} bcInserted={bcInserted} dbCount={dbCount}");

        if (doFinal)
        {
            await Push(92, "Updating branch stats…");
            await MgrExec("UPDATE branches SET total_records=@cnt, uploaded_at=NOW() WHERE id=@bid",
                conn, 30, ("@cnt", dbCount), ("@bid", branchId));
        }
        await MgrExec("SET foreign_key_checks = 1", conn);
        await MgrExec("SET unique_checks = 1", conn);

        int inserted = (int)dbCount;
        await Push(100, $"Done", inserted, sw.Elapsed.TotalSeconds);

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
        await ctx.Response.WriteAsync($"{{\"pct\":-1,\"msg\":{System.Text.Json.JsonSerializer.Serialize(ex.Message)}}}\n");
        await ctx.Response.Body.FlushAsync();
    }

    return new NoopResult();
});


app.MapGet("/api/mgr/export/users", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(TenantContext.Conn);
        await conn.OpenAsync();
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

const string XLSX_FIELDS = @"
    vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model, vr.agreement_no,
    vr.customer_name, vr.customer_contact, vr.customer_address,
    COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name,
    COALESCE(vr.branch_name_raw,'') AS branch_name_raw,
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
    "Finance Name","Branch Name","Branch Name (Raw)","Bucket","GV","OD","Seasoning",
    "TBR Flag","Sec9 Available","Sec17 Available",
    "Level1","Level1 Contact","Level2","Level2 Contact",
    "Level3","Level3 Contact","Level4","Level4 Contact",
    "Sender Mail 1","Sender Mail 2","Executive Name",
    "POS","TOSS","Remark","Region","Area","Created On" };

static bool MgrAuthFlexible(HttpContext ctx, string key) =>
    (ctx.Request.Headers.TryGetValue("X-Api-Key", out var v) && v == key) ||
    (ctx.Request.Query.TryGetValue("key", out var q) && q == key);

async Task StreamVehicleXlsx(HttpContext ctx, string whereSql, string sheetName,
                             string downloadName, long offset, int limit, params (string, object)[] ps)
{
    var bodyControl = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
    if (bodyControl != null) bodyControl.AllowSynchronousIO = true;

    await using var conn = new MySqlConnection(TenantContext.Conn);
    await conn.OpenAsync();
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

foreach (var spec in new[]
{
    ("vehicle-records", "Vehicle Records", $"SELECT {XLSX_FIELDS} FROM vehicle_records vr INNER JOIN branches b ON b.id=vr.branch_id LEFT JOIN finances f ON f.id=b.finance_id ORDER BY vr.id"),
    ("rc-records",      "RC Records",      $"SELECT ri.rc_number AS vehicle_no, vr.chassis_no, vr.engine_no, ri.model, vr.agreement_no, vr.customer_name, vr.customer_contact, vr.customer_address, COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name, COALESCE(vr.branch_name_raw,'') AS branch_name_raw, vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag, vr.sec9_available, vr.sec17_available, vr.level1, vr.level1_contact, vr.level2, vr.level2_contact, vr.level3, vr.level3_contact, vr.level4, vr.level4_contact, vr.sender_mail1, vr.sender_mail2, vr.executive_name, vr.pos, vr.toss, vr.remark, vr.region, vr.area, COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on FROM rc_info ri INNER JOIN vehicle_records vr ON vr.id=ri.vehicle_record_id INNER JOIN branches b ON b.id=vr.branch_id LEFT JOIN finances f ON f.id=b.finance_id ORDER BY ri.id"),
    ("chassis-records", "Chassis Records", $"SELECT vr.vehicle_no, ci.chassis_number AS chassis_no, vr.engine_no, ci.model, vr.agreement_no, vr.customer_name, vr.customer_contact, vr.customer_address, COALESCE(f.name,'') AS financer, COALESCE(b.name,'') AS branch_name, COALESCE(vr.branch_name_raw,'') AS branch_name_raw, vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag, vr.sec9_available, vr.sec17_available, vr.level1, vr.level1_contact, vr.level2, vr.level2_contact, vr.level3, vr.level3_contact, vr.level4, vr.level4_contact, vr.sender_mail1, vr.sender_mail2, vr.executive_name, vr.pos, vr.toss, vr.remark, vr.region, vr.area, COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on FROM chassis_info ci INNER JOIN vehicle_records vr ON vr.id=ci.vehicle_record_id INNER JOIN branches b ON b.id=vr.branch_id LEFT JOIN finances f ON f.id=b.finance_id ORDER BY ci.id"),
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

var agencyUploads = "/opt/vkapi/agency-uploads";
try { Directory.CreateDirectory(agencyUploads); } catch { }
if (Directory.Exists(agencyUploads))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(agencyUploads),
        RequestPath  = "/agency-uploads",
        ServeUnknownFileTypes = true
    });
}

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


var webhookFilesRoot = Path.Combine(app.Environment.ContentRootPath, "webhook-files");
Directory.CreateDirectory(webhookFilesRoot);

app.MapPost("/api/webhooks/provider/HDB", async (HttpContext ctx) =>
{
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

    var slug = ctx.Request.Headers["X-Agency-Slug"].FirstOrDefault()?.Trim().ToLowerInvariant();
    if (string.IsNullOrEmpty(slug))
        return Results.BadRequest(new { message = "X-Agency-Slug header required" });
    var tenantConn = TenantContext.BuildTenantConn(mysqlHost, mysqlPort, slug);

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

    WebhookProviderRequest? body;
    try { body = await ctx.Request.ReadFromJsonAsync<WebhookProviderRequest>(); }
    catch { return Results.BadRequest(new { message = "Invalid JSON body" }); }
    if (body == null || body.FileInfo == null || body.Data == null || body.Data.Count == 0)
        return Results.BadRequest(new { message = "fileInfo and data[] required" });
    var fi = body.FileInfo;

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
            ORDER BY wf.id DESC";
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

var hdbAllowedHeaders = new Dictionary<string, string>
{
    ["vehicleno"] = "Vehicle No", ["chassisno"] = "Chassis No", ["engineno"] = "Engine No",
    ["model"] = "Model", ["agreementno"] = "Agreement No", ["bucket"] = "Bucket",
    ["gv"] = "GV", ["od"] = "OD", ["seasoning"] = "Seasoning", ["tbr"] = "TBR",
    ["sec9"] = "Sec 9", ["sec17"] = "Sec 17", ["customername"] = "Customer Name",
    ["customeraddress"] = "Customer Address", ["customercontact"] = "Customer Contact",
    ["region"] = "Region", ["area"] = "Area", ["branch"] = "Branch",
    ["level1"] = "Level 1", ["level1contact"] = "Level 1 Contact",
    ["level2"] = "Level 2", ["level2contact"] = "Level 2 Contact",
    ["level3"] = "Level 3", ["level3contact"] = "Level 3 Contact",
    ["level4"] = "Level 4", ["level4contact"] = "Level 4 Contact",
    ["sendermail1"] = "Sender Mail 1", ["sendermail2"] = "Sender Mail 2",
    ["executivename"] = "Executive Name", ["pos"] = "POS", ["toss"] = "TOSS", ["remark"] = "Remark"
};

const string hdbSlug = "v_k_enterprises";
const string hdbBank = "HDB";

static string IntegNorm(string s) =>
    System.Text.RegularExpressions.Regex.Replace(s ?? "", "[^A-Za-z0-9]", "").ToLowerInvariant();

async Task<bool> IntegValidateCreds(string connStr, string? user, string? pass)
{
    await using var c = new MySqlConnection(connStr);
    await c.OpenAsync();
    await using var cmd = new MySqlCommand(
        "SELECT password_hash FROM webhook_users WHERE username=@u LIMIT 1", c);
    cmd.Parameters.AddWithValue("@u", user ?? "");
    var stored = await cmd.ExecuteScalarAsync() as string;
    if (stored == null) return false;
    if (stored.StartsWith("$2a$") || stored.StartsWith("$2b$"))
        return BCrypt.Net.BCrypt.Verify(pass, stored);
    var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(pass ?? ""))).ToLowerInvariant();
    return sha == stored;
}

app.MapPost("/api/integration/vk/hdb/login", async (IntegrationLoginDto dto) =>
{
    var connStr = TenantContext.BuildTenantConn(mysqlHost, mysqlPort, hdbSlug);
    try
    {
        if (!await IntegValidateCreds(connStr, dto.Username, dto.Password))
            return Results.Json(new { message = "Invalid username or password." }, statusCode: 401);
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/integration/vk/hdb/files", async (IntegrationLoginDto dto) =>
{
    var connStr = TenantContext.BuildTenantConn(mysqlHost, mysqlPort, hdbSlug);
    try
    {
        if (!await IntegValidateCreds(connStr, dto.Username, dto.Password))
            return Results.Json(new { message = "Invalid username or password." }, statusCode: 401);
        await using var c = new MySqlConnection(connStr);
        await c.OpenAsync();
        var files = new List<object>();
        await using var cmd = new MySqlCommand(@"
            SELECT wf.file_name, wf.total_records,
                   COALESCE(DATE_FORMAT(wf.created_at,'%d %b %Y %h:%i %p'),'')
            FROM webhook_files wf
            INNER JOIN webhook_banks wb ON wb.id = wf.bank_id
            WHERE wb.bank_name = @b
            ORDER BY wf.id DESC", c) { CommandTimeout = 15 };
        cmd.Parameters.AddWithValue("@b", hdbBank);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            files.Add(new { fileName = rdr.GetString(0), totalRecords = rdr.GetInt32(1), uploadedAt = rdr.GetString(2) });
        return Results.Ok(new { files });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/integration/vk/hdb/upload", async (IntegrationUploadDto dto) =>
{
    var connStr = TenantContext.BuildTenantConn(mysqlHost, mysqlPort, hdbSlug);
    if (dto.Headers == null || dto.Headers.Count == 0 || dto.Rows == null)
        return Results.BadRequest(new { message = "Empty sheet." });
    try
    {
        if (!await IntegValidateCreds(connStr, dto.Username, dto.Password))
            return Results.Json(new { message = "Invalid username or password." }, statusCode: 401);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }

    var unknown = dto.Headers
        .Where(h => !string.IsNullOrWhiteSpace(h) && !hdbAllowedHeaders.ContainsKey(IntegNorm(h)))
        .ToList();
    if (unknown.Count > 0)
        return Results.BadRequest(new
        {
            message = "These column names are not recognised and must be removed or renamed: " + string.Join(", ", unknown),
            unknownColumns = unknown
        });

    var safeSlug = System.Text.RegularExpressions.Regex.Replace(hdbSlug, "[^a-z0-9_-]", "");
    var slotDir  = Path.Combine(webhookFilesRoot, safeSlug);
    Directory.CreateDirectory(slotDir);
    var baseName = string.IsNullOrWhiteSpace(dto.FileName) ? "hdb-upload" : dto.FileName.Replace(" ", "_");
    var csvName  = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{baseName}.csv";
    var csvPath  = Path.Combine(slotDir, csvName);
    var relPath  = Path.Combine("webhook-files", safeSlug, csvName);

    static string Csv(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
    int totalRows = 0;
    try
    {
        await using var sw = new StreamWriter(csvPath, false, System.Text.Encoding.UTF8);
        await sw.WriteLineAsync(string.Join(",", dto.Headers.Select(Csv)));
        foreach (var row in dto.Rows)
        {
            var cells = new List<string>(dto.Headers.Count);
            for (int i = 0; i < dto.Headers.Count; i++)
                cells.Add(Csv(row != null && i < row.Count ? row[i] : ""));
            await sw.WriteLineAsync(string.Join(",", cells));
            totalRows++;
        }
    }
    catch (Exception ex) { return Results.Problem($"CSV write failed: {ex.Message}"); }

    try
    {
        await using var c = new MySqlConnection(connStr);
        await c.OpenAsync();

        await using var bankCmd = new MySqlCommand(@"
            INSERT INTO webhook_banks (bank_name) VALUES (@n)
            ON DUPLICATE KEY UPDATE bank_name=bank_name;
            SELECT id FROM webhook_banks WHERE bank_name=@n LIMIT 1;", c);
        bankCmd.Parameters.AddWithValue("@n", hdbBank);
        var bankId = Convert.ToInt32(await bankCmd.ExecuteScalarAsync());

        await using var fileCmd = new MySqlCommand(@"
            INSERT INTO webhook_files
                (bank_id, file_name, file_path, vehicle_type, uploaded_by, uploaded_date, total_records)
            VALUES (@bid, @fn, @fp, @vt, @ub, @ud, @tr)", c);
        fileCmd.Parameters.AddWithValue("@bid", bankId);
        fileCmd.Parameters.AddWithValue("@fn",  dto.FileName ?? csvName);
        fileCmd.Parameters.AddWithValue("@fp",  relPath);
        fileCmd.Parameters.AddWithValue("@vt",  dto.VehicleType ?? "");
        fileCmd.Parameters.AddWithValue("@ub",  dto.Username ?? "");
        fileCmd.Parameters.AddWithValue("@ud",  DateTime.UtcNow.ToString("dd MMM yyyy"));
        fileCmd.Parameters.AddWithValue("@tr",  totalRows);
        await fileCmd.ExecuteNonQueryAsync();
    }
    catch (Exception ex) { return Results.Problem($"DB insert failed: {ex.Message}"); }

    return Results.Ok(new { ok = true, records = totalRows });
});

AgencyPortal.Map(app, mysqlHost, mysqlPort);

SandboxKyc.Map(app, ctx => MgrAuth(ctx, desktopLoginPassword));

app.Run($"http://localhost:{port}");

static async Task<T> GetCachedAsync<T>(IMemoryCache cache, string key, Func<Task<T>> factory, int seconds)
{
    key = TenantContext.Key + ":" + key;
    if (cache.TryGetValue(key, out T? cached) && cached is not null)
        return cached;
    var result = await factory();
    cache.Set(key, result, TimeSpan.FromSeconds(seconds));
    return result;
}


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
record MgrSetBillingTargetsDto(int? Demand, int? Target, int Year, int Month);
record MgrAddSubscriptionDto(string StartDate, string EndDate, decimal Amount, string? Notes);
record MgrCreateMappingDto(int ColumnTypeId, string RawName);
record MgrCreateColumnTypeDto(string Name);
record MgrSetStoppedDto(bool Stopped);
record MgrSetBlacklistedDto(bool Blacklisted);
record MgrSetKycStatusDto(string? Status, string? Note);
record MgrBillingSettingsDto(
    int FinanceId,
    string? AgencyName, string? PanNo, string? GstState, string? BankAccountName,
    string? AccountNo, string? IfscCode, string? BankBranch, string? ParkingYard,
    string? PaymentName, string? FooterLine, string? VendorCode);
record MgrBillingImageDto(string? ImageBase64, int FinanceId = 0);
record MgrBillingMemberDto(
    long Id, string Name, string? Mobile, string? Email,
    string Username, string? Password, bool IsActive, List<int>? FinanceIds);
record MgrMemberLoginDto(string Username, string Password);
record MgrSetMemberFinancesDto(List<int> FinanceIds);
record MgrMarkBilledDto(long MemberId, string? InvoiceNo = null, string? BillBase64 = null, string? BillExt = null);

record MgrCourierUpdateDto(decimal? RepoCharges, decimal? Advance, string? CourierYn,
    string? BankerAddress, string? PodNumber, string? BillingAction);
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
record IntegrationLoginDto(string Username, string Password);
record IntegrationUploadDto(
    string Username, string Password, string? FileName, string? VehicleType,
    List<string> Headers, List<List<string>> Rows);

sealed class NoopResult : IResult
{
    public Task ExecuteAsync(HttpContext httpContext) => Task.CompletedTask;
}
