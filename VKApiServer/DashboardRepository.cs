using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using VKApiServer.Models;

namespace VKApiServer;

internal static class DashboardRepository
{
    public static async Task<HomeDashboardResponse> BuildHomeDashboardAsync(IMongoDatabase db)
    {
        var overviewTask = BuildOverviewAsync(db);
        var collectionsTask = BuildCollectionMetricsAsync(db);
        var uploadsTask = GetRecentUploadsAsync(db, 6);
        var detailsTask = GetRecentDetailsAsync(db, 8);
        var branchesTask = GetTopBranchesAsync(db, 8);

        await Task.WhenAll(overviewTask, collectionsTask, uploadsTask, detailsTask, branchesTask);

        return new HomeDashboardResponse
        {
            Overview = overviewTask.Result,
            Collections = collectionsTask.Result,
            RecentUploads = uploadsTask.Result,
            RecentDetails = detailsTask.Result,
            TopBranches = branchesTask.Result
        };
    }

    public static async Task<VehicleSearchResponse> BuildVehicleSearchAsync(IMongoDatabase db, string? query, string? mode)
    {
        var vehicleMode = string.Equals(mode, "chassis", StringComparison.OrdinalIgnoreCase)
            ? "chassis"
            : "rc";

        var vehicles = db.GetCollection<BsonDocument>("vehicles");
        var searchTerm = (query ?? string.Empty).Trim();
        var filter = BuildVehicleFilter(searchTerm, vehicleMode);
        
        // Sorting alphabetically by RC/Chassis instead of by Update time
        var sortField = vehicleMode == "chassis" ? "chassis_no" : "rc_no";
        var sort = Builders<BsonDocument>.Sort.Ascending(sortField);

        var docs = await vehicles.Find(filter).Sort(sort).Limit(150).ToListAsync();
        var branchIds = docs
            .Select(doc => GetString(doc, "branch_id"))
            .Where(id => ObjectId.TryParse(id, out _))
            .Select(ObjectId.Parse)
            .Distinct()
            .ToList();

        var branches = await db.GetCollection<BsonDocument>("branches")
            .Find(Builders<BsonDocument>.Filter.In("_id", branchIds))
            .ToListAsync();

        var branchLookup = branches.ToDictionary(GetId, doc => doc);
        var results = docs.Select(doc => MapVehicle(doc, branchLookup)).ToList();

        return new VehicleSearchResponse
        {
            Query = searchTerm,
            Mode = vehicleMode,
            ResultCount = results.Count,
            UniqueBranches = results
                .Select(item => item.BranchName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Results = results
        };
    }

    public static async Task<FinanceDashboardResponse> BuildFinanceDashboardAsync(IMongoDatabase db)
    {
        var headOfficeCount = await db.GetCollection<BsonDocument>("head_offices").EstimatedDocumentCountAsync();
        var branchCount = await db.GetCollection<BsonDocument>("branches").EstimatedDocumentCountAsync();
        var recordCount = await db.GetCollection<BsonDocument>("vehicles").EstimatedDocumentCountAsync();
        var uploadCount = await db.GetCollection<BsonDocument>("file_info").EstimatedDocumentCountAsync();

        return new FinanceDashboardResponse
        {
            TotalHeadOffices = (long)headOfficeCount,
            TotalBranches = (long)branchCount,
            TotalRecords = (long)recordCount,
            TotalUploads = (long)uploadCount,
            TopBranches = await GetTopBranchesAsync(db, 18),
            RecentUploads = await GetRecentUploadsAsync(db, 10),
            Banks = await GetBanksAsync(db)
        };
    }

    public static async Task<UsersDashboardResponse> BuildUsersDashboardAsync(IMongoDatabase db)
    {
        var usersCollection = db.GetCollection<BsonDocument>("users");
        var plansCollection = db.GetCollection<BsonDocument>("user_plan");
        var devicesCollection = db.GetCollection<BsonDocument>("deviceids");

        var users = await usersCollection
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
            .Limit(100)
            .ToListAsync();

        var userIds = users.Select(GetId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        var objectIds = userIds
            .Where(id => ObjectId.TryParse(id, out _))
            .Select(ObjectId.Parse)
            .ToList();

        var plans = await plansCollection
            .Find(Builders<BsonDocument>.Filter.In("user_id", objectIds))
            .ToListAsync();

        var latestPlanByUser = plans
            .Where(doc => !string.IsNullOrWhiteSpace(GetString(doc, "user_id")))
            .GroupBy(doc => GetString(doc, "user_id"))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(doc => GetSortableDate(doc, "endDate")).First());

        var totalUsers = await usersCollection.EstimatedDocumentCountAsync();
        var activeUsers = await usersCollection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("status", "ACTIVE"));
        var adminUsers = await usersCollection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("role", "ADMIN"));
        var totalPlans = await plansCollection.EstimatedDocumentCountAsync();
        var registeredDevices = await devicesCollection.EstimatedDocumentCountAsync();

        return new UsersDashboardResponse
        {
            TotalUsers = (long)totalUsers,
            ActiveUsers = activeUsers,
            AdminUsers = adminUsers,
            TotalPlans = (long)totalPlans,
            RegisteredDevices = (long)registeredDevices,
            Users = users.Select(user =>
            {
                var id = GetId(user);
                latestPlanByUser.TryGetValue(id, out var plan);

                return new UserSummaryItem
                {
                    UserId = id,
                    FullName = GetString(user, "name"),
                    MobileNo = GetString(user, "mobile"),
                    Address = GetString(user, "address"),
                    Role = GetString(user, "role"),
                    Status = GetString(user, "status"),
                    BranchCount = GetArrayCount(user, "branchId"),
                    DeviceId = GetString(user, "deviceId"),
                    RequestDeviceId = GetString(user, "requestDeviceId"),
                    PlanEndDate = FormatDate(plan, "endDate"),
                    CreatedOn = FormatDate(user, "createdAt")
                };
            }).ToList(),
            PlanAlerts = latestPlanByUser.Values
                .OrderBy(doc => GetSortableDate(doc, "endDate") ?? DateTime.MaxValue)
                .Take(12)
                .Select(doc =>
                {
                    var userId = GetString(doc, "user_id");
                    var user = users.FirstOrDefault(item => GetId(item) == userId);
                    return new PlanSummaryItem
                    {
                        UserName = GetString(user, "name"),
                        MobileNo = GetString(user, "mobile"),
                        StartDate = FormatDate(doc, "startDate"),
                        EndDate = FormatDate(doc, "endDate")
                    };
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.UserName))
                .ToList()
        };
    }

    public static async Task<UploadsDashboardResponse> BuildUploadsDashboardAsync(IMongoDatabase db)
    {
        var filesCollection = db.GetCollection<BsonDocument>("file_info");
        var latest = await filesCollection
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
            .Limit(1)
            .FirstOrDefaultAsync();

        return new UploadsDashboardResponse
        {
            TotalFiles = (long)await filesCollection.EstimatedDocumentCountAsync(),
            TotalBanks = (long)await db.GetCollection<BsonDocument>("webhook_banks").EstimatedDocumentCountAsync(),
            TotalHeaders = (long)await db.GetCollection<BsonDocument>("headers").EstimatedDocumentCountAsync(),
            LatestUpload = FormatDate(latest, "createdAt"),
            Files = await GetRecentUploadsAsync(db, 18),
            Banks = await GetBanksAsync(db)
        };
    }

    public static async Task<DetailsDashboardResponse> BuildDetailsDashboardAsync(IMongoDatabase db)
    {
        var detailsCollection = db.GetCollection<BsonDocument>("details");
        var allItems = await detailsCollection
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
            .Limit(60)
            .ToListAsync();

        var totalViews = await detailsCollection.EstimatedDocumentCountAsync();
        var foundCount = await detailsCollection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("vehicle_status", "FOUND"));
        var notFoundCount = await detailsCollection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("vehicle_status", "NOT FOUND"));

        var userIds = allItems
            .Select(item => GetString(item, "user_id"))
            .Where(id => ObjectId.TryParse(id, out _))
            .Select(ObjectId.Parse)
            .Distinct()
            .ToList();

        var users = await db.GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.In("_id", userIds))
            .ToListAsync();

        var userLookup = users.ToDictionary(GetId, doc => doc);

        var locations = allItems
            .Select(item => GetString(item, "location"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .Take(10)
            .Select(group => new NamedCountItem
            {
                Name = group.Key,
                Count = group.Count(),
                Detail = "Recent details views"
            })
            .ToList();

        return new DetailsDashboardResponse
        {
            TotalViews = (long)totalViews,
            FoundCount = foundCount,
            NotFoundCount = notFoundCount,
            UniqueUsers = allItems.Select(item => GetString(item, "user_id")).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count(),
            Items = allItems.Select(item =>
            {
                var userId = GetString(item, "user_id");
                userLookup.TryGetValue(userId, out var user);
                return MapDetail(item, user);
            }).ToList(),
            Locations = locations
        };
    }

    public static async Task<OtpDashboardResponse> BuildOtpDashboardAsync(IMongoDatabase db)
    {
        var otpCollection = db.GetCollection<BsonDocument>("otp");
        var items = await otpCollection
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("updatedAt").Descending("createdAt"))
            .Limit(100)
            .ToListAsync();

        var userIds = items
            .Select(item => GetString(item, "user_id"))
            .Where(id => ObjectId.TryParse(id, out _))
            .Select(ObjectId.Parse)
            .Distinct()
            .ToList();

        var users = await db.GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.In("_id", userIds))
            .ToListAsync();

        var userLookup = users.ToDictionary(GetId, doc => doc);
        var dayAgo = DateTime.UtcNow.AddDays(-1);

        return new OtpDashboardResponse
        {
            TotalOtps = (long)await otpCollection.EstimatedDocumentCountAsync(),
            TotalUsers = (long)await db.GetCollection<BsonDocument>("users").EstimatedDocumentCountAsync(),
            Last24Hours = items.Count(item => (GetSortableDate(item, "updatedAt") ?? DateTime.MinValue) >= dayAgo),
            Items = items.Select(item =>
            {
                userLookup.TryGetValue(GetString(item, "user_id"), out var user);
                return new OtpItem
                {
                    UserName = GetString(user, "name"),
                    UserMobile = GetString(user, "mobile"),
                    Otp = GetString(item, "otp"),
                    UpdatedOn = FormatDate(item, "updatedAt"),
                    Status = "RECORDED"
                };
            }).ToList()
        };
    }

    public static async Task<ReportsDashboardResponse> BuildReportsDashboardAsync(IMongoDatabase db)
    {
        return new ReportsDashboardResponse
        {
            TotalVehicles = (long)await db.GetCollection<BsonDocument>("vehicles").EstimatedDocumentCountAsync(),
            TotalUploads = (long)await db.GetCollection<BsonDocument>("file_info").EstimatedDocumentCountAsync(),
            TotalBranches = (long)await db.GetCollection<BsonDocument>("branches").EstimatedDocumentCountAsync(),
            TotalUsers = (long)await db.GetCollection<BsonDocument>("users").EstimatedDocumentCountAsync(),
            Collections = await BuildCollectionMetricsAsync(db),
            TopBranches = await GetTopBranchesAsync(db, 12),
            TopBanks = await GetTopBanksAsync(db)
        };
    }

    public static async Task<PaymentsDashboardResponse> BuildPaymentsDashboardAsync(IMongoDatabase db)
    {
        return new PaymentsDashboardResponse
        {
            TotalBanks = (long)await db.GetCollection<BsonDocument>("webhook_banks").EstimatedDocumentCountAsync(),
            TotalBillings = (long)await db.GetCollection<BsonDocument>("billings").EstimatedDocumentCountAsync(),
            WebhookUsers = (long)await db.GetCollection<BsonDocument>("webhook_users").EstimatedDocumentCountAsync(),
            TotalUploads = (long)await db.GetCollection<BsonDocument>("file_info").EstimatedDocumentCountAsync(),
            StatusNote = "Billing and payment posting are being staged for the desktop flow. The shell is ready and the related Mongo collections are connected.",
            PaymentMethods = new List<PaymentMethodItem>
            {
                new() { PaymentMethodId = 1, MethodName = "Cash Counter", Details = "Manual counter collection", IsActive = true },
                new() { PaymentMethodId = 2, MethodName = "Bank Transfer", Details = "Tie to webhook bank workflows", IsActive = true },
                new() { PaymentMethodId = 3, MethodName = "UPI", Details = "To be activated in the desktop payment flow", IsActive = true },
                new() { PaymentMethodId = 4, MethodName = "Billing Ledger", Details = "Reserved for future billing automation", IsActive = false }
            },
            Banks = await GetBanksAsync(db),
            RecentUploads = await GetRecentUploadsAsync(db, 8)
        };
    }

    public static async Task<ModuleStatusResponse> BuildModuleStatusAsync(IMongoDatabase db, string moduleKey)
    {
        var normalized = (moduleKey ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "confirmations" => await BuildSimpleModuleAsync(
                db,
                "confirmations",
                "Repo confirmations are not populated in the current Mongo cluster yet.",
                ("repoconformations", "Live repo confirmation rows"),
                ("vehicles", "Source record base"),
                ToTimeline(await GetRecentUploadsAsync(db, 5))),
            "feedbacks" => await BuildSimpleModuleAsync(
                db,
                "feedbacks",
                "Feedback workflows are still pending for the desktop rollout. The dashboard is wired so the module can light up once data starts flowing.",
                ("finalworks", "Feedback work queue"),
                ("webhook_users", "Support integration users"),
                ToTimeline(await GetRecentUploadsAsync(db, 5))),
            "blacklist" => await BuildSimpleModuleAsync(
                db,
                "blacklist",
                "Blacklist operations are staged. The backing collection exists but currently has no live rows.",
                ("confiscatedvehicles", "Watchlist rows"),
                ("vehicles", "Vehicle master data"),
                ToTimeline(await GetRecentDetailsAsync(db, 5))),
            "billing" => await BuildSimpleModuleAsync(
                db,
                "billing",
                "Billing automation is planned next. The desktop shell is in place and connected to the live Mongo environment.",
                ("billings", "Billing entries"),
                ("webhook_banks", "Bank connectors"),
                ToTimeline(await GetRecentUploadsAsync(db, 5))),
            "cleanfile" => await BuildSimpleModuleAsync(
                db,
                "cleanfile",
                "Clean-file processing is still a staged utility. Header definitions and upload history are already connected for the future implementation.",
                ("headers", "Column templates"),
                ("file_info", "Uploaded source files"),
                ToTimeline(await GetRecentUploadsAsync(db, 5))),
            "controlpin" => await BuildSimpleModuleAsync(
                db,
                "controlpin",
                "Mobile control security actions will be added in a later pass. Device assignment data is already visible from MongoDB.",
                ("deviceids", "Registered devices"),
                ("users", "User access base"),
                ToTimeline(await GetRecentDetailsAsync(db, 5))),
            _ => await BuildSimpleModuleAsync(
                db,
                normalized,
                "This dashboard module has been reserved in the desktop shell and can be expanded next.",
                ("vehicles", "Vehicle master data"),
                ("users", "User access base"),
                ToTimeline(await GetRecentUploadsAsync(db, 5)))
        };
    }

    private static async Task<OverviewCard> BuildOverviewAsync(IMongoDatabase db)
    {
        var vehicles = db.GetCollection<BsonDocument>("vehicles");
        var branches = db.GetCollection<BsonDocument>("branches");
        var headOffices = db.GetCollection<BsonDocument>("head_offices");
        var users = db.GetCollection<BsonDocument>("users");
        var details = db.GetCollection<BsonDocument>("details");
        var uploads = db.GetCollection<BsonDocument>("file_info");
        var otps = db.GetCollection<BsonDocument>("otp");
        var billings = db.GetCollection<BsonDocument>("billings");

        var totalVehicles = vehicles.EstimatedDocumentCountAsync();
        var totalBranches = branches.EstimatedDocumentCountAsync();
        var totalHeadOffices = headOffices.EstimatedDocumentCountAsync();
        var totalUsers = users.EstimatedDocumentCountAsync();
        var activeUsers = users.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("status", "ACTIVE"));
        var adminUsers = users.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("role", "ADMIN"));
        var totalDetails = details.EstimatedDocumentCountAsync();
        var foundDetails = details.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("vehicle_status", "FOUND"));
        var totalUploads = uploads.EstimatedDocumentCountAsync();
        var totalOtps = otps.EstimatedDocumentCountAsync();
        var totalBillings = billings.EstimatedDocumentCountAsync();

        await Task.WhenAll(totalVehicles, totalBranches, totalHeadOffices, totalUsers, activeUsers,
            adminUsers, totalDetails, foundDetails, totalUploads, totalOtps, totalBillings);

        return new OverviewCard
        {
            TotalRecords = (long)totalVehicles.Result,
            TotalBranches = (long)totalBranches.Result,
            TotalHeadOffices = (long)totalHeadOffices.Result,
            TotalUsers = (long)totalUsers.Result,
            ActiveUsers = activeUsers.Result,
            AdminUsers = adminUsers.Result,
            TotalDetailViews = (long)totalDetails.Result,
            FoundDetails = foundDetails.Result,
            TotalUploads = (long)totalUploads.Result,
            TotalOtps = (long)totalOtps.Result,
            TotalBillings = (long)totalBillings.Result
        };
    }

    private static async Task<List<CollectionMetric>> BuildCollectionMetricsAsync(IMongoDatabase db)
    {
        var collections = await db.ListCollectionNames().ToListAsync();
        var ordered = collections
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<CollectionMetric>(ordered.Count);
        foreach (var name in ordered)
        {
            var count = await db.GetCollection<BsonDocument>(name).EstimatedDocumentCountAsync();
            results.Add(new CollectionMetric
            {
                Name = name,
                Count = (long)count,
                Summary = count == 0 ? "Waiting for module data" : "Live MongoDB data"
            });
        }

        return results;
    }

    private static FilterDefinition<BsonDocument> BuildVehicleFilter(string query, string mode)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Builders<BsonDocument>.Filter.Empty;
        }

        var sanitized = SanitizeAlphaNumeric(query);
        var isDigitsOnly = sanitized.All(char.IsDigit);

        // 1 to 5 ms Instant Search Logic using EXACT MATCH EQUALS
        // Using strict .Eq triggers an immediate index lookup without $in branching
        if (mode == "chassis" && isDigitsOnly && sanitized.Length == 4)
        {
            var parsedVal = int.TryParse(sanitized, out var v) ? v.ToString() : sanitized;
            return Builders<BsonDocument>.Filter.Eq("last_four_digit_chassis", parsedVal);
        }

        if (mode == "rc" && isDigitsOnly && sanitized.Length == 4)
        {
            var parsedVal = int.TryParse(sanitized, out var v) ? v.ToString() : sanitized;
            return Builders<BsonDocument>.Filter.Eq("last_four_digit_rc", parsedVal);
        }

        // Fallback for partial/full queries
        var field = mode == "chassis" ? "chassis_no" : "rc_no";
        var lastFourField = mode == "chassis" ? "last_four_digit_chassis" : "last_four_digit_rc";
        var upper = query.Trim().ToUpperInvariant();
        var filters = new List<FilterDefinition<BsonDocument>>();

        var lastFour = GetLastFourDigits(sanitized);
        if (!string.IsNullOrWhiteSpace(lastFour))
        {
            filters.Add(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq(lastFourField, lastFour),
                Builders<BsonDocument>.Filter.Eq(lastFourField, int.TryParse(lastFour, out var value) ? value : -1)));
        }

        filters.Add(Builders<BsonDocument>.Filter.Regex(
            field,
            new BsonRegularExpression(Regex.Escape(upper), "i")));

        if (!string.Equals(sanitized, upper, StringComparison.Ordinal))
        {
            filters.Add(Builders<BsonDocument>.Filter.Regex(
                field,
                new BsonRegularExpression(Regex.Escape(sanitized), "i")));
        }

        return Builders<BsonDocument>.Filter.Or(filters);
    }

    private static async Task<ModuleStatusResponse> BuildSimpleModuleAsync(
        IMongoDatabase db,
        string key,
        string banner,
        (string CollectionName, string Detail) primaryCollection,
        (string CollectionName, string Detail) secondaryCollection,
        List<TimelineItem> recentItems)
    {
        var primaryCount = await db.GetCollection<BsonDocument>(primaryCollection.CollectionName).EstimatedDocumentCountAsync();
        var secondaryCount = await db.GetCollection<BsonDocument>(secondaryCollection.CollectionName).EstimatedDocumentCountAsync();

        return new ModuleStatusResponse
        {
            ModuleKey = key,
            Title = ToTitleCase(key.Replace("controlpin", "mobile control pin")),
            Subtitle = "Desktop showcase module",
            Banner = banner,
            Primary = new MetricCard
            {
                Label = primaryCollection.CollectionName,
                Value = primaryCount.ToString("N0"),
                Description = primaryCollection.Detail
            },
            Secondary = new MetricCard
            {
                Label = secondaryCollection.CollectionName,
                Value = secondaryCount.ToString("N0"),
                Description = secondaryCollection.Detail
            },
            Highlights = new List<NamedCountItem>
            {
                new() { Name = "Local shell ready", Count = 1, Detail = "UI module is available from the dashboard" },
                new() { Name = "Mongo connected", Count = 1, Detail = "Reading from the configured local environment" },
                new() { Name = "Production switchable", Count = 1, Detail = "API base URL remains configurable from desktop settings" }
            },
            Collections = new List<CollectionMetric>
            {
                new()
                {
                    Name = primaryCollection.CollectionName,
                    Count = (long)primaryCount,
                    Summary = primaryCollection.Detail
                },
                new()
                {
                    Name = secondaryCollection.CollectionName,
                    Count = (long)secondaryCount,
                    Summary = secondaryCollection.Detail
                }
            },
            RecentItems = recentItems
        };
    }

    private static async Task<List<BranchSummaryItem>> GetTopBranchesAsync(IMongoDatabase db, int limit)
    {
        var branchDocs = await db.GetCollection<BsonDocument>("branches")
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("records").Descending("updatedAt"))
            .Limit(limit)
            .ToListAsync();

        var headOfficeIds = branchDocs
            .Select(doc => GetString(doc, "head_office_id"))
            .Where(id => ObjectId.TryParse(id, out _))
            .Select(ObjectId.Parse)
            .Distinct()
            .ToList();

        var headOffices = await db.GetCollection<BsonDocument>("head_offices")
            .Find(Builders<BsonDocument>.Filter.In("_id", headOfficeIds))
            .ToListAsync();

        var headOfficeLookup = headOffices.ToDictionary(GetId, doc => GetString(doc, "name"));

        return branchDocs.Select(branch =>
        {
            var headOfficeId = GetString(branch, "head_office_id");
            headOfficeLookup.TryGetValue(headOfficeId, out var headOfficeName);
            var contactOne = branch.TryGetValue("contact_one", out var contactValue) && contactValue.IsBsonDocument
                ? contactValue.AsBsonDocument
                : null;

            return new BranchSummaryItem
            {
                BranchId = GetId(branch),
                BranchName = GetString(branch, "name"),
                HeadOfficeName = headOfficeName ?? string.Empty,
                Records = GetLong(branch, "records"),
                ContactPerson = GetString(contactOne, "name"),
                ContactMobile = GetString(contactOne, "mobile"),
                UpdatedOn = FormatDate(branch, "updatedAt")
            };
        }).ToList();
    }

    private static async Task<List<UploadFileItem>> GetRecentUploadsAsync(IMongoDatabase db, int limit)
    {
        var uploads = await db.GetCollection<BsonDocument>("file_info")
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
            .Limit(limit)
            .ToListAsync();

        var bankIds = uploads
            .Select(doc => GetString(doc, "bankId"))
            .Where(id => ObjectId.TryParse(id, out _))
            .Select(ObjectId.Parse)
            .Distinct()
            .ToList();

        var banks = await db.GetCollection<BsonDocument>("webhook_banks")
            .Find(Builders<BsonDocument>.Filter.In("_id", bankIds))
            .ToListAsync();

        var bankLookup = banks.ToDictionary(GetId, doc => GetString(doc, "bank_name"));

        return uploads.Select(upload =>
        {
            var bankId = GetString(upload, "bankId");
            bankLookup.TryGetValue(bankId, out var bankName);
            return new UploadFileItem
            {
                FileName = GetString(upload, "file_name"),
                BankName = bankName ?? string.Empty,
                VehicleType = GetString(upload, "vehicle_type"),
                UploadedBy = GetString(upload, "uploaded_by"),
                UploadedDate = GetString(upload, "uploaded_date"),
                CreatedOn = FormatDate(upload, "createdAt")
            };
        }).ToList();
    }

    private static async Task<List<DetailViewItem>> GetRecentDetailsAsync(IMongoDatabase db, int limit)
    {
        var details = await db.GetCollection<BsonDocument>("details")
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
            .Limit(limit)
            .ToListAsync();

        var userIds = details
            .Select(doc => GetString(doc, "user_id"))
            .Where(id => ObjectId.TryParse(id, out _))
            .Select(ObjectId.Parse)
            .Distinct()
            .ToList();

        var users = await db.GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.In("_id", userIds))
            .ToListAsync();

        var userLookup = users.ToDictionary(GetId, doc => doc);

        return details.Select(detail =>
        {
            userLookup.TryGetValue(GetString(detail, "user_id"), out var user);
            return MapDetail(detail, user);
        }).ToList();
    }

    private static DetailViewItem MapDetail(BsonDocument detail, BsonDocument? user) =>
        new()
        {
            VehicleNo = GetString(detail, "rc_no"),
            ChassisNo = GetString(detail, "chassis_no"),
            EngineNo = GetString(detail, "engine_no"),
            Model = GetString(detail, "mek_and_model"),
            UserName = GetString(user, "name"),
            UserMobile = GetString(user, "mobile"),
            Location = GetString(detail, "location"),
            VehicleStatus = GetString(detail, "vehicle_status"),
            CreatedOn = FormatDate(detail, "createdAt")
        };

    private static async Task<List<NamedCountItem>> GetBanksAsync(IMongoDatabase db)
    {
        var banks = await db.GetCollection<BsonDocument>("webhook_banks")
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("bank_name"))
            .ToListAsync();

        return banks.Select(bank => new NamedCountItem
        {
            Name = GetString(bank, "bank_name"),
            Count = 1,
            Detail = "Connected webhook bank"
        }).ToList();
    }

    private static async Task<List<NamedCountItem>> GetTopBanksAsync(IMongoDatabase db)
    {
        var uploads = await db.GetCollection<BsonDocument>("file_info")
            .Find(Builders<BsonDocument>.Filter.Empty)
            .ToListAsync();

        var banks = await db.GetCollection<BsonDocument>("webhook_banks")
            .Find(Builders<BsonDocument>.Filter.Empty)
            .ToListAsync();

        var bankLookup = banks.ToDictionary(GetId, doc => GetString(doc, "bank_name"));

        return uploads
            .GroupBy(doc => GetString(doc, "bankId"))
            .Select(group =>
            {
                bankLookup.TryGetValue(group.Key, out var name);
                return new NamedCountItem
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "UNKNOWN BANK" : name,
                    Count = group.LongCount(),
                    Detail = "Uploaded files"
                };
            })
            .OrderByDescending(item => item.Count)
            .Take(10)
            .ToList();
    }

    private static List<TimelineItem> ToTimeline(IEnumerable<UploadFileItem> uploads) =>
        uploads.Select(item => new TimelineItem
        {
            Title = item.FileName,
            Subtitle = item.BankName,
            Detail = string.IsNullOrWhiteSpace(item.UploadedBy)
                ? item.VehicleType
                : $"{item.UploadedBy} · {item.VehicleType}",
            Timestamp = item.CreatedOn
        }).ToList();

    private static List<TimelineItem> ToTimeline(IEnumerable<DetailViewItem> details) =>
        details.Select(item => new TimelineItem
        {
            Title = item.VehicleNo,
            Subtitle = item.UserName,
            Detail = string.IsNullOrWhiteSpace(item.Location)
                ? item.VehicleStatus
                : $"{item.VehicleStatus} · {item.Location}",
            Timestamp = item.CreatedOn
        }).ToList();

    private static VehicleSearchItem MapVehicle(BsonDocument doc, IReadOnlyDictionary<string, BsonDocument> branchLookup)
    {
        var branchName = GetString(doc, "branch");
        BsonDocument? branchDoc = null;
        if (branchLookup.TryGetValue(GetString(doc, "branch_id"), out var b))
        {
            branchDoc = b;
            if (string.IsNullOrWhiteSpace(branchName))
            {
                branchName = GetString(branchDoc, "name");
            }
        }

        string FormatContact(BsonDocument? contactDoc)
        {
            if (contactDoc == null) return string.Empty;
            var name = GetString(contactDoc, "name");
            var mobile = GetString(contactDoc, "mobile");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(mobile)) return $"{name} | {mobile}";
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return mobile;
        }

        var contactOne = branchDoc != null && branchDoc.Contains("contact_one") && branchDoc["contact_one"].IsBsonDocument
            ? FormatContact(branchDoc["contact_one"].AsBsonDocument) : string.Empty;
        var contactTwo = branchDoc != null && branchDoc.Contains("contact_two") && branchDoc["contact_two"].IsBsonDocument
            ? FormatContact(branchDoc["contact_two"].AsBsonDocument) : string.Empty;
        var contactThree = branchDoc != null && branchDoc.Contains("contact_three") && branchDoc["contact_three"].IsBsonDocument
            ? FormatContact(branchDoc["contact_three"].AsBsonDocument) : string.Empty;

        return new VehicleSearchItem
        {
            // Record Identifier
            Id = GetId(doc),

            // Vehicle
            ReleaseStatus = GetString(doc, "is_released"),
            VehicleNo = GetString(doc, "rc_no"),
            ChassisNo = GetString(doc, "chassis_no"),
            Model = GetString(doc, "mek_and_model"),
            EngineNo = GetString(doc, "engine_no"),

            // Customer
            CustomerName = GetString(doc, "customer_name"),
            CustomerContactNos = GetString(doc, "customer_contact_nos"),
            CustomerAddress = GetString(doc, "customer_address"),

            // Finance
            Financer = GetString(doc, "financer"),
            BranchName = branchName ?? string.Empty,
            FirstContactDetails = contactOne,
            SecondContactDetails = contactTwo,
            ThirdContactDetails = contactThree,
            Address = "", // Kept empty as per db schema
            BranchFromExcel = GetString(doc, "branch"),
            ExecutiveName = GetString(doc, "ex_name"),
            Level1 = GetString(doc, "level1"),
            Level1ContactNos = GetString(doc, "level1con"),
            Level2 = GetString(doc, "level2"),
            Level2ContactNos = GetString(doc, "level2con"),
            Level3 = GetString(doc, "level3"),
            Level3ContactNos = GetString(doc, "level3con"),
            Level4 = GetString(doc, "level4"),
            Level4ContactNos = GetString(doc, "level4con"),

            // Loan
            AgreementNo = GetString(doc, "contract_no"),
            Region = GetString(doc, "region"),
            Area = GetString(doc, "area"),
            Bucket = GetString(doc, "bkt"),
            GV = GetString(doc, "gv"),
            OD = GetString(doc, "od"),
            Sec9Available = GetString(doc, "ses9"),
            Sec17Available = GetString(doc, "ses17"),
            TBRFlag = GetString(doc, "tbr"),
            Seasoning = GetString(doc, "seasoning"),
            SenderMailId1 = GetString(doc, "sender_mail_id_1"),
            SenderMailId2 = GetString(doc, "sender_mail_id_2"),
            POS = GetString(doc, "poss"),
            TOSS = GetString(doc, "toss"),
            Remark = GetString(doc, "remark"),

            // Other
            CreatedOn = FormatDate(doc, "createdAt"),
            UpdatedOn = FormatDate(doc, "updatedAt")
        };
    }

    private static string GetId(BsonDocument? doc)
    {
        if (doc is null || !doc.TryGetValue("_id", out var value) || value.IsBsonNull)
        {
            return string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    private static string GetString(BsonDocument? doc, string key)
    {
        if (doc is null || !doc.TryGetValue(key, out var value) || value.IsBsonNull)
        {
            return string.Empty;
        }

        return value.BsonType switch
        {
            BsonType.String => value.AsString,
            BsonType.ObjectId => value.AsObjectId.ToString(),
            BsonType.Int32 => value.AsInt32.ToString(),
            BsonType.Int64 => value.AsInt64.ToString(),
            BsonType.Double => value.AsDouble.ToString("0.##"),
            BsonType.Boolean => value.AsBoolean ? "YES" : "NO",
            _ => value.ToString()
        } ?? string.Empty;
    }

    private static long GetLong(BsonDocument? doc, string key)
    {
        if (doc is null || !doc.TryGetValue(key, out var value) || value.IsBsonNull)
        {
            return 0;
        }

        return value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.Double => (long)value.AsDouble,
            BsonType.String when long.TryParse(value.AsString, out var parsed) => parsed,
            _ => 0
        };
    }

    private static int GetArrayCount(BsonDocument? doc, string key)
    {
        if (doc is null || !doc.TryGetValue(key, out var value) || value.IsBsonNull || !value.IsBsonArray)
        {
            return 0;
        }

        return value.AsBsonArray.Count;
    }

    private static DateTime? GetSortableDate(BsonDocument? doc, string key)
    {
        if (doc is null || !doc.TryGetValue(key, out var value) || value.IsBsonNull)
        {
            return null;
        }

        if (value.BsonType == BsonType.DateTime)
        {
            return value.ToUniversalTime();
        }

        return value.BsonType == BsonType.String && DateTime.TryParse(value.AsString, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatDate(BsonDocument? doc, string key)
    {
        var value = GetSortableDate(doc, key);
        return value?.ToLocalTime().ToString("dd MMM yyyy hh:mm tt") ?? string.Empty;
    }

    private static string GetLastFourDigits(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        var lastFour = digits.Length <= 4 ? digits : digits[^4..];
        return int.TryParse(lastFour, out var parsed) ? parsed.ToString() : lastFour;
    }

    private static string SanitizeAlphaNumeric(string value) =>
        new(value
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Module";
        }

        var words = value
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);

        return string.Join(' ', words);
    }
}
