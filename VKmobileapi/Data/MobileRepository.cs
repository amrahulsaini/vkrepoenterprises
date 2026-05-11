using System.Collections.Concurrent;
using MySqlConnector;
using VKmobileapi.Models;

namespace VKmobileapi.Data;

public class MobileRepository
{
    // ── In-memory search cache ─────────────────────────────────────────────
    // Key: "rc:XXXX" or "ch:XXXXX" — value: cached result list with timestamp
    private static readonly ConcurrentDictionary<string, (List<SearchResult> Results, DateTime At)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);

    // ── Subscription status cache — avoids DB hit on every request ─────────
    // Key: userId — value: (hasActiveSub, cachedAt). TTL 5 min.
    private static readonly ConcurrentDictionary<long, (bool Active, DateTime At)> _subCache = new();
    private static readonly TimeSpan SubCacheTtl = TimeSpan.FromMinutes(5);

    public static void InvalidateSearchCache()
    {
        _cache.Clear();
    }

    public static void InvalidateSubCache(long userId)
    {
        _subCache.TryRemove(userId, out _);
    }


    // ── Register ──────────────────────────────────────────────────────────
    public async Task<(bool Success, string Reason, long UserId)> RegisterAsync(
        string mobile, string name, string? address, string? pincode,
        string? pfpBase64, string deviceId,
        string? aadhaarFront, string? aadhaarBack, string? panFront,
        string? accountNumber, string? ifscCode)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();

        await using (var chk = new MySqlCommand(
            "SELECT COUNT(*) FROM app_users WHERE mobile = @m", conn))
        {
            chk.Parameters.AddWithValue("@m", mobile);
            var cnt = Convert.ToInt64(await chk.ExecuteScalarAsync());
            if (cnt > 0) return (false, "mobile_exists", 0);
        }

        const string sql = @"
            INSERT INTO app_users (mobile, name, address, pincode, pfp, device_id,
                                   account_number, ifsc_code, is_active, is_admin)
            VALUES (@mobile, @name, @addr, @pin, @pfp, @did,
                    @acct, @ifsc, 0, 0);
            SELECT LAST_INSERT_ID();";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@mobile", mobile);
        cmd.Parameters.AddWithValue("@name",   name);
        cmd.Parameters.AddWithValue("@addr",   (object?)address       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pin",    (object?)pincode       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pfp",    (object?)pfpBase64     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@did",    deviceId);
        cmd.Parameters.AddWithValue("@acct",   (object?)accountNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ifsc",   (object?)ifscCode      ?? DBNull.Value);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());

        // Save KYC documents if provided
        bool hasKyc = aadhaarFront != null || aadhaarBack != null || panFront != null;
        if (hasKyc)
        {
            const string kycSql = @"
                INSERT INTO user_kyc (user_id, aadhaar_front, aadhaar_back, pan_front)
                VALUES (@uid, @af, @ab, @pf)";
            await using var kycCmd = new MySqlCommand(kycSql, conn);
            kycCmd.Parameters.AddWithValue("@uid", id);
            kycCmd.Parameters.AddWithValue("@af",  (object?)aadhaarFront ?? DBNull.Value);
            kycCmd.Parameters.AddWithValue("@ab",  (object?)aadhaarBack  ?? DBNull.Value);
            kycCmd.Parameters.AddWithValue("@pf",  (object?)panFront     ?? DBNull.Value);
            await kycCmd.ExecuteNonQueryAsync();
        }

        return (true, "registered", id);
    }

    // ── Login ─────────────────────────────────────────────────────────────
    public async Task<AuthResponse> LoginAsync(string mobile, string deviceId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();

        const string sql = @"
            SELECT u.id, u.name, u.mobile, u.device_id, u.is_active, u.is_admin, u.pfp,
                   COALESCE(
                       (SELECT DATE_FORMAT(s.end_date,'%Y-%m-%d')
                        FROM subscriptions s
                        WHERE s.user_id = u.id AND s.end_date >= CURDATE()
                        ORDER BY s.end_date DESC LIMIT 1),
                       '') AS sub_end
            FROM app_users u
            WHERE u.mobile = @m
            LIMIT 1";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@m", mobile);
        await using var rdr = await cmd.ExecuteReaderAsync();

        if (!await rdr.ReadAsync())
            return new AuthResponse(false, "Not registered.", "not_found",
                null, null, null, false, null, null);

        var id       = rdr.GetInt64(0);
        var name     = rdr.GetString(1);
        var dbMobile = rdr.GetString(2);
        var dbDevice = rdr.IsDBNull(3) ? null : rdr.GetString(3);
        var isActive = rdr.GetInt32(4) == 1;
        var isAdmin  = rdr.GetInt32(5) == 1;
        var pfp      = rdr.IsDBNull(6) ? null : rdr.GetString(6);
        var subEnd   = rdr.GetString(7);

        if (!string.IsNullOrEmpty(dbDevice) &&
            !string.Equals(dbDevice, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            // Store a device-change request so the admin can approve on the desktop
            try
            {
                await rdr.CloseAsync();
                await using var upsert = new MySqlCommand(@"
                    INSERT INTO device_change_requests (user_id,user_name,user_mobile,new_device_id,requested_at)
                    VALUES (@uid,@name,@mob,@dev,NOW())
                    ON DUPLICATE KEY UPDATE new_device_id=@dev, requested_at=NOW()",
                    conn);
                upsert.Parameters.AddWithValue("@uid",  id);
                upsert.Parameters.AddWithValue("@name", name);
                upsert.Parameters.AddWithValue("@mob",  dbMobile);
                upsert.Parameters.AddWithValue("@dev",  deviceId);
                await upsert.ExecuteNonQueryAsync();
            }
            catch { /* don't block login response on logging failure */ }
            return new AuthResponse(false,
                "This mobile number is registered on another device. Ask admin to reset your device.",
                "device_mismatch", null, null, null, false, null, null);
        }

        if (!isActive)
            return new AuthResponse(false,
                "Your account is pending admin approval. Please wait.",
                "pending_approval", null, null, null, false, null, null);

        return new AuthResponse(true, "Login successful.", "ok",
            id, name, dbMobile, isAdmin, pfp, subEnd == "" ? null : subEnd);
    }

    // ── Heartbeat / live-user location ────────────────────────────────────
    public async Task HeartbeatAsync(long userId, double? lat, double? lng)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_users SET last_seen=NOW(), last_lat=@lat, last_lng=@lng WHERE id=@uid",
            conn) { CommandTimeout = 5 };
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@lat", lat.HasValue ? (object)lat.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@lng", lng.HasValue ? (object)lng.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<LiveUserItem>> GetLiveUsersAsync()
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, name, mobile, last_seen, last_lat, last_lng
            FROM app_users
            WHERE last_seen >= NOW() - INTERVAL 15 MINUTE
            ORDER BY last_seen DESC LIMIT 100";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 5 };
        var list = new List<LiveUserItem>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new LiveUserItem(
                rdr.GetInt64(0), rdr.GetString(1), rdr.GetString(2),
                rdr.GetDateTime(3).ToString("yyyy-MM-dd HH:mm:ss"),
                rdr.IsDBNull(4) ? null : (double?)rdr.GetDouble(4),
                rdr.IsDBNull(5) ? null : (double?)rdr.GetDouble(5)));
        return list;
    }

    // ── Admin check ───────────────────────────────────────────────────────
    public async Task<bool> IsAdminAsync(long userId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT is_admin FROM app_users WHERE id=@id LIMIT 1", conn) { CommandTimeout = 5 };
        cmd.Parameters.AddWithValue("@id", userId);
        var v = await cmd.ExecuteScalarAsync();
        return v is not (null or DBNull) && Convert.ToInt32(v) == 1;
    }

    // ── Subscription check (cached 5 min) ─────────────────────────────────
    public async Task<bool> HasActiveSubscriptionAsync(long userId)
    {
        if (_subCache.TryGetValue(userId, out var sc) && DateTime.UtcNow - sc.At < SubCacheTtl)
            return sc.Active;

        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = "SELECT COUNT(*) FROM subscriptions WHERE user_id=@uid AND end_date >= CURDATE()";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        var active = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;

        _subCache[userId] = (active, DateTime.UtcNow);
        return active;
    }

    // ── Profile ───────────────────────────────────────────────────────────
    public async Task<ProfileResponse?> GetProfileAsync(long userId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();

        const string sql = @"
            SELECT u.id, u.name, u.mobile, u.address, u.pincode, u.pfp,
                   u.is_active, u.is_admin, u.balance,
                   DATE_FORMAT(u.created_at,'%d %b %Y') AS created_at,
                   u.account_number, u.ifsc_code,
                   k.aadhaar_front, k.aadhaar_back, k.pan_front
            FROM app_users u
            LEFT JOIN user_kyc k ON k.user_id = u.id
            WHERE u.id = @id LIMIT 1";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", userId);
        await using var rdr = await cmd.ExecuteReaderAsync();

        if (!await rdr.ReadAsync()) return null;

        string? S(int i) => rdr.IsDBNull(i) ? null : rdr.GetString(i);

        var aadhaarFront = S(12);
        var aadhaarBack  = S(13);
        var panFront     = S(14);
        var kycSubmitted = aadhaarFront != null || aadhaarBack != null || panFront != null;

        var profile = new ProfileResponse(
            UserId:        rdr.GetInt64(0),
            Name:          rdr.GetString(1),
            Mobile:        rdr.GetString(2),
            Address:       S(3),
            Pincode:       S(4),
            PfpBase64:     S(5),
            IsActive:      rdr.GetInt32(6) == 1,
            IsAdmin:       rdr.GetInt32(7) == 1,
            Balance:       rdr.GetDecimal(8),
            CreatedAt:     rdr.GetString(9),
            AccountNumber: S(10),
            IfscCode:      S(11),
            Kyc: new KycInfo(kycSubmitted, aadhaarFront, aadhaarBack, panFront),
            Subscriptions: new List<SubscriptionRecord>());

        await rdr.CloseAsync();

        // Load subscriptions
        const string subSql = @"
            SELECT id, DATE_FORMAT(start_date,'%Y-%m-%d'), DATE_FORMAT(end_date,'%Y-%m-%d'),
                   COALESCE(amount,0), notes,
                   (end_date >= CURDATE()) AS is_active
            FROM subscriptions WHERE user_id=@id ORDER BY end_date DESC";
        await using var subCmd = new MySqlCommand(subSql, conn);
        subCmd.Parameters.AddWithValue("@id", userId);
        await using var sr = await subCmd.ExecuteReaderAsync();
        while (await sr.ReadAsync())
        {
            profile.Subscriptions.Add(new SubscriptionRecord(
                Id:        sr.GetInt64(0),
                StartDate: sr.GetString(1),
                EndDate:   sr.GetString(2),
                Amount:    sr.GetDecimal(3),
                Notes:     sr.IsDBNull(4) ? null : sr.GetString(4),
                IsActive:  sr.GetInt32(5) == 1));
        }

        return profile;
    }

    // ── Update PFP ────────────────────────────────────────────────────────
    public async Task UpdatePfpAsync(long userId, string? pfpBase64)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_users SET pfp = @p WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@p",  (object?)pfpBase64 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── RC search (instant — indexed last4) ───────────────────────────────
    public async Task<List<SearchResult>> SearchByRcAsync(string last4)
    {
        var key = $"rc:{last4.ToUpper()}";
        if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < CacheTtl)
            return cached.Results;

        var results = await SearchAsync($@"
            SELECT {SelectFields}
            FROM rc_info ri
            INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE ri.last4 = @q
            ORDER BY b.name, vr.vehicle_no LIMIT 500",
            last4.ToUpper());

        _cache[key] = (results, DateTime.UtcNow);
        return results;
    }

    // ── Chassis search (instant — indexed last5) ──────────────────────────
    public async Task<List<SearchResult>> SearchByChassisAsync(string last5)
    {
        var key = $"ch:{last5.ToUpper()}";
        if (_cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < CacheTtl)
            return cached.Results;

        var results = await SearchAsync($@"
            SELECT {SelectFields}
            FROM chassis_info ci
            INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE ci.last5 = @q
            ORDER BY b.name, vr.chassis_no LIMIT 500",
            last5.ToUpper());

        _cache[key] = (results, DateTime.UtcNow);
        return results;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private const string SelectFields = @"
        vr.id, vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model,
        vr.agreement_no, vr.customer_name, vr.customer_contact, vr.customer_address,
        COALESCE(f.name,'')  AS financer,
        b.name               AS branch_name,
        COALESCE(b.contact1,'') AS c1, COALESCE(b.contact2,'') AS c2,
        COALESCE(b.contact3,'') AS c3, COALESCE(b.address,'')  AS b_addr,
        vr.region, vr.area, vr.bucket, vr.gv, vr.od, vr.seasoning,
        vr.tbr_flag, vr.sec9_available, vr.sec17_available,
        vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
        vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
        vr.sender_mail1, vr.sender_mail2, vr.executive_name, vr.pos, vr.toss, vr.remark,
        COALESCE(vr.branch_name_raw,'') AS branch_name_raw,
        COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on";

    private static async Task<List<SearchResult>> SearchAsync(string sql, string query)
    {
        var list = new List<SearchResult>();
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@q", query);
        await using var r = await cmd.ExecuteReaderAsync();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        while (await r.ReadAsync())
            list.Add(new SearchResult(
                r.GetInt64(0), S(1), S(2), S(3), S(4), S(5),
                S(6), S(7), S(8), S(9), S(10), S(11), S(12), S(13),
                S(14), S(15), S(16), S(17), S(18), S(19), S(20), S(21),
                S(22), S(23), S(24), S(25), S(26), S(27), S(28), S(29),
                S(30), S(31), S(32), S(33), S(34), S(35), S(36), S(37),
                S(38), S(39)));
        return list;
    }

    // ── Profile picture ───────────────────────────────────────────────────
    public async Task<string?> GetPfpAsync(long userId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = "SELECT pfp FROM app_users WHERE id=@id LIMIT 1";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : result.ToString();
    }

    // ── Sync: branch list ──────────────────────────────────────────────────
    public async Task<List<SyncBranch>> GetSyncBranchesAsync()
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = @"
            SELECT b.id, b.name, COALESCE(f.name,'') AS financer,
                   COALESCE(b.total_records,0),
                   DATE_FORMAT(b.uploaded_at,'%Y-%m-%dT%H:%i:%s')
            FROM branches b
            LEFT JOIN finances f ON f.id = b.finance_id
            WHERE b.is_active = 1 AND b.uploaded_at IS NOT NULL
            ORDER BY b.id";
        var list = new List<SyncBranch>();
        await using var cmd = new MySqlCommand(sql, conn);
        await using var r   = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new SyncBranch(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.GetInt64(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    // ── Sync: compact records for one branch (paginated) ──────────────────
    public async Task<List<SyncRecord>> GetSyncRecordsAsync(int branchId, int page, int size)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = @"
            SELECT vr.id,
                   COALESCE(vr.vehicle_no,'')         AS vehicle_no,
                   COALESCE(vr.chassis_no,'')         AS chassis_no,
                   COALESCE(vr.engine_no,'')          AS engine_no,
                   COALESCE(vr.model,'')              AS model,
                   COALESCE(vr.customer_name,'')      AS customer_name,
                   COALESCE(RIGHT(vr.vehicle_no,4),'') AS last4,
                   COALESCE(RIGHT(vr.chassis_no,5),'') AS last5
            FROM vehicle_records vr
            WHERE vr.branch_id = @bid
            ORDER BY vr.id
            LIMIT @size OFFSET @off";
        var list = new List<SyncRecord>();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@bid",  branchId);
        cmd.Parameters.AddWithValue("@size", size);
        cmd.Parameters.AddWithValue("@off",  page * size);
        await using var r = await cmd.ExecuteReaderAsync();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        while (await r.ReadAsync())
            list.Add(new SyncRecord(r.GetInt64(0), S(1), S(2), S(3), S(4), S(5), S(6), S(7)));
        return list;
    }

    // ── Stats ──────────────────────────────────────────────────────────────
    public async Task<(long vehicleRecords, long rcRecords, long chassisRecords)> GetStatsAsync()
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        // Read pre-computed total_records from branches (stored on every upload) —
        // avoids full InnoDB COUNT(*) scan which takes seconds on large tables.
        const string sql = @"
            SELECT
                COALESCE(SUM(total_records), 0),
                COALESCE(SUM(total_records), 0),
                COALESCE(SUM(total_records), 0)
            FROM branches WHERE is_active = 1";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var r   = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2));
    }
}
