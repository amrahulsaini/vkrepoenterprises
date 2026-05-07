using MySqlConnector;
using VKApiServer.Models;

namespace VKApiServer;

internal static class DashboardRepository
{
    public static async Task<HomeDashboardResponse> BuildHomeDashboardAsync(string connStr)
    {
        return new HomeDashboardResponse
        {
            Overview      = await BuildOverviewAsync(connStr),
            Collections   = await BuildTableMetricsAsync(connStr),
            RecentUploads = await GetRecentUploadsAsync(connStr, 6),
            RecentDetails = await GetRecentDetailsAsync(connStr, 8),
            TopBranches   = await GetTopBranchesAsync(connStr, 8)
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
            Query           = searchTerm,
            Mode            = vehicleMode,
            ResultCount     = results.Count,
            UniqueBranches  = results.Select(r => r.BranchName).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Results         = results
        };
    }

    public static async Task<FinanceDashboardResponse> BuildFinanceDashboardAsync(string connStr)
    {
        return new FinanceDashboardResponse
        {
            TotalHeadOffices = 0,
            TotalBranches    = await CountAsync(connStr, "SELECT COUNT(*) FROM branches"),
            TotalRecords     = await CountAsync(connStr, "SELECT COUNT(*) FROM records"),
            TotalUploads     = await CountAsync(connStr, "SELECT COUNT(*) FROM file_info"),
            TopBranches      = await GetTopBranchesAsync(connStr, 18),
            RecentUploads    = await GetRecentUploadsAsync(connStr, 10),
            Banks            = await GetFinancesAsync(connStr)
        };
    }

    public static async Task<UsersDashboardResponse> BuildUsersDashboardAsync(string connStr)
    {
        var users = new List<UserSummaryItem>();
        long total = 0, active = 0, admin = 0;

        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();

            total  = await CountAsyncConn(conn, "SELECT COUNT(*) FROM users");
            active = await CountAsyncConn(conn, "SELECT COUNT(*) FROM users WHERE status = 'ACTIVE'");
            admin  = await CountAsyncConn(conn, "SELECT COUNT(*) FROM users WHERE role = 'ADMIN'");

            await using var cmd = new MySqlCommand(
                "SELECT id, name, mobile, address, role, status, created_at FROM users ORDER BY created_at DESC LIMIT 100",
                conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
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
        }
        catch { }

        return new UsersDashboardResponse
        {
            TotalUsers  = total,
            ActiveUsers = active,
            AdminUsers  = admin,
            Users       = users
        };
    }

    public static async Task<UploadsDashboardResponse> BuildUploadsDashboardAsync(string connStr)
    {
        return new UploadsDashboardResponse
        {
            TotalFiles   = await CountAsync(connStr, "SELECT COUNT(*) FROM file_info"),
            TotalBanks   = await CountAsync(connStr, "SELECT COUNT(*) FROM finances"),
            TotalHeaders = 0,
            Files        = await GetRecentUploadsAsync(connStr, 18),
            Banks        = await GetFinancesAsync(connStr)
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
            total    = await CountAsyncConn(conn, "SELECT COUNT(*) FROM details");
            found    = await CountAsyncConn(conn, "SELECT COUNT(*) FROM details WHERE vehicle_status = 'FOUND'");
            notFound = await CountAsyncConn(conn, "SELECT COUNT(*) FROM details WHERE vehicle_status = 'NOT FOUND'");

            await using var cmd = new MySqlCommand(
                "SELECT rc_no,chassis_no,engine_no,model,location,vehicle_status,created_at FROM details ORDER BY created_at DESC LIMIT 60",
                conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new DetailViewItem
                {
                    VehicleNo     = Col(reader, 0),
                    ChassisNo     = Col(reader, 1),
                    EngineNo      = Col(reader, 2),
                    Model         = Col(reader, 3),
                    Location      = Col(reader, 4),
                    VehicleStatus = Col(reader, 5),
                    CreatedOn     = DateCol(reader, 6)
                });
            }
        }
        catch { }

        return new DetailsDashboardResponse
        {
            TotalViews   = total,
            FoundCount   = found,
            NotFoundCount= notFound,
            UniqueUsers  = 0,
            Items        = items,
            Locations    = new List<NamedCountItem>()
        };
    }

    public static async Task<OtpDashboardResponse> BuildOtpDashboardAsync(string connStr)
    {
        var items   = new List<OtpItem>();
        long total  = 0;
        var dayAgo  = DateTime.UtcNow.AddDays(-1);
        int last24  = 0;

        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            total = await CountAsyncConn(conn, "SELECT COUNT(*) FROM otp");

            await using var cmd = new MySqlCommand(
                "SELECT mobile, otp, updated_at FROM otp ORDER BY updated_at DESC LIMIT 100", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var updatedAt = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2);
                if (updatedAt >= dayAgo) last24++;
                items.Add(new OtpItem
                {
                    UserMobile = Col(reader, 0),
                    Otp        = Col(reader, 1),
                    UpdatedOn  = DateCol(reader, 2),
                    Status     = "RECORDED"
                });
            }
        }
        catch { }

        return new OtpDashboardResponse
        {
            TotalOtps   = total,
            TotalUsers  = await CountAsync(connStr, "SELECT COUNT(*) FROM users"),
            Last24Hours = last24,
            Items       = items
        };
    }

    public static async Task<ReportsDashboardResponse> BuildReportsDashboardAsync(string connStr)
    {
        return new ReportsDashboardResponse
        {
            TotalVehicles = await CountAsync(connStr, "SELECT COUNT(*) FROM records"),
            TotalUploads  = await CountAsync(connStr, "SELECT COUNT(*) FROM file_info"),
            TotalBranches = await CountAsync(connStr, "SELECT COUNT(*) FROM branches"),
            TotalUsers    = await CountAsync(connStr, "SELECT COUNT(*) FROM users"),
            Collections   = await BuildTableMetricsAsync(connStr),
            TopBranches   = await GetTopBranchesAsync(connStr, 12),
            TopBanks      = await GetFinancesAsync(connStr)
        };
    }

    public static async Task<PaymentsDashboardResponse> BuildPaymentsDashboardAsync(string connStr)
    {
        return new PaymentsDashboardResponse
        {
            TotalBanks    = await CountAsync(connStr, "SELECT COUNT(*) FROM finances"),
            TotalBillings = await CountAsync(connStr, "SELECT COUNT(*) FROM billings"),
            WebhookUsers  = 0,
            TotalUploads  = await CountAsync(connStr, "SELECT COUNT(*) FROM file_info"),
            StatusNote    = "Billing and payment posting are being staged for the desktop flow.",
            PaymentMethods = new List<PaymentMethodItem>
            {
                new() { PaymentMethodId = 1, MethodName = "Cash Counter",    Details = "Manual counter collection",    IsActive = true  },
                new() { PaymentMethodId = 2, MethodName = "Bank Transfer",   Details = "Direct bank transfer",         IsActive = true  },
                new() { PaymentMethodId = 3, MethodName = "UPI",             Details = "UPI payment",                  IsActive = true  },
                new() { PaymentMethodId = 4, MethodName = "Billing Ledger",  Details = "Reserved for future billing",  IsActive = false }
            },
            Banks         = await GetFinancesAsync(connStr),
            RecentUploads = await GetRecentUploadsAsync(connStr, 8)
        };
    }

    public static async Task<ModuleStatusResponse> BuildModuleStatusAsync(string connStr, string moduleKey)
    {
        var key = (moduleKey ?? "").Trim().ToLowerInvariant();
        return new ModuleStatusResponse
        {
            ModuleKey  = key,
            Title      = ToTitleCase(key),
            Subtitle   = "Desktop showcase module",
            Banner     = "This module is ready in the desktop shell.",
            Primary    = new MetricCard { Label = "records",  Value = (await CountAsync(connStr, "SELECT COUNT(*) FROM records")).ToString("N0"),   Description = "Total records" },
            Secondary  = new MetricCard { Label = "branches", Value = (await CountAsync(connStr, "SELECT COUNT(*) FROM branches")).ToString("N0"), Description = "Total branches" },
            Highlights = new List<NamedCountItem>
            {
                new() { Name = "MySQL connected",        Count = 1, Detail = "Reading from configured MySQL" },
                new() { Name = "Production switchable",  Count = 1, Detail = "API base URL configurable from desktop settings" }
            },
            Collections = new List<CollectionMetric>(),
            RecentItems = new List<TimelineItem>()
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static async Task<OverviewCard> BuildOverviewAsync(string connStr)
    {
        return new OverviewCard
        {
            TotalRecords     = await CountAsync(connStr, "SELECT COUNT(*) FROM records"),
            TotalBranches    = await CountAsync(connStr, "SELECT COUNT(*) FROM branches"),
            TotalHeadOffices = await CountAsync(connStr, "SELECT COUNT(*) FROM finances"),
            TotalUsers       = await CountAsync(connStr, "SELECT COUNT(*) FROM users"),
            ActiveUsers      = await CountAsync(connStr, "SELECT COUNT(*) FROM users WHERE status = 'ACTIVE'"),
            AdminUsers       = await CountAsync(connStr, "SELECT COUNT(*) FROM users WHERE role = 'ADMIN'"),
            TotalDetailViews = await CountAsync(connStr, "SELECT COUNT(*) FROM details"),
            FoundDetails     = await CountAsync(connStr, "SELECT COUNT(*) FROM details WHERE vehicle_status = 'FOUND'"),
            TotalUploads     = await CountAsync(connStr, "SELECT COUNT(*) FROM file_info"),
            TotalOtps        = await CountAsync(connStr, "SELECT COUNT(*) FROM otp"),
            TotalBillings    = await CountAsync(connStr, "SELECT COUNT(*) FROM billings")
        };
    }

    private static async Task<List<CollectionMetric>> BuildTableMetricsAsync(string connStr)
    {
        var tables = new[] { "records", "branches", "finances", "users", "file_info", "details", "otp", "billings", "repoconformations" };
        var result = new List<CollectionMetric>();
        foreach (var t in tables)
        {
            var count = await CountAsync(connStr, $"SELECT COUNT(*) FROM `{t}`");
            result.Add(new CollectionMetric
            {
                Name    = t,
                Count   = count,
                Summary = count == 0 ? "Waiting for data" : "Live MySQL data"
            });
        }
        return result;
    }

    private static async Task<List<BranchSummaryItem>> GetTopBranchesAsync(string connStr, int limit)
    {
        var branches = new List<BranchSummaryItem>();
        try
        {
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(@"
                SELECT b.id, b.name, b.total_records, b.contact1, b.contact2, b.updated_at, f.name
                FROM branches b
                LEFT JOIN finances f ON f.id = b.finance_id
                WHERE b.is_active = 1
                ORDER BY b.total_records DESC
                LIMIT @lim", conn);
            cmd.Parameters.AddWithValue("@lim", limit);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                branches.Add(new BranchSummaryItem
                {
                    BranchId       = Col(reader, 0),
                    BranchName     = Col(reader, 1),
                    Records        = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    ContactPerson  = Col(reader, 3),
                    ContactMobile  = Col(reader, 4),
                    UpdatedOn      = DateCol(reader, 5),
                    HeadOfficeName = Col(reader, 6)
                });
            }
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
                "SELECT file_name, uploaded_by, upload_status, uploaded_at FROM file_info ORDER BY uploaded_at DESC LIMIT @lim",
                conn);
            cmd.Parameters.AddWithValue("@lim", limit);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                uploads.Add(new UploadFileItem
                {
                    FileName   = Col(reader, 0),
                    UploadedBy = Col(reader, 1),
                    VehicleType= Col(reader, 2),
                    CreatedOn  = DateCol(reader, 3)
                });
            }
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
                "SELECT rc_no, chassis_no, engine_no, model, location, vehicle_status, created_at FROM details ORDER BY created_at DESC LIMIT @lim",
                conn);
            cmd.Parameters.AddWithValue("@lim", limit);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new DetailViewItem
                {
                    VehicleNo     = Col(reader, 0),
                    ChassisNo     = Col(reader, 1),
                    EngineNo      = Col(reader, 2),
                    Model         = Col(reader, 3),
                    Location      = Col(reader, 4),
                    VehicleStatus = Col(reader, 5),
                    CreatedOn     = DateCol(reader, 6)
                });
            }
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
            await using var cmd = new MySqlCommand(
                "SELECT name FROM finances WHERE is_active = 1 ORDER BY name", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new NamedCountItem
                {
                    Name   = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Count  = 1,
                    Detail = "Finance entity"
                });
            }
        }
        catch { }
        return list;
    }

    private static VehicleSearchItem MapRecordRow(MySqlDataReader r)
    {
        var cols = new string[r.FieldCount];
        for (int i = 0; i < r.FieldCount; i++) cols[i] = r.GetName(i);
        string Get(string name)
        {
            var idx = Array.IndexOf(cols, name);
            return idx >= 0 && !r.IsDBNull(idx) ? r.GetString(idx) : "";
        }
        return new VehicleSearchItem
        {
            Id                 = Get("id"),
            ReleaseStatus      = Get("is_released"),
            VehicleNo          = Get("rc_no"),
            ChassisNo          = Get("chassis_no"),
            Model              = Get("model"),
            EngineNo           = Get("engine_no"),
            CustomerName       = Get("customer_name"),
            CustomerContactNos = Get("customer_contact_nos"),
            CustomerAddress    = Get("customer_address"),
            BranchName         = Get("branch_name"),
            ExecutiveName      = Get("executive_name"),
            AgreementNo        = Get("agreement_no"),
            Region             = Get("region"),
            Area               = Get("area"),
            Bucket             = Get("bucket"),
            GV                 = Get("gv"),
            OD                 = Get("od"),
            Sec9Available      = Get("sec9_available"),
            Sec17Available     = Get("sec17_available"),
            TBRFlag            = Get("tbr_flag"),
            Seasoning          = Get("seasoning"),
            SenderMailId1      = Get("sender_mail_id1"),
            SenderMailId2      = Get("sender_mail_id2"),
            POS                = Get("pos"),
            TOSS               = Get("toss"),
            Remark             = Get("remark"),
            Level1             = Get("level1"),
            Level1ContactNos   = Get("level1_contact"),
            Level2             = Get("level2"),
            Level2ContactNos   = Get("level2_contact"),
            Level3             = Get("level3"),
            Level3ContactNos   = Get("level3_contact"),
            Level4             = Get("level4"),
            Level4ContactNos   = Get("level4_contact")
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
        try
        {
            await using var cmd = new MySqlCommand(sql, conn);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
        }
        catch { return 0; }
    }

    private static string Col(MySqlDataReader r, int i) =>
        r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";

    private static string DateCol(MySqlDataReader r, int i)
    {
        if (r.IsDBNull(i)) return "";
        try { return r.GetDateTime(i).ToLocalTime().ToString("dd MMM yyyy hh:mm tt"); }
        catch { return r.GetValue(i).ToString() ?? ""; }
    }

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Module";
        var words = value.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(' ', words);
    }
}
