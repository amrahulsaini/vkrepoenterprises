using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
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

var mongoUrl = Environment.GetEnvironmentVariable("MONGO_URL")
    ?? throw new InvalidOperationException("MONGO_URL is not configured.");
var mongoDbName = Environment.GetEnvironmentVariable("MONGO_DB_NAME") ?? "vers_system";
var desktopLoginPassword = Environment.GetEnvironmentVariable("DESKTOP_LOGIN_PASSWORD") ?? "vk@kunal.admin";
var privateKey = Environment.GetEnvironmentVariable("PRIVATEKEY") ?? "vk_enterprises_local_jwt_key";
var port = Environment.GetEnvironmentVariable("PORT") ?? "5002";

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoUrl));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDbName));

var app = builder.Build();

app.UseCors();

app.MapPost("/api/AppUsers/Login", async (LoginRequest request, IMongoDatabase db) =>
{
    if (!Regex.IsMatch(request.mobileno ?? string.Empty, @"^\d{10}$"))
    {
        return Results.BadRequest(new { message = "Please enter a valid 10-digit mobile number." });
    }

    if (!string.Equals(request.password, desktopLoginPassword, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { message = "Invalid mobile number or password." });
    }

    var users = db.GetCollection<BsonDocument>("users");
    var filter = Builders<BsonDocument>.Filter.Or(
        Builders<BsonDocument>.Filter.Eq("mobile", request.mobileno),
        Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("role", "ADMIN"),
            Builders<BsonDocument>.Filter.Eq("status", "ACTIVE")));

    BsonDocument? user = await users
        .Find(filter)
        .Sort(Builders<BsonDocument>.Sort.Descending("role"))
        .FirstOrDefaultAsync();

    int appUserId = 0;
    if (user != null)
    {
        if (user.TryGetValue("AppUserId", out var appUserField))
        {
            try
            {
                if (appUserField.IsInt32) appUserId = appUserField.AsInt32;
                else if (appUserField.IsInt64) appUserId = (int)appUserField.AsInt64;
                else int.TryParse(appUserField.ToString(), out appUserId);
            }
            catch { int.TryParse(appUserField.ToString(), out appUserId); }
        }
        else if (user.TryGetValue("_id", out var idValue))
        {
            try
            {
                if (idValue.IsInt32) appUserId = idValue.AsInt32;
                else if (idValue.IsInt64) appUserId = (int)idValue.AsInt64;
                else int.TryParse(idValue.ToString(), out appUserId);
            }
            catch { int.TryParse(idValue.ToString(), out appUserId); }
        }
    }
    var fullName = user != null && user.TryGetValue("name", out var nameValue)
        ? nameValue.ToString() ?? string.Empty
        : string.Empty;

    if (string.IsNullOrWhiteSpace(fullName))
    {
        fullName = "VK ENTERPRISES ADMIN";
    }

    var nameParts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var tokenPayload = $"{request.mobileno}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:{privateKey}";

    return Results.Ok(new SignedAppUser
    {
        AppUserId = appUserId,
        MobileNo = request.mobileno ?? string.Empty,
        FirstName = nameParts.FirstOrDefault() ?? "VK",
        LastName = nameParts.Length > 1 ? nameParts[1] : "ADMIN",
        IsActive = true,
        IsAdmin = true,
        Token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tokenPayload))
    });
});

app.MapGet("/api/Overview", async (IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "home-dashboard", () => DashboardRepository.BuildHomeDashboardAsync(db), 30)));

app.MapGet("/api/Records/Search", async (string? q, string? mode, IMongoDatabase db) =>
    Results.Ok(await DashboardRepository.BuildVehicleSearchAsync(db, q, mode)));

app.MapPost("/api/Records/MarkReleased/{id}", async (string id, IMongoDatabase db) =>
{
    var collection = db.GetCollection<BsonDocument>("vehicles");
    if (!ObjectId.TryParse(id, out var objectId)) return Results.BadRequest("Invalid ID");
    
    var doc = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", objectId)).FirstOrDefaultAsync();
    var currentStatus = doc?.GetValue("is_released", "").ToString()?.ToUpperInvariant();
    var newStatus = currentStatus == "YES" ? "NO" : "YES";
    
    var update = Builders<BsonDocument>.Update
        .Set("is_released", newStatus)
        .Set("updatedAt", DateTime.UtcNow);
        
    await collection.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", objectId), update);
    return Results.Ok(new { status = newStatus });
});

app.MapDelete("/api/Records/Delete/{id}", async (string id, IMongoDatabase db) =>
{
    var collection = db.GetCollection<BsonDocument>("vehicles");
    if (!ObjectId.TryParse(id, out var objectId)) return Results.BadRequest("Invalid ID");
    
    await collection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", objectId));
    return Results.Ok();
});

app.MapPost("/api/Records/PostRecordsFile", async (HttpRequest req) =>
{
    // Minimal mockup endpoint for Excel record uploads
    return Results.Ok(new { message = "Records uploaded successfully." });
});

app.MapGet("/api/Mapping/GetMappingDetails", async (IMongoDatabase db) =>
{
    var collection = db.GetCollection<BsonDocument>("headers");
    var doc = await collection.Find(new BsonDocument()).FirstOrDefaultAsync();

    var columnTypes = new List<object>
    {
        new { columnTypeId = 1, columnTypeName = "Vehicle No" },
        new { columnTypeId = 2, columnTypeName = "Chasis No" },
        new { columnTypeId = 3, columnTypeName = "Model" },
        new { columnTypeId = 4, columnTypeName = "Engine No" },
        new { columnTypeId = 5, columnTypeName = "Agreement No" },
        new { columnTypeId = 6, columnTypeName = "Customer Name" },
        new { columnTypeId = 7, columnTypeName = "Customer Address" },
        new { columnTypeId = 8, columnTypeName = "Region" },
        new { columnTypeId = 9, columnTypeName = "Area" },
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
        new { columnTypeId = 26, columnTypeName = "Sender Mail Id 1" },
        new { columnTypeId = 27, columnTypeName = "Sender Mail Id 2" },
        new { columnTypeId = 28, columnTypeName = "Executive Name" },
        new { columnTypeId = 29, columnTypeName = "POS" },
        new { columnTypeId = 30, columnTypeName = "TOSS" },
        new { columnTypeId = 31, columnTypeName = "Customer Contact Nos" },
        new { columnTypeId = 32, columnTypeName = "Remark" }
    };

    var mappings = new List<object>();

    if (doc != null)
    {
        var fieldToId = new Dictionary<string, int>
        {
            { "rc_no", 1 }, { "chassis_no", 2 }, { "mek_and_model", 3 }, { "engine_no", 4 },
            { "contract_no", 5 }, { "customer_name", 6 }, { "customer_address", 7 }, { "region", 8 },
            { "area", 9 }, { "bkt", 10 }, { "gv", 11 }, { "od", 12 }, { "branch", 13 },
            { "level1", 14 }, { "level1con", 15 }, { "level2", 16 }, { "level2con", 17 },
            { "level3", 18 }, { "level3con", 19 }, { "level4", 20 }, { "level4con", 21 },
            { "ses9", 22 }, { "ses17", 23 }, { "tbr", 24 }, { "seasoning", 25 },
            { "ex_name", 28 }, { "poss", 29 }, { "toss", 30 }, { "customer_contact_nos", 31 }
        };

        int mappingId = 1;
        foreach (var kvp in fieldToId)
        {
            if (doc.TryGetValue(kvp.Key, out var val) && val.IsBsonArray)
            {
                foreach (var item in val.AsBsonArray)
                {
                    if (item.IsString)
                    {
                        mappings.Add(new
                        {
                            mappingId = mappingId++,
                            columnTypeId = kvp.Value,
                            name = item.AsString
                        });
                    }
                }
            }
        }
    }

    return Results.Ok(new { mappings = mappings, columnTypes = columnTypes });
});

app.MapPost("/api/Mapping/UnMap", () =>
    Results.Ok(new { message = "Unmapped." }));

app.MapPost("/api/Mapping/CreateMapping", () =>
    Results.Ok(new { mappingId = 999, columnTypeId = 1, name = "mapped" }));

app.MapGet("/api/Branches/GetBranches/{financeId}", async (int financeId, IMongoDatabase db, IMemoryCache cache) =>
{
    try
    {
        // First try: Get branches from Finance dashboard TopBranches
        var dashboard = await GetCachedAsync(cache, "finance-dashboard", () => DashboardRepository.BuildFinanceDashboardAsync(db), 45);
        
        if (dashboard?.TopBranches != null && dashboard.TopBranches.Count > 0)
        {
            var branches = dashboard.TopBranches
                .Select(b => new
                {
                    branchId = b.BranchId,
                    branchName = b.BranchName,
                    headOfficeName = b.HeadOfficeName
                })
                .Where(b => !string.IsNullOrWhiteSpace(b.branchId) && !string.IsNullOrWhiteSpace(b.branchName))
                .OrderBy(b => b.branchName)
                .ToList();

            if (branches.Count > 0)
            {
                return Results.Ok(branches);
            }
        }

        // Fallback: Query branches collection directly if dashboard has no branches
        var branchesCollection = db.GetCollection<BsonDocument>("branches");
        var branchDocs = await branchesCollection
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Limit(100)
            .ToListAsync();

        if (branchDocs.Count == 0)
        {
            return Results.Ok(new List<object>());
        }

        var fallbackBranches = branchDocs
            .Select(doc => new
            {
                branchId = GetBranchIdFromDoc(doc),
                branchName = GetBranchNameFromDoc(doc),
                headOfficeName = GetHeadOfficeFromDoc(doc)
            })
            .Where(b => !string.IsNullOrWhiteSpace(b.branchId) && !string.IsNullOrWhiteSpace(b.branchName))
            .OrderBy(b => b.branchName)
            .ToList();

        return Results.Ok(fallbackBranches);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Error loading branches: " + ex.Message, exception = ex.ToString() });
    }
});

static string GetBranchIdFromDoc(BsonDocument doc)
{
    foreach (var key in new[] { "BranchId", "branchId", "_id", "id" })
    {
        if (doc.TryGetValue(key, out var val) && !val.IsBsonNull)
        {
            return val.ToString();
        }
    }
    return string.Empty;
}

static string GetBranchNameFromDoc(BsonDocument doc)
{
    foreach (var key in new[] { "BranchName", "branchName", "Name", "name" })
    {
        if (doc.TryGetValue(key, out var val) && !val.IsBsonNull)
        {
            return val.ToString();
        }
    }
    return string.Empty;
}

static string GetHeadOfficeFromDoc(BsonDocument doc)
{
    foreach (var key in new[] { "HeadOfficeName", "headOfficeName", "HeadOffice", "headOffice" })
    {
        if (doc.TryGetValue(key, out var val) && !val.IsBsonNull)
        {
            return val.ToString();
        }
    }
    return string.Empty;
}

// Debug endpoint to check database state
app.MapGet("/api/Debug/BranchesCollection", async (IMongoDatabase db) =>
{
    try
    {
        var branchesCollection = db.GetCollection<BsonDocument>("branches");
        var count = await branchesCollection.EstimatedDocumentCountAsync();
        var sample = await branchesCollection.Find(Builders<BsonDocument>.Filter.Empty).Limit(3).ToListAsync();
        
        return Results.Ok(new
        {
            totalCount = count,
            sampleRecords = sample.Select(doc => new
            {
                id = GetBranchIdFromDoc(doc),
                name = GetBranchNameFromDoc(doc),
                headOffice = GetHeadOfficeFromDoc(doc),
                raw = doc.ToString()
            }).ToList()
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/Finances", async (IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "finance-dashboard", () => DashboardRepository.BuildFinanceDashboardAsync(db), 45)));

app.MapGet("/api/AppUsers", async (IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "users-dashboard", () => DashboardRepository.BuildUsersDashboardAsync(db), 45)));

app.MapGet("/api/Uploads", async (IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "uploads-dashboard", () => DashboardRepository.BuildUploadsDashboardAsync(db), 45)));

app.MapGet("/api/DetailsViews", async (IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "details-dashboard", () => DashboardRepository.BuildDetailsDashboardAsync(db), 30)));

app.MapGet("/api/OTPs", async (IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "otp-dashboard", () => DashboardRepository.BuildOtpDashboardAsync(db), 30)));

app.MapGet("/api/Reports", async (IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "reports-dashboard", () => DashboardRepository.BuildReportsDashboardAsync(db), 60)));

app.MapGet("/api/Payments", async (IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, "payments-dashboard", () => DashboardRepository.BuildPaymentsDashboardAsync(db), 45)));

app.MapGet("/api/PaymentMethods", async (IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok((await GetCachedAsync(cache, "payments-dashboard", () => DashboardRepository.BuildPaymentsDashboardAsync(db), 45)).PaymentMethods));

app.MapPost("/api/Confirmations", async (ConfirmationRequest req, IMongoDatabase db) =>
{
    var collection = db.GetCollection<BsonDocument>("repoconformations");
    var doc = new BsonDocument
    {
        { "vehicle_no", req.VehicleNo },
        { "chassis_no", req.ChassisNo },
        { "model", req.Model },
        { "engine_no", req.EngineNo },
        { "customer_name", req.CustomerName },
        { "customer_contact_nos", req.CustomerContactNos },
        { "customer_address", req.CustomerAddress },
        { "finance_name", req.FinanceName },
        { "branch_name", req.BranchName },
        { "branch_contact_1", req.BranchFirstContactDetails },
        { "branch_contact_2", req.BranchSecondContactDetails },
        { "branch_contact_3", req.BranchThirdContactDetails },
        { "seizer_id", req.SeizerId },
        { "seizer_name", req.SeizerName },
        { "vehicle_contains_load", req.VehicleContainsLoad },
        { "load_description", req.LoadDescription },
        { "confirm_by", req.ConfirmBy },
        { "status", req.Status },
        { "yard", req.Yard },
        { "apply_amt_credited", req.ApplyAmtCredited },
        { "amount_credited", (double)req.AmountCredited },
        { "createdAt", DateTime.UtcNow },
        { "updatedAt", DateTime.UtcNow }
    };
    await collection.InsertOneAsync(doc);
    return Results.Ok(new { id = doc["_id"].ToString() });
});

app.MapGet("/api/Confirmations", async (IMongoDatabase db) =>
{
    var collection = db.GetCollection<BsonDocument>("repoconformations");
    var docs = await collection.Find(Builders<BsonDocument>.Filter.Empty)
        .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
        .ToListAsync();

    var results = docs.Select(doc => new ConfirmationResponseItem
    {
        Id = doc.GetValue("_id", ObjectId.Empty).ToString() ?? string.Empty,
        VehicleNo = doc.GetValue("vehicle_no", "").AsString,
        ChassisNo = doc.GetValue("chassis_no", "").AsString,
        Model = doc.GetValue("model", "").AsString,
        SeizerName = doc.GetValue("seizer_name", "").AsString,
        Status = doc.GetValue("status", "").AsString,
        ConfirmedOn = doc.TryGetValue("createdAt", out var dt) && dt.IsBsonDateTime
            ? dt.ToUniversalTime().ToLocalTime().ToString("dd-MM-yyyy")
            : ""
    }).ToList();

    return Results.Ok(results);
});

app.MapGet("/api/Modules/{moduleKey}", async (string moduleKey, IMongoDatabase db, IMemoryCache cache) =>
    Results.Ok(await GetCachedAsync(cache, $"module-{moduleKey}", () => DashboardRepository.BuildModuleStatusAsync(db, moduleKey), 45)));

app.MapGet("/", () => Results.Ok(new
{
    name = "VK Enterprises API Server",
    mode = "local",
    port
}));

app.Run($"http://localhost:{port}");

static async Task<T> GetCachedAsync<T>(
    IMemoryCache cache,
    string cacheKey,
    Func<Task<T>> factory,
    int seconds)
{
    if (cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
    {
        return cached;
    }

    var result = await factory();
    cache.Set(cacheKey, result, TimeSpan.FromSeconds(seconds));
    return result;
}
