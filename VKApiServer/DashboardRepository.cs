using MySqlConnector;
using VKApiServer.Models;

namespace VKApiServer;

internal static class DashboardRepository
{
    public static async Task<HomeDashboardResponse> BuildHomeDashboardAsync(string connStr)
    {
        var t1 = BuildOverviewAsync(connStr);
        var t2 = BuildTableMetricsAsync(connStr);
        var t3 = GetRecentUploadsAsync(connStr, 6);
        var t4 = GetRecentDetailsAsync(connStr, 8);
        var t5 = GetTopBranchesAsync(connStr, 8);
        await Task.WhenAll(t1, t2, t3, t4, t5);
        return new HomeDashboardResponse
        {
            Overview      = t1.Result,
            Collections   = t2.Result,
            RecentUploads = t3.Result,
            RecentDetails = t4.Result,
            TopBranches   = t5.Result
        };
    }

    public static async Task<VehicleSearchResponse> BuildVehicleSearchAsync(string connStr, string? query, string? mode)
    {
        var vehicleMode = string.Equals(mode, "chassis", StringComparison.OrdinalIgnoreCase) ? "chassis" : "rc";
        var searchTerm  = (query ?? string.Empty).Trim();
        var results     = new List<VehicleSearchItem>();
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            var field = vehicleMode == "chassis" ? "chassis_no" : "rc_no";
            var sql   = string.IsNullOrWhiteSpace(searchTerm)
                ? $"SELECT * FROM records ORDER BY {field} LIMIT 150"
                : $"SELECT * FROM records WHERE {field} LIKE @q ORDER BY {field} LIMIT 150";
            await using var cmd = new MySqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(searchTerm))
                cmd.Parameters.AddWithValue("@q", $"%{searchTerm}%");
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(MapRecordRow(reader));
        }
        catch { }
        return new VehicleSearchResponse
        {
            Query          = searchTerm,
            Mode           = vehicleMode,
            ResultCount    = results.Count,
            UniqueBranches = results.Select(r => r.BranchName).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Results        = results
        };
    }

    public static async Task<FinanceDashboardResponse> BuildFinanceDashboardAsync(string connStr)
    {
        var tBranches = CountAsync(connStr, "SELECT COUNT(*) FROM branches");
        var tRecords  = CountAsync(connStr, "SELECT COUNT(*) FROM records");
        var tUploads  = CountAsync(connStr, "SELECT COUNT(*) FROM file_info");
        var tTop      = GetTopBranchesAsync(connStr, 18);
        var tRecent   = GetRecentUploadsAsync(connStr, 10);
        var tFinances = GetFinancesAsync(connStr);
        await Task.WhenAll(tBranches, tRecords, tUploads, tTop, tRecent, tFinances);
        return new FinanceDashboardResponse
        {
            TotalHeadOffices = 0,
            TotalBranches    = tBranches.Result,
            TotalRecords     = tRecords.Result,
            TotalUploads     = tUploads.Result,
            TopBranches      = tTop.Result,
            RecentUploads    = tRecent.Result,
            Banks            = tFinances.Result
        };
    }

    public static async Task<UsersDashboardResponse> BuildUsersDashboardAsync(string connStr)
    {
        var users  = new List<UserSummaryItem>();
        long total = 0, active = 0, admin = 0;
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            var t1 = CountAsyncConn(conn, "SELECT COUNT(*) FROM users");
            var t2 = CountAsyncConn(conn, "SELECT COUNT(*) FROM users WHERE status='ACTIVE'");
            var t3 = CountAsyncConn(conn, "SELECT COUNT(*) FROM users WHERE role='ADMIN'");
            await Task.WhenAll(t1, t2, t3);
            total = t1.Result; active = t2.Result; admin = t3.Result;
            await using var cmd = new MySqlCommand(
                "SELECT id,name,mobile,address,role,status,created_at FROM users ORDER BY created_at DESC LIMIT 100", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                users.Add(new UserSummaryItem
                {
                    UserId    = Col(reader, 0),
                    FullName  = Col(reader, 1),
                    MobileNo  = Col(reader, 2),
                    Address   = Col(reader, 3),
                    Role      = Col(reader, 4),
                    Status    = Col(reader, 5),
                    CreatedOn = DateCol(reader, 6)
                });
        }
        catch { }
        return new UsersDashboardResponse { TotalUsers = total, ActiveUsers = active, AdminUsers = admin, Users = users };
    }

    public static async Task<UploadsDashboardResponse> BuildUploadsDashboardAsync(string connStr)
    {
        var tFiles   = CountAsync(connStr, "SELECT COUNT(*) FROM file_info");
        var tBanks   = CountAsync(connStr, "SELECT COUNT(*) FROM finances");
        var tRecent  = GetRecentUploadsAsync(connStr, 18);
        var tFinances= GetFinancesAsync(connStr);
        await Task.WhenAll(tFiles, tBanks, tRecent, tFinances);
        return new UploadsDashboardResponse
        {
            TotalFiles   = tFiles.Result,
            TotalBanks   = tBanks.Result,
            TotalHeaders = 0,
            Files        = tRecent.Result,
            Banks        = tFinances.Result
        };
    }

    public static async Task<DetailsDashboardResponse> BuildDetailsDashboardAsync(string connStr)
    {
        var items = new List<DetailViewItem>();
        long total = 0, found = 0, notFound = 0;
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            var t1 = CountAsyncConn(conn, "SELECT COUNT(*) FROM details");
            var t2 = CountAsyncConn(conn, "SELECT COUNT(*) FROM details WHERE vehicle_status='FOUND'");
            var t3 = CountAsyncConn(conn, "SELECT COUNT(*) FROM details WHERE vehicle_status='NOT FOUND'");
            await Task.WhenAll(t1, t2, t3);
            total = t1.Result; found = t2.Result; notFound = t3.Result;
            await using var cmd = new MySqlCommand(
                "SELECT rc_no,chassis_no,engine_no,model,location,vehicle_status,created_at FROM details ORDER BY created_at DESC LIMIT 60", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                items.Add(new DetailViewItem
                {
                    VehicleNo = Col(reader,0), ChassisNo = Col(reader,1), EngineNo = Col(reader,2),
                    Model = Col(reader,3), Location = Col(reader,4), VehicleStatus = Col(reader,5),
                    CreatedOn = DateCol(reader,6)
                });
        }
        catch { }
        return new DetailsDashboardResponse { TotalViews = total, FoundCount = found, NotFoundCount = notFound, Items = items, Locations = new() };
    }

    public static async Task<OtpDashboardResponse> BuildOtpDashboardAsync(string connStr)
    {
        var items  = new List<OtpItem>();
        long total = 0, totalUsers = 0;
        int last24 = 0;
        var dayAgo = DateTime.UtcNow.AddDays(-1);
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            var t1 = CountAsyncConn(conn, "SELECT COUNT(*) FROM otp");
            var t2 = CountAsyncConn(conn, "SELECT COUNT(*) FROM users");
            await Task.WhenAll(t1, t2);
            total = t1.Result; totalUsers = t2.Result;
            await using var cmd = new MySqlCommand("SELECT mobile,otp,updated_at FROM otp ORDER BY updated_at DESC LIMIT 100", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var upd = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2);
                if (upd >= dayAgo) last24++;
                items.Add(new OtpItem { UserMobile = Col(reader,0), Otp = Col(reader,1), UpdatedOn = DateCol(reader,2), Status = "RECORDED" });
            }
        }
        catch { }
        return new OtpDashboardResponse { TotalOtps = total, TotalUsers = totalUsers, Last24Hours = last24, Items = items };
    }

    public static async Task<ReportsDashboardResponse> BuildReportsDashboardAsync(string connStr)
    {
        var t1 = CountAsync(connStr, "SELECT COUNT(*) FROM records");
        var t2 = CountAsync(connStr, "SELECT COUNT(*) FROM file_info");
        var t3 = CountAsync(connStr, "SELECT COUNT(*) FROM branches");
        var t4 = CountAsync(connStr, "SELECT COUNT(*) FROM users");
        var t5 = BuildTableMetricsAsync(connStr);
        var t6 = GetTopBranchesAsync(connStr, 12);
        var t7 = GetFinancesAsync(connStr);
        await Task.WhenAll(t1, t2, t3, t4, t5, t6, t7);
        return new ReportsDashboardResponse
        {
            TotalVehicles = t1.Result, TotalUploads = t2.Result, TotalBranches = t3.Result, TotalUsers = t4.Result,
            Collections = t5.Result, TopBranches = t6.Result, TopBanks = t7.Result
        };
    }

    public static async Task<PaymentsDashboardResponse> BuildPaymentsDashboardAsync(string connStr)
    {
        var t1 = CountAsync(connStr, "SELECT COUNT(*) FROM finances");
        var t2 = CountAsync(connStr, "SELECT COUNT(*) FROM billings");
        var t3 = CountAsync(connStr, "SELECT COUNT(*) FROM file_info");
        var t4 = GetFinancesAsync(connStr);
        var t5 = GetRecentUploadsAsync(connStr, 8);
        await Task.WhenAll(t1, t2, t3, t4, t5);
        return new PaymentsDashboardResponse
        {
            TotalBanks = t1.Result, TotalBillings = t2.Result, WebhookUsers = 0, TotalUploads = t3.Result,
            StatusNote = "Billing and payment posting are being staged for the desktop flow.",
            PaymentMethods = new List<PaymentMethodItem>
            {
                new() { PaymentMethodId = 1, MethodName = "Cash Counter",   Details = "Manual counter collection", IsActive = true  },
                new() { PaymentMethodId = 2, MethodName = "Bank Transfer",  Details = "Direct bank transfer",      IsActive = true  },
                new() { PaymentMethodId = 3, MethodName = "UPI",            Details = "UPI payment",               IsActive = true  },
                new() { PaymentMethodId = 4, MethodName = "Billing Ledger", Details = "Reserved for future",       IsActive = false }
            },
            Banks = t4.Result, RecentUploads = t5.Result
        };
    }

    public static async Task<ModuleStatusResponse> BuildModuleStatusAsync(string connStr, string moduleKey)
    {
        var key = (moduleKey ?? "").Trim().ToLowerInvariant();
        var t1 = CountAsync(connStr, "SELECT COUNT(*) FROM records");
        var t2 = CountAsync(connStr, "SELECT COUNT(*) FROM branches");
        await Task.WhenAll(t1, t2);
        return new ModuleStatusResponse
        {
            ModuleKey  = key,
            Title      = ToTitleCase(key),
            Subtitle   = "Desktop showcase module",
            Banner     = "This module is ready in the desktop shell.",
            Primary    = new MetricCard { Label = "records",  Value = t1.Result.ToString("N0"), Description = "Total records"  },
            Secondary  = new MetricCard { Label = "branches", Value = t2.Result.ToString("N0"), Description = "Total branches" },
            Highlights = new List<NamedCountItem>
            {
                new() { Name = "MySQL connected",       Count = 1, Detail = "Reading from configured MySQL" },
                new() { Name = "Production switchable", Count = 1, Detail = "API base URL configurable from desktop settings" }
            },
            Collections = new List<CollectionMetric>(),
            RecentItems = new List<TimelineItem>()
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static async Task<OverviewCard> BuildOverviewAsync(string connStr)
    {
        var t1  = CountAsync(connStr, "SELECT COUNT(*) FROM records");
        var t2  = CountAsync(connStr, "SELECT COUNT(*) FROM branches WHERE is_active=1");
        var t3  = CountAsync(connStr, "SELECT COUNT(*) FROM finances WHERE is_active=1");
        var t4  = CountAsync(connStr, "SELECT COUNT(*) FROM users");
        var t5  = CountAsync(connStr, "SELECT COUNT(*) FROM users WHERE status='ACTIVE'");
        var t6  = CountAsync(connStr, "SELECT COUNT(*) FROM users WHERE role='ADMIN'");
        var t7  = CountAsync(connStr, "SELECT COUNT(*) FROM details");
        var t8  = CountAsync(connStr, "SELECT COUNT(*) FROM details WHERE vehicle_status='FOUND'");
        var t9  = CountAsync(connStr, "SELECT COUNT(*) FROM file_info");
        var t10 = CountAsync(connStr, "SELECT COUNT(*) FROM otp");
        var t11 = CountAsync(connStr, "SELECT COUNT(*) FROM billings");
        await Task.WhenAll(t1, t2, t3, t4, t5, t6, t7, t8, t9, t10, t11);
        return new OverviewCard
        {
            TotalRecords     = t1.Result,  TotalBranches    = t2.Result,
            TotalHeadOffices = t3.Result,  TotalUsers       = t4.Result,
            ActiveUsers      = t5.Result,  AdminUsers       = t6.Result,
            TotalDetailViews = t7.Result,  FoundDetails     = t8.Result,
            TotalUploads     = t9.Result,  TotalOtps        = t10.Result,
            TotalBillings    = t11.Result
        };
    }

    private static async Task<List<CollectionMetric>> BuildTableMetricsAsync(string connStr)
    {
        var tables = new[] { "records","branches","finances","users","file_info","details","otp","billings","repoconformations" };
        var tasks  = tables.Select(t => CountAsync(connStr, $"SELECT COUNT(*) FROM `{t}`")).ToArray();
        await Task.WhenAll(tasks);
        return tables.Zip(tasks, (name, task) => new CollectionMetric
        {
            Name    = name,
            Count   = task.Result,
            Summary = task.Result == 0 ? "Waiting for data" : "Live MySQL data"
        }).ToList();
    }

    private static async Task<List<BranchSummaryItem>> GetTopBranchesAsync(string connStr, int limit)
    {
        var branches = new List<BranchSummaryItem>();
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT b.id,b.name,b.total_records,b.contact1,b.contact2,b.updated_at,f.name
                FROM branches b LEFT JOIN finances f ON f.id=b.finance_id
                WHERE b.is_active=1 ORDER BY b.total_records DESC LIMIT @lim", conn);
            cmd.Parameters.AddWithValue("@lim", limit);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                branches.Add(new BranchSummaryItem
                {
                    BranchId       = Col(reader,0), BranchName     = Col(reader,1),
                    Records        = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    ContactPerson  = Col(reader,3), ContactMobile  = Col(reader,4),
                    UpdatedOn      = DateCol(reader,5), HeadOfficeName = Col(reader,6)
                });
        }
        catch { }
        return branches;
    }

    private static async Task<List<UploadFileItem>> GetRecentUploadsAsync(string connStr, int limit)
    {
        var uploads = new List<UploadFileItem>();
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT file_name,uploaded_by,upload_status,uploaded_at FROM file_info ORDER BY uploaded_at DESC LIMIT @lim", conn);
            cmd.Parameters.AddWithValue("@lim", limit);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                uploads.Add(new UploadFileItem { FileName = Col(reader,0), UploadedBy = Col(reader,1), VehicleType = Col(reader,2), CreatedOn = DateCol(reader,3) });
        }
        catch { }
        return uploads;
    }

    private static async Task<List<DetailViewItem>> GetRecentDetailsAsync(string connStr, int limit)
    {
        var items = new List<DetailViewItem>();
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT rc_no,chassis_no,engine_no,model,location,vehicle_status,created_at FROM details ORDER BY created_at DESC LIMIT @lim", conn);
            cmd.Parameters.AddWithValue("@lim", limit);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                items.Add(new DetailViewItem
                {
                    VehicleNo = Col(reader,0), ChassisNo = Col(reader,1), EngineNo = Col(reader,2),
                    Model = Col(reader,3), Location = Col(reader,4), VehicleStatus = Col(reader,5),
                    CreatedOn = DateCol(reader,6)
                });
        }
        catch { }
        return items;
    }

    private static async Task<List<NamedCountItem>> GetFinancesAsync(string connStr)
    {
        var list = new List<NamedCountItem>();
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("SELECT name FROM finances WHERE is_active=1 ORDER BY name", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(new NamedCountItem { Name = reader.IsDBNull(0) ? "" : reader.GetString(0), Count = 1, Detail = "Finance entity" });
        }
        catch { }
        return list;
    }

    private static VehicleSearchItem MapRecordRow(MySqlDataReader r)
    {
        var cols = new string[r.FieldCount];
        for (int i = 0; i < r.FieldCount; i++) cols[i] = r.GetName(i);
        string Get(string name) { var idx = Array.IndexOf(cols, name); return idx >= 0 && !r.IsDBNull(idx) ? r.GetString(idx) : ""; }
        return new VehicleSearchItem
        {
            Id = Get("id"), ReleaseStatus = Get("is_released"), VehicleNo = Get("rc_no"),
            ChassisNo = Get("chassis_no"), Model = Get("model"), EngineNo = Get("engine_no"),
            CustomerName = Get("customer_name"), CustomerContactNos = Get("customer_contact_nos"),
            CustomerAddress = Get("customer_address"), BranchName = Get("branch_name"),
            ExecutiveName = Get("executive_name"), AgreementNo = Get("agreement_no"),
            Region = Get("region"), Area = Get("area"), Bucket = Get("bucket"),
            GV = Get("gv"), OD = Get("od"), Sec9Available = Get("sec9_available"),
            Sec17Available = Get("sec17_available"), TBRFlag = Get("tbr_flag"),
            Seasoning = Get("seasoning"), SenderMailId1 = Get("sender_mail_id1"),
            SenderMailId2 = Get("sender_mail_id2"), POS = Get("pos"), TOSS = Get("toss"),
            Remark = Get("remark"), Level1 = Get("level1"), Level1ContactNos = Get("level1_contact"),
            Level2 = Get("level2"), Level2ContactNos = Get("level2_contact"),
            Level3 = Get("level3"), Level3ContactNos = Get("level3_contact"),
            Level4 = Get("level4"), Level4ContactNos = Get("level4_contact")
        };
    }

    private static async Task<long> CountAsync(string connStr, string sql)
    {
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(sql, conn);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
        }
        catch { return 0; }
    }

    private static async Task<long> CountAsyncConn(MySqlConnection conn, string sql)
    {
        try { await using var cmd = new MySqlCommand(sql, conn); return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L); }
        catch { return 0; }
    }

    private static string Col(MySqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";

    private static string DateCol(MySqlDataReader r, int i)
    {
        if (r.IsDBNull(i)) return "";
        try { return r.GetDateTime(i).ToLocalTime().ToString("dd MMM yyyy hh:mm tt"); }
        catch { return r.GetValue(i).ToString() ?? ""; }
    }

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Module";
        return string.Join(' ', value.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
