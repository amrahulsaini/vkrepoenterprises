using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
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

var connStr = new MySqlConnectionStringBuilder
{
    Server   = Environment.GetEnvironmentVariable("MYSQL_HOST")     ?? "127.0.0.1",
    UserID   = Environment.GetEnvironmentVariable("MYSQL_USER")     ?? "vkre_db1",
    Password = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? "db1",
    Database = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "vkre_db1",
    Port     = uint.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var p) ? p : 3306u,
    SslMode  = MySqlSslMode.None,
    Pooling  = true,
    MaximumPoolSize      = 20,
    ConnectionTimeout    = 10,
    DefaultCommandTimeout = 30
}.ConnectionString;

var desktopLoginPassword = Environment.GetEnvironmentVariable("DESKTOP_LOGIN_PASSWORD") ?? "vk@kunal.admin";
var privateKey = Environment.GetEnvironmentVariable("PRIVATEKEY") ?? "vk_enterprises_local_jwt_key";
var port = Environment.GetEnvironmentVariable("PORT") ?? "5002";

var app = builder.Build();
app.UseCors();

app.MapPost("/api/AppUsers/Login", async (LoginRequest request) =>
{
    if (!Regex.IsMatch(request.mobileno ?? string.Empty, @"^\d{10}$"))
        return Results.BadRequest(new { message = "Please enter a valid 10-digit mobile number." });

    if (!string.Equals(request.password, desktopLoginPassword, StringComparison.Ordinal))
        return Results.BadRequest(new { message = "Invalid mobile number or password." });

    int appUserId = 0;
    string fullName = "VK ENTERPRISES ADMIN";

    try
    {
        await using var conn = new MySqlConnection(connStr);
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

    if (string.IsNullOrWhiteSpace(fullName)) fullName = "VK ENTERPRISES ADMIN";
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
        () => DashboardRepository.BuildHomeDashboardAsync(connStr), 30)));

app.MapGet("/api/Records/Search", async (string? q, string? mode) =>
    Results.Ok(await DashboardRepository.BuildVehicleSearchAsync(connStr, q, mode)));

app.MapPost("/api/Records/MarkReleased/{id}", async (string id) =>
{
    try
    {
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();
        await using var getCmd = new MySqlCommand(
            "SELECT is_released FROM records WHERE id = @id LIMIT 1", conn);
        getCmd.Parameters.AddWithValue("@id", id);
        var current = (await getCmd.ExecuteScalarAsync())?.ToString()?.ToUpperInvariant();
        var newStatus = current == "YES" ? "NO" : "YES";
        await using var upd = new MySqlCommand(
            "UPDATE records SET is_released = @s, updated_at = @d WHERE id = @id", conn);
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
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM records WHERE id = @id", conn);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        () => DashboardRepository.BuildFinanceDashboardAsync(connStr), 45)));

app.MapGet("/api/AppUsers", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "users-dashboard",
        () => DashboardRepository.BuildUsersDashboardAsync(connStr), 45)));

app.MapGet("/api/Uploads", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "uploads-dashboard",
        () => DashboardRepository.BuildUploadsDashboardAsync(connStr), 45)));

app.MapGet("/api/DetailsViews", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "details-dashboard",
        () => DashboardRepository.BuildDetailsDashboardAsync(connStr), 30)));

app.MapGet("/api/OTPs", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "otp-dashboard",
        () => DashboardRepository.BuildOtpDashboardAsync(connStr), 30)));

app.MapGet("/api/Reports", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "reports-dashboard",
        () => DashboardRepository.BuildReportsDashboardAsync(connStr), 60)));

app.MapGet("/api/Payments", async (IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "payments-dashboard",
        () => DashboardRepository.BuildPaymentsDashboardAsync(connStr), 45)));

app.MapGet("/api/PaymentMethods", async (IMemoryCache cache) =>
    Results.Ok((await GetCachedAsync(cache, "payments-dashboard",
        () => DashboardRepository.BuildPaymentsDashboardAsync(connStr), 45)).PaymentMethods));

app.MapPost("/api/Confirmations", async (ConfirmationRequest req) =>
{
    try
    {
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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

app.MapGet("/api/Modules/{moduleKey}", async (string moduleKey, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, $"module-{moduleKey}",
        () => DashboardRepository.BuildModuleStatusAsync(connStr, moduleKey), 45)));

app.MapGet("/", () => Results.Ok(new
{
    name = "VK Enterprises API Server",
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
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();
        const string sql = @"
            SELECT f.id, f.name,
                   COALESCE(b.branch_cnt, 0) AS branch_count,
                   COALESCE(b.record_cnt, 0) AS total_records
            FROM finances f
            LEFT JOIN (
                SELECT br.finance_id,
                       COUNT(DISTINCT br.id) AS branch_cnt,
                       COUNT(vr.id)          AS record_cnt
                FROM   branches br
                LEFT JOIN vehicle_records vr ON vr.branch_id = br.id
                WHERE  br.is_active = 1
                GROUP BY br.finance_id
            ) b ON b.finance_id = f.id
            ORDER BY f.name LIMIT 100";
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();
        string sql; MySqlCommand cmd;
        if (financeId.HasValue)
        {
            sql = @"SELECT b.id, b.name,
                           COALESCE(b.contact1,'') AS c1, COALESCE(b.contact2,'') AS c2,
                           COALESCE(b.contact3,'') AS c3, COALESCE(b.address,'')  AS addr,
                           (SELECT COUNT(*) FROM vehicle_records WHERE branch_id = b.id) AS total_records,
                           IFNULL(DATE_FORMAT(b.uploaded_at,'%d %b %y %h:%i %p'),'') AS up,
                           '' AS finance_name,
                           b.finance_id
                    FROM branches b
                    WHERE b.finance_id=@fid AND b.is_active=1
                    ORDER BY total_records DESC LIMIT 100";
            cmd = new MySqlCommand(sql, conn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@fid", financeId.Value);
        }
        else
        {
            sql = @"SELECT b.id, b.name,
                           COALESCE(b.contact1,'') AS c1, COALESCE(b.contact2,'') AS c2,
                           COALESCE(b.contact3,'') AS c3, COALESCE(b.address,'')  AS addr,
                           (SELECT COUNT(*) FROM vehicle_records WHERE branch_id = b.id) AS total_records,
                           IFNULL(DATE_FORMAT(b.uploaded_at,'%d %b %y %h:%i %p'),'') AS up,
                           COALESCE(f.name,'') AS finance_name,
                           b.finance_id
                    FROM branches b LEFT JOIN finances f ON f.id=b.finance_id
                    WHERE b.is_active=1 ORDER BY f.name, b.name LIMIT 500";
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
                   u.balance, u.created_at,
                   (SELECT MAX(s.end_date) FROM subscriptions s WHERE s.user_id = u.id) AS sub_end
            FROM app_users u ORDER BY u.created_at DESC";
        var users = new List<object>();
        await using (var cmd = new MySqlCommand(usersSql, conn) { CommandTimeout = 30 })
        {
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                users.Add(new
                {
                    id         = rdr.GetInt64(0),
                    name       = rdr.GetString(1),
                    mobile     = rdr.GetString(2),
                    address    = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                    pincode    = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                    pfpBase64  = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                    deviceId   = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                    isActive   = rdr.GetBoolean(7),
                    isAdmin    = rdr.GetBoolean(8),
                    balance    = rdr.GetDecimal(9),
                    createdAt  = rdr.GetDateTime(10),
                    subEndDate = rdr.IsDBNull(11) ? null : rdr.GetDateTime(11).ToString("yyyy-MM-dd"),
                });
        }
        return Results.Ok(new { stats = new { total, active, admins, withSub }, users });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/users/stats", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();
        await MgrExec("UPDATE app_users SET device_id=NULL WHERE id=@id", conn, 10, ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/mgr/users/{id:long}/subscriptions", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapDelete("/api/mgr/subscriptions/{id:long}", async (HttpContext ctx, long id) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();
        await MgrExec("DELETE FROM subscriptions WHERE id=@id", conn, 10, ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Dashboard quick stats ──────────────────────────────────────────────────

app.MapGet("/api/mgr/dashboard-stats", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();
        const string sql = @"
            SELECT
                (SELECT COUNT(*) FROM vehicle_records),
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
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
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();
        await MgrExec("DELETE FROM device_change_requests WHERE id=@id", conn, 10, ("@id", id));
        return Results.Ok();
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ── Live users (active in last 15 min) ────────────────────────────────────

app.MapGet("/api/mgr/live-users", async (HttpContext ctx) =>
{
    if (!MgrAuth(ctx, desktopLoginPassword)) return Results.Unauthorized();
    try
    {
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, name, mobile,
                   DATE_FORMAT(last_seen,'%H:%i • %d %b') AS seen,
                   last_lat, last_lng
            FROM app_users
            WHERE last_seen >= NOW() - INTERVAL 15 MINUTE
            ORDER BY last_seen DESC LIMIT 100";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new
            {
                id      = rdr.GetInt64(0),
                name    = rdr.GetString(1),
                mobile  = rdr.GetString(2),
                lastSeen = rdr.GetString(3),
                lat     = rdr.IsDBNull(4) ? (double?)null : rdr.GetDouble(4),
                lng     = rdr.IsDBNull(5) ? (double?)null : rdr.GetDouble(5),
            });
        return Results.Ok(list);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.Run($"http://localhost:{port}");

// Local functions must appear before type declarations (CS8803)
static async Task<T> GetCachedAsync<T>(IMemoryCache cache, string key, Func<Task<T>> factory, int seconds)
{
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
