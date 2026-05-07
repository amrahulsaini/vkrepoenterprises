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
    Server   = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? "localhost",
    UserID   = Environment.GetEnvironmentVariable("MYSQL_USER") ?? "root",
    Password = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? "",
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

app.Run($"http://localhost:{port}");

static async Task<T> GetCachedAsync<T>(IMemoryCache cache, string key, Func<Task<T>> factory, int seconds)
{
    if (cache.TryGetValue(key, out T? cached) && cached is not null)
        return cached;
    var result = await factory();
    cache.Set(key, result, TimeSpan.FromSeconds(seconds));
    return result;
}
