using System.Collections.Concurrent;
using MySqlConnector;
using VKmobileapi;
using VKmobileapi.Models;

namespace VKmobileapi.Data;

public class MobileRepository
{
    public static string UploadsPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

    private static async Task<string?> SaveBase64ImageAsync(string? base64, string subFolder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            var dir = Path.Combine(UploadsPath, subFolder);
            Directory.CreateDirectory(dir);
            var bytes = Convert.FromBase64String(base64);
            var path  = Path.Combine(dir, fileName);
            await File.WriteAllBytesAsync(path, bytes);
            return $"{subFolder}/{fileName}";
        }
        catch { return null; }
    }

    private static readonly ConcurrentDictionary<string, (List<SearchResult> Results, DateTime At)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);

    private static readonly ConcurrentDictionary<string, (bool Active, DateTime At)> _subCache = new();
    private static readonly TimeSpan SubCacheTtl = TimeSpan.FromMinutes(5);

    public static void InvalidateSearchCache()
    {
        _cache.Clear();
    }

    public static void InvalidateSubCache(long userId)
    {
        _subCache.Clear();
    }

    public async Task<List<AgencyListItem>> GetApprovedAgenciesAsync()
    {
        await using var conn = DbFactory.CreateMaster();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, name, slug, COALESCE(logo_path,'') FROM agencies WHERE status='approved' ORDER BY name ASC", conn);
        var list = new List<AgencyListItem>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new AgencyListItem(rdr.GetInt64(0), rdr.GetString(1), rdr.GetString(2), rdr.GetString(3)));
        return list;
    }

    public async Task<(bool Found, string Name, string Mobile1, string Status)> GetAgencyBySlugAsync(string slug)
    {
        await using var conn = DbFactory.CreateMaster();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT name, mobile1, status FROM agencies WHERE slug=@s LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@s", slug);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return (false, "", "", "");
        return (true,
                rdr.GetString(0),
                rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                rdr.GetString(2));
    }

    public async Task<AgencyInfo?> GetAgencyInfoAsync(string slug)
    {
        await using var conn = DbFactory.CreateMaster();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            SELECT name, COALESCE(address,''), mobile1,
                   COALESCE(mobile2,''), COALESCE(mobiles_extra,''),
                   COALESCE(logo_path,'')
              FROM agencies WHERE slug=@s LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@s", slug);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;

        var mobiles = new List<string>();
        void Add(string s) {
            s = s.Trim();
            if (!string.IsNullOrWhiteSpace(s) && !mobiles.Contains(s))
                mobiles.Add(s);
        }
        Add(rdr.GetString(2));
        Add(rdr.GetString(3));
        foreach (var line in rdr.GetString(4).Split(new[] { '\n','\r' }, StringSplitOptions.RemoveEmptyEntries))
            Add(line);
        return new AgencyInfo(
            Name:     rdr.GetString(0),
            Address:  rdr.GetString(1),
            Mobiles:  mobiles,
            LogoPath: rdr.GetString(5));
    }


    public async Task<string?> FindExistingAgencyForMobileOrDevice(string mobile, string deviceId, string currentSlug)
    {
        await using var conn = DbFactory.CreateMaster();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            SELECT agency_slug FROM app_user_registry
             WHERE (mobile = @m OR device_id = @d) AND agency_slug != @s
             LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@m", mobile);
        cmd.Parameters.AddWithValue("@d", deviceId);
        cmd.Parameters.AddWithValue("@s", currentSlug);
        var r = await cmd.ExecuteScalarAsync();
        return r as string;
    }

    public async Task RegisterInMasterAsync(string mobile, string deviceId, string slug)
    {
        await using var conn = DbFactory.CreateMaster();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO app_user_registry (mobile, device_id, agency_slug) VALUES (@m, @d, @s)", conn);
        cmd.Parameters.AddWithValue("@m", mobile);
        cmd.Parameters.AddWithValue("@d", deviceId);
        cmd.Parameters.AddWithValue("@s", slug);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> IsMobileOrDeviceLiveInAgencyAsync(string mobile, string deviceId, string slug)
    {
        try
        {
            var connStr = TenantContext.BuildTenantConn(slug);
            await using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT 1 FROM app_users WHERE mobile=@m OR (@d <> '' AND device_id=@d) LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@m", mobile);
            cmd.Parameters.AddWithValue("@d", deviceId ?? "");
            var r = await cmd.ExecuteScalarAsync();
            return r != null;
        }
        catch (MySqlException)
        {
            return false;
        }
    }

    public async Task PurgeRegistryForMobileOrDeviceAsync(string mobile, string deviceId, string slug)
    {
        await using var conn = DbFactory.CreateMaster();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            DELETE FROM app_user_registry
             WHERE agency_slug = @s
               AND (mobile = @m OR (@d <> '' AND device_id = @d))", conn);
        cmd.Parameters.AddWithValue("@s", slug);
        cmd.Parameters.AddWithValue("@m", mobile);
        cmd.Parameters.AddWithValue("@d", deviceId ?? "");
        await cmd.ExecuteNonQueryAsync();
    }


    public async Task<(bool Success, string Reason, long UserId)> RegisterAsync(
        string mobile, string name, string? address, string? pincode,
        string? pfpBase64, string deviceId,
        string? aadhaarFront, string? aadhaarBack, string? panFront,
        string? accountNumber, string? ifscCode,
        string? selfieWithAadhaar = null,
        string? aadhaarNumber = null, string? aadhaarName = null, string? aadhaarDob = null,
        string? aadhaarGender = null, string? aadhaarAddress = null, bool aadhaarVerified = false,
        double? regLat = null, double? regLng = null, string? regLocation = null,
        string? aadhaarPhoto = null)
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
            VALUES (@mobile, @name, @addr, @pin, NULL, @did,
                    @acct, @ifsc, 0, 0);
            SELECT LAST_INSERT_ID();";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@mobile", mobile);
        cmd.Parameters.AddWithValue("@name",   name);
        cmd.Parameters.AddWithValue("@addr",   (object?)address       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pin",    (object?)pincode       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@did",    deviceId);
        cmd.Parameters.AddWithValue("@acct",   (object?)accountNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ifsc",   (object?)ifscCode      ?? DBNull.Value);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());

        var pfpPath = await SaveBase64ImageAsync(pfpBase64, "pfp", $"user_{id}.jpg");
        if (pfpPath != null)
        {
            await using var pfpCmd = new MySqlCommand(
                "UPDATE app_users SET pfp = @p WHERE id = @id", conn);
            pfpCmd.Parameters.AddWithValue("@p",  pfpPath);
            pfpCmd.Parameters.AddWithValue("@id", id);
            await pfpCmd.ExecuteNonQueryAsync();
        }

        bool hasKyc = aadhaarFront != null || aadhaarBack != null
                      || panFront != null || selfieWithAadhaar != null || aadhaarPhoto != null;
        if (hasKyc)
        {
            var kycDir   = $"kyc/{id}";
            var afPath   = await SaveBase64ImageAsync(aadhaarFront,      kycDir, "aadhaar_front.jpg");
            var abPath   = await SaveBase64ImageAsync(aadhaarBack,       kycDir, "aadhaar_back.jpg");
            var pfPath   = await SaveBase64ImageAsync(panFront,          kycDir, "pan_front.jpg");
            var selPath  = await SaveBase64ImageAsync(selfieWithAadhaar, kycDir, "selfie.jpg");
            var uidaiPath = await SaveBase64ImageAsync(aadhaarPhoto,     kycDir, "aadhaar_photo.jpg");

            const string kycSql = @"
                INSERT INTO user_kyc (user_id, aadhaar_front, aadhaar_back, pan_front, selfie, aadhaar_photo)
                VALUES (@uid, @af, @ab, @pf, @sel, @uidai)";
            await using var kycCmd = new MySqlCommand(kycSql, conn);
            kycCmd.Parameters.AddWithValue("@uid", id);
            kycCmd.Parameters.AddWithValue("@af",    (object?)afPath    ?? DBNull.Value);
            kycCmd.Parameters.AddWithValue("@ab",    (object?)abPath    ?? DBNull.Value);
            kycCmd.Parameters.AddWithValue("@pf",    (object?)pfPath    ?? DBNull.Value);
            kycCmd.Parameters.AddWithValue("@sel",   (object?)selPath   ?? DBNull.Value);
            kycCmd.Parameters.AddWithValue("@uidai", (object?)uidaiPath ?? DBNull.Value);
            await kycCmd.ExecuteNonQueryAsync();
        }

        var digits = new string((aadhaarNumber ?? "").Where(char.IsDigit).ToArray());
        var last4  = digits.Length >= 4 ? digits[^4..] : null;
        bool hasDemo = aadhaarVerified || last4 != null || regLat != null || regLng != null
                       || !string.IsNullOrWhiteSpace(regLocation);
        if (hasDemo)
        {
            const string demoSql = @"
                UPDATE app_users SET
                    kyc_aadhaar_last4    = @l4,
                    kyc_aadhaar_number   = @anum,
                    kyc_aadhaar_name     = @an,
                    kyc_aadhaar_dob      = @adob,
                    kyc_aadhaar_gender   = @agen,
                    kyc_aadhaar_address  = @aaddr,
                    kyc_aadhaar_verified = @aver,
                    kyc_verified_at      = CASE WHEN @aver=1 THEN UTC_TIMESTAMP() ELSE kyc_verified_at END,
                    -- New registrants go into the KYC REVIEW QUEUE first: the
                    -- admin verifies their Aadhaar/selfie (kyc_status -> success),
                    -- and only then does the activation gate show awaiting
                    -- approval. So a fresh registration is always pending here.
                    kyc_status           = 'pending',
                    kyc_reg_lat          = @lat,
                    kyc_reg_lng          = @lng,
                    kyc_reg_location     = @loc
                WHERE id = @id";
            await using var demoCmd = new MySqlCommand(demoSql, conn);
            demoCmd.Parameters.AddWithValue("@l4",    (object?)last4 ?? DBNull.Value);
            demoCmd.Parameters.AddWithValue("@anum",  (object?)(digits.Length == 12 ? digits : null) ?? DBNull.Value);
            demoCmd.Parameters.AddWithValue("@an",    (object?)aadhaarName    ?? DBNull.Value);
            demoCmd.Parameters.AddWithValue("@adob",  (object?)aadhaarDob     ?? DBNull.Value);
            demoCmd.Parameters.AddWithValue("@agen",  (object?)aadhaarGender  ?? DBNull.Value);
            demoCmd.Parameters.AddWithValue("@aaddr", (object?)aadhaarAddress ?? DBNull.Value);
            demoCmd.Parameters.AddWithValue("@aver",  aadhaarVerified ? 1 : 0);
            demoCmd.Parameters.AddWithValue("@lat",   (object?)regLat ?? DBNull.Value);
            demoCmd.Parameters.AddWithValue("@lng",   (object?)regLng ?? DBNull.Value);
            demoCmd.Parameters.AddWithValue("@loc",   (object?)regLocation ?? DBNull.Value);
            demoCmd.Parameters.AddWithValue("@id",    id);
            await demoCmd.ExecuteNonQueryAsync();
        }

        return (true, "registered", id);
    }

    public async Task UpdateKycFieldsAsync(long userId, Dictionary<string, object?> fields)
    {
        if (fields.Count == 0) return;
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        var sets = string.Join(", ", fields.Keys.Select(k => $"`{k}`=@{k}"));
        await using var cmd = new MySqlCommand($"UPDATE app_users SET {sets} WHERE id=@id", conn);
        foreach (var kv in fields) cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> ResubmitKycAsync(
        string mobile,
        string? aadhaarFront, string? aadhaarBack, string? panFront,
        string? selfieWithAadhaar, string? aadhaarPhoto,
        string? aadhaarNumber, string? aadhaarName, string? aadhaarDob,
        string? aadhaarGender, string? aadhaarAddress, bool aadhaarVerified,
        double? regLat, double? regLng, string? regLocation)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();

        long id;
        await using (var find = new MySqlCommand("SELECT id FROM app_users WHERE mobile=@m LIMIT 1", conn))
        {
            find.Parameters.AddWithValue("@m", mobile);
            var v = await find.ExecuteScalarAsync();
            if (v is null or DBNull) return false;
            id = Convert.ToInt64(v);
        }

        var kycDir    = $"kyc/{id}";
        var afPath    = await SaveBase64ImageAsync(aadhaarFront,      kycDir, "aadhaar_front.jpg");
        var abPath    = await SaveBase64ImageAsync(aadhaarBack,       kycDir, "aadhaar_back.jpg");
        var pfPath    = await SaveBase64ImageAsync(panFront,          kycDir, "pan_front.jpg");
        var selPath   = await SaveBase64ImageAsync(selfieWithAadhaar, kycDir, "selfie.jpg");
        var uidaiPath = await SaveBase64ImageAsync(aadhaarPhoto,      kycDir, "aadhaar_photo.jpg");

        await using (var kyc = new MySqlCommand(@"
            INSERT INTO user_kyc (user_id, aadhaar_front, aadhaar_back, pan_front, selfie, aadhaar_photo)
            VALUES (@uid, @af, @ab, @pf, @sel, @uidai)
            ON DUPLICATE KEY UPDATE
                aadhaar_front = COALESCE(@af, aadhaar_front),
                aadhaar_back  = COALESCE(@ab, aadhaar_back),
                pan_front     = COALESCE(@pf, pan_front),
                selfie        = COALESCE(@sel, selfie),
                aadhaar_photo = COALESCE(@uidai, aadhaar_photo)", conn))
        {
            kyc.Parameters.AddWithValue("@uid",   id);
            kyc.Parameters.AddWithValue("@af",    (object?)afPath    ?? DBNull.Value);
            kyc.Parameters.AddWithValue("@ab",    (object?)abPath    ?? DBNull.Value);
            kyc.Parameters.AddWithValue("@pf",    (object?)pfPath    ?? DBNull.Value);
            kyc.Parameters.AddWithValue("@sel",   (object?)selPath   ?? DBNull.Value);
            kyc.Parameters.AddWithValue("@uidai", (object?)uidaiPath ?? DBNull.Value);
            await kyc.ExecuteNonQueryAsync();
        }

        var digits = new string((aadhaarNumber ?? "").Where(char.IsDigit).ToArray());
        var last4  = digits.Length >= 4 ? digits[^4..] : null;
        await using (var upd = new MySqlCommand(@"
            UPDATE app_users SET
                kyc_aadhaar_last4    = @l4,
                kyc_aadhaar_number   = @anum,
                kyc_aadhaar_name     = @an,
                kyc_aadhaar_dob      = @adob,
                kyc_aadhaar_gender   = @agen,
                kyc_aadhaar_address  = @aaddr,
                kyc_aadhaar_verified = @aver,
                kyc_verified_at      = CASE WHEN @aver=1 THEN UTC_TIMESTAMP() ELSE kyc_verified_at END,
                kyc_reg_lat          = COALESCE(@lat, kyc_reg_lat),
                kyc_reg_lng          = COALESCE(@lng, kyc_reg_lng),
                kyc_reg_location     = COALESCE(@loc, kyc_reg_location),
                kyc_status           = 'pending',
                kyc_reject_note      = NULL
            WHERE id=@id", conn))
        {
            upd.Parameters.AddWithValue("@l4",    (object?)last4 ?? DBNull.Value);
            upd.Parameters.AddWithValue("@anum",  (object?)(digits.Length == 12 ? digits : null) ?? DBNull.Value);
            upd.Parameters.AddWithValue("@an",    (object?)aadhaarName    ?? DBNull.Value);
            upd.Parameters.AddWithValue("@adob",  (object?)aadhaarDob     ?? DBNull.Value);
            upd.Parameters.AddWithValue("@agen",  (object?)aadhaarGender  ?? DBNull.Value);
            upd.Parameters.AddWithValue("@aaddr", (object?)aadhaarAddress ?? DBNull.Value);
            upd.Parameters.AddWithValue("@aver",  aadhaarVerified ? 1 : 0);
            upd.Parameters.AddWithValue("@lat",   (object?)regLat ?? DBNull.Value);
            upd.Parameters.AddWithValue("@lng",   (object?)regLng ?? DBNull.Value);
            upd.Parameters.AddWithValue("@loc",   (object?)regLocation ?? DBNull.Value);
            upd.Parameters.AddWithValue("@id",    id);
            await upd.ExecuteNonQueryAsync();
        }
        return true;
    }

    public async Task<bool> IsMobileRegisteredAsync(string mobile)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT EXISTS(SELECT 1 FROM app_users WHERE mobile=@m LIMIT 1)", conn) { CommandTimeout = 5 };
        cmd.Parameters.AddWithValue("@m", mobile);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1;
    }

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
                       '') AS sub_end,
                   COALESCE(u.is_blacklisted,0),
                   COALESCE(u.is_stopped,0),
                   COALESCE(u.kyc_status,'success'),
                   COALESCE(u.kyc_reject_note,'')
            FROM app_users u
            WHERE u.mobile = @m
            LIMIT 1";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@m", mobile);
        await using var rdr = await cmd.ExecuteReaderAsync();

        if (!await rdr.ReadAsync())
            return new AuthResponse(false, "Not registered.", "not_found",
                null, null, null, false, null, null);

        var id            = rdr.GetInt64(0);
        var name          = rdr.GetString(1);
        var dbMobile      = rdr.GetString(2);
        var dbDevice      = rdr.IsDBNull(3) ? null : rdr.GetString(3);
        var isActive      = rdr.GetInt32(4) == 1;
        var isAdmin       = rdr.GetInt32(5) == 1;
        var pfp           = rdr.IsDBNull(6) ? null : rdr.GetString(6);
        var subEnd        = rdr.GetString(7);
        var isBlacklisted = rdr.GetInt32(8) == 1;
        var isStopped     = rdr.GetInt32(9) == 1;
        var kycStatus     = rdr.GetString(10);
        var kycRejectNote = rdr.GetString(11);

        if (isBlacklisted)
            return new AuthResponse(false,
                "You have been blocked by the agency. Please contact the agency for assistance.",
                "blacklisted", null, null, null, false, null, null);

        if (!string.IsNullOrEmpty(dbDevice) &&
            !string.Equals(dbDevice, deviceId, StringComparison.OrdinalIgnoreCase))
        {
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
            catch { }
            return new AuthResponse(false,
                "This mobile number is registered on another device. Ask admin to reset your device.",
                "device_mismatch", null, null, null, false, null, null);
        }

        if (string.Equals(kycStatus, "failed", StringComparison.OrdinalIgnoreCase))
            return new AuthResponse(false,
                string.IsNullOrWhiteSpace(kycRejectNote)
                    ? "Your KYC was rejected. Please re-submit your documents."
                    : kycRejectNote,
                "kyc_failed", null, null, null, false, null, null);

        if (string.Equals(kycStatus, "pending", StringComparison.OrdinalIgnoreCase))
            return new AuthResponse(false,
                "Your KYC is under review. You can log in once the agency verifies your documents.",
                "kyc_pending", null, null, null, false, null, null);

        if (!isActive)
            return new AuthResponse(false,
                "Your KYC is verified. Your account is pending admin activation. Please wait.",
                "pending_approval", null, null, null, false, null, null);

        if (isStopped)
            return new AuthResponse(false,
                "Your app has been stopped by admin. Please contact agency to start app.",
                "app_stopped", null, null, null, false, null, null);

        return new AuthResponse(true, "Login successful.", "ok",
            id, name, dbMobile, isAdmin, pfp, subEnd == "" ? null : subEnd);
    }

    public async Task HeartbeatAsync(long userId, double? lat, double? lng)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_users SET last_seen=NOW(), last_lat=COALESCE(@lat, last_lat), last_lng=COALESCE(@lng, last_lng) WHERE id=@uid",
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
            ORDER BY last_seen DESC";
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

    public async Task<bool> HasActiveSubscriptionAsync(long userId)
    {
        var ck = $"{TenantContext.Key}:{userId}";
        if (_subCache.TryGetValue(ck, out var sc) && DateTime.UtcNow - sc.At < SubCacheTtl)
            return sc.Active;

        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = "SELECT COUNT(*) FROM subscriptions WHERE user_id=@uid AND end_date >= CURDATE()";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        var active = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;

        _subCache[ck] = (active, DateTime.UtcNow);
        return active;
    }

    public async Task<ProfileResponse?> GetProfileAsync(long userId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();

        const string sql = @"
            SELECT u.id, u.name, u.mobile, u.address, u.pincode, u.pfp,
                   u.is_active, u.is_admin,
                   COALESCE((SELECT SUM(amount) FROM subscriptions WHERE user_id = u.id), 0) AS balance,
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
            PfpUrl:        S(5),
            IsActive:      rdr.GetInt32(6) == 1,
            IsAdmin:       rdr.GetInt32(7) == 1,
            Balance:       rdr.GetDecimal(8),
            CreatedAt:     rdr.GetString(9),
            AccountNumber: S(10),
            IfscCode:      S(11),
            Kyc: new KycInfo(kycSubmitted, aadhaarFront, aadhaarBack, panFront),
            Subscriptions: new List<SubscriptionRecord>());

        await rdr.CloseAsync();

        string? selfie = null, uidaiPhoto = null;
        try
        {
            await using var kc = new MySqlCommand(
                "SELECT selfie, aadhaar_photo FROM user_kyc WHERE user_id=@id LIMIT 1", conn);
            kc.Parameters.AddWithValue("@id", userId);
            await using var kr = await kc.ExecuteReaderAsync();
            if (await kr.ReadAsync())
            {
                selfie     = kr.IsDBNull(0) ? null : kr.GetString(0);
                uidaiPhoto = kr.IsDBNull(1) ? null : kr.GetString(1);
            }
        }
        catch { }

        string  kycStatus = "success"; string? rejectNote = null;
        bool    aaVer = false;
        string? aaNum = null, aaLast4 = null, aaName = null, aaDob = null, aaGen = null, aaAddr = null;
        double? lat = null, lng = null; string? loc = null;
        try
        {
            await using var dc = new MySqlCommand(@"
                SELECT COALESCE(kyc_status,'success'), kyc_reject_note,
                       kyc_aadhaar_verified, kyc_aadhaar_number, kyc_aadhaar_last4,
                       kyc_aadhaar_name, kyc_aadhaar_dob, kyc_aadhaar_gender, kyc_aadhaar_address,
                       kyc_reg_lat, kyc_reg_lng, kyc_reg_location
                FROM app_users WHERE id=@id LIMIT 1", conn);
            dc.Parameters.AddWithValue("@id", userId);
            await using var dr = await dc.ExecuteReaderAsync();
            if (await dr.ReadAsync())
            {
                string? DS(int i) => dr.IsDBNull(i) ? null : dr.GetString(i);
                kycStatus  = DS(0) ?? "success"; rejectNote = DS(1);
                aaVer      = !dr.IsDBNull(2) && dr.GetInt32(2) != 0;
                aaNum      = DS(3); aaLast4 = DS(4); aaName = DS(5); aaDob = DS(6);
                aaGen      = DS(7); aaAddr  = DS(8);
                lat        = dr.IsDBNull(9)  ? (double?)null : dr.GetDouble(9);
                lng        = dr.IsDBNull(10) ? (double?)null : dr.GetDouble(10);
                loc        = DS(11);
            }
        }
        catch { }

        profile = profile with {
            Kyc = profile.Kyc with {
                Selfie          = selfie,
                AadhaarPhoto    = uidaiPhoto,
                KycStatus       = kycStatus,
                RejectNote      = rejectNote,
                AadhaarVerified = aaVer,
                AadhaarNumber   = aaNum,
                AadhaarLast4    = aaLast4,
                AadhaarName     = aaName,
                AadhaarDob      = aaDob,
                AadhaarGender   = aaGen,
                AadhaarAddress  = aaAddr,
                Lat             = lat,
                Lng             = lng,
                LocationLabel   = loc
            }
        };

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

    public async Task<string?> UpdatePfpAsync(long userId, string? pfpBase64)
    {
        var pfpPath = await SaveBase64ImageAsync(pfpBase64, "pfp", $"user_{userId}.jpg");
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_users SET pfp = @p WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@p",  (object?)pfpPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
        return pfpPath;
    }

    public async Task<UserStatusDto> GetUserStatusAsync(long userId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT is_active, COALESCE(is_stopped,0), COALESCE(is_blacklisted,0) FROM app_users WHERE id=@id LIMIT 1",
            conn) { CommandTimeout = 5 };
        cmd.Parameters.AddWithValue("@id", userId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return new UserStatusDto(false, false, false);
        return new UserStatusDto(rdr.GetInt32(0)==1, rdr.GetInt32(1)==1, rdr.GetInt32(2)==1);
    }

    private async Task<List<int>> GetFinanceRestrictionsAsync(long userId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT finance_id FROM user_finance_restrictions WHERE user_id=@uid", conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        var ids = new List<int>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) ids.Add(rdr.GetInt32(0));
        return ids;
    }

    private static string FinanceScope(int financeId) =>
        financeId > 0 ? $"AND b.finance_id = {financeId}" : "";

    public async Task<List<SearchResult>> SearchByRcAsync(string last4, long userId, int financeId = 0)
    {
        var restricted = await GetFinanceRestrictionsAsync(userId);
        var filter = restricted.Count > 0
            ? $"AND b.finance_id NOT IN ({string.Join(",", restricted)})"
            : "";
        return await SearchAsync($@"
            SELECT {SelectFields}
            FROM rc_info ri
            INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE ri.last4 = @q {filter} {FinanceScope(financeId)}
            ORDER BY b.name, vr.vehicle_no",
            last4.ToUpper());
    }

    public async Task<List<SearchResult>> SearchByChassisAsync(string last5, long userId, int financeId = 0)
    {
        var restricted = await GetFinanceRestrictionsAsync(userId);
        var filter = restricted.Count > 0
            ? $"AND b.finance_id NOT IN ({string.Join(",", restricted)})"
            : "";
        return await SearchAsync($@"
            SELECT {SelectFields}
            FROM chassis_info ci
            INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE ci.last5 = @q {filter} {FinanceScope(financeId)}
            ORDER BY b.name, vr.chassis_no",
            last5.ToUpper());
    }

    private const string LiteFields = @"
        vr.id, vr.vehicle_no, vr.chassis_no, vr.model,
        COALESCE(f.name,'') AS financer, b.name AS branch_name,
        COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y, %h:%i %p'),'') AS created_on";

    private static string BestPerGroupFilter(string groupCol) => $@"
        AND NOT EXISTS (
            SELECT 1 FROM vehicle_records dup
            WHERE dup.{groupCol} = vr.{groupCol}
              AND (dup.completeness, dup.id) > (vr.completeness, vr.id)
        )";

    public async Task<List<SearchResult>> SearchByRcLiteAsync(string last4, long userId, int financeId = 0)
    {
        var restricted = await GetFinanceRestrictionsAsync(userId);
        var filter = restricted.Count > 0
            ? $"AND b.finance_id NOT IN ({string.Join(",", restricted)})" : "";
        return await SearchLiteAsync($@"
            SELECT {LiteFields}
            FROM rc_info ri
            INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE ri.last4 = @q {filter} {FinanceScope(financeId)}
            {BestPerGroupFilter("vehicle_no")}", last4.ToUpper());
    }

    public async Task<List<SearchResult>> SearchByChassisLiteAsync(string last5, long userId, int financeId = 0)
    {
        var restricted = await GetFinanceRestrictionsAsync(userId);
        var filter = restricted.Count > 0
            ? $"AND b.finance_id NOT IN ({string.Join(",", restricted)})" : "";
        return await SearchLiteAsync($@"
            SELECT {LiteFields}
            FROM chassis_info ci
            INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE ci.last5 = @q {filter} {FinanceScope(financeId)}
            {BestPerGroupFilter("chassis_no")}", last5.ToUpper());
    }

    public async Task<List<SearchResult>> GetVehicleBranchesAsync(string key, long userId, int financeId = 0)
    {
        var restricted = await GetFinanceRestrictionsAsync(userId);
        var filter = restricted.Count > 0
            ? $"AND b.finance_id NOT IN ({string.Join(",", restricted)})" : "";
        return await SearchAsync($@"
            SELECT {SelectFields}
            FROM vehicle_records vr
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE (vr.vehicle_no = @q OR vr.chassis_no = @q) {filter} {FinanceScope(financeId)}
            ORDER BY vr.completeness DESC, vr.id DESC", key.ToUpper());
    }

    public async Task<List<HeadOffice>> GetHeadOfficesAsync(long userId)
    {
        var restricted = await GetFinanceRestrictionsAsync(userId);
        var filter = restricted.Count > 0
            ? $"AND f.id NOT IN ({string.Join(",", restricted)})" : "";
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand($@"
            SELECT f.id, f.name, COALESCE(SUM(b.total_records),0) AS total_records
            FROM finances f
            LEFT JOIN branches b ON b.finance_id = f.id AND b.is_active = 1
            WHERE f.is_active = 1 {filter}
            GROUP BY f.id, f.name
            ORDER BY f.name ASC", conn) { CommandTimeout = 15 };
        var list = new List<HeadOffice>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new HeadOffice(r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    public async Task<RepoLetterSettings> GetRepoSettingsAsync(int financeId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            SELECT finance_id, agency_name, authorized_by, police_station, police_address, logo_path
            FROM repo_letter_settings WHERE finance_id IN (0, @fid)", conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@fid", financeId);

        string? agencyLvlName = null, agencyLvlAuth = null, police = null, policeAddr = null, agencyLvlLogo = null;
        string? finName = null, finAuth = null, finLogo = null;
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            while (await r.ReadAsync())
            {
                if (r.GetInt32(0) == 0)
                {
                    agencyLvlName = S(1); agencyLvlAuth = S(2);
                    police = S(3); policeAddr = S(4); agencyLvlLogo = S(5);
                }
                else
                {
                    finName = S(1); finAuth = S(2); finLogo = S(5);
                }
            }
        }
        return new RepoLetterSettings(
            financeId,
            finName ?? agencyLvlName,
            finAuth ?? agencyLvlAuth,
            police,
            policeAddr,
            finLogo ?? agencyLvlLogo);
    }

    public async Task<BillingSettings> GetBillingSettingsAsync()
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            SELECT agency_name, header_address, header_contact, header_email,
                   pan_no, gst_state, bank_account_name, account_no, ifsc_code,
                   bank_branch, parking_yard, payment_name, footer_line, logo_path
            FROM billing_settings WHERE id = 1 LIMIT 1", conn) { CommandTimeout = 10 };
        await using var r = await cmd.ExecuteReaderAsync();
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        if (!await r.ReadAsync())
            return new BillingSettings(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        return new BillingSettings(
            S(0), S(1), S(2), S(3), S(4), S(5), S(6), S(7), S(8), S(9), S(10), S(11), S(12), S(13));
    }

    public async Task<string?> SaveBillingLogoAsync(string? imageBase64)
    {
        var slug = TenantContext.Key;
        var relPath = await SaveBase64ImageAsync(imageBase64, "billing-logos", $"{slug}_letterhead.jpg");
        if (relPath == null) return null;

        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            INSERT INTO billing_settings (id, logo_path) VALUES (1, @lp)
            ON DUPLICATE KEY UPDATE logo_path = VALUES(logo_path)", conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@lp", relPath);
        await cmd.ExecuteNonQueryAsync();
        return relPath;
    }

    public async Task SaveBillingSettingsAsync(BillingSettings b)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            INSERT INTO billing_settings
              (id, agency_name, header_address, header_contact, header_email,
               pan_no, gst_state, bank_account_name, account_no, ifsc_code,
               bank_branch, parking_yard, payment_name, footer_line)
            VALUES (1, @an, @ha, @hc, @he, @pan, @gst, @ban, @acc, @ifsc, @bb, @py, @pn, @fl)
            ON DUPLICATE KEY UPDATE
               agency_name=VALUES(agency_name), header_address=VALUES(header_address),
               header_contact=VALUES(header_contact), header_email=VALUES(header_email),
               pan_no=VALUES(pan_no), gst_state=VALUES(gst_state),
               bank_account_name=VALUES(bank_account_name), account_no=VALUES(account_no),
               ifsc_code=VALUES(ifsc_code), bank_branch=VALUES(bank_branch),
               parking_yard=VALUES(parking_yard), payment_name=VALUES(payment_name),
               footer_line=VALUES(footer_line)", conn) { CommandTimeout = 10 };
        void P(string k, string? v) => cmd.Parameters.AddWithValue(k, (object?)v ?? DBNull.Value);
        P("@an", b.AgencyName); P("@ha", b.HeaderAddress); P("@hc", b.HeaderContact); P("@he", b.HeaderEmail);
        P("@pan", b.PanNo); P("@gst", b.GstState); P("@ban", b.BankAccountName); P("@acc", b.AccountNo);
        P("@ifsc", b.IfscCode); P("@bb", b.BankBranch); P("@py", b.ParkingYard); P("@pn", b.PaymentName);
        P("@fl", b.FooterLine);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> SaveRepoLogoAsync(int financeId, string? imageBase64)
    {
        var slug = TenantContext.Key;
        var relPath = await SaveBase64ImageAsync(imageBase64, "repo-logos", $"{slug}_finance_{financeId}.jpg");
        if (relPath == null) return null;

        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(@"
            INSERT INTO repo_letter_settings (finance_id, logo_path)
            VALUES (@fid, @lp)
            ON DUPLICATE KEY UPDATE logo_path = VALUES(logo_path)", conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@fid", financeId);
        cmd.Parameters.AddWithValue("@lp", relPath);
        await cmd.ExecuteNonQueryAsync();
        return relPath;
    }

    public async Task SaveRepoSettingsAsync(int financeId, SaveRepoSettingsRequest req)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();

        await using (var cmd = new MySqlCommand(@"
            INSERT INTO repo_letter_settings (finance_id, police_station, police_address)
            VALUES (0, @ps, @pa)
            ON DUPLICATE KEY UPDATE
                police_station = VALUES(police_station),
                police_address = VALUES(police_address)", conn) { CommandTimeout = 10 })
        {
            cmd.Parameters.AddWithValue("@ps", (object?)req.PoliceStation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pa", (object?)req.PoliceAddress ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new MySqlCommand(@"
            INSERT INTO repo_letter_settings (finance_id, agency_name, authorized_by)
            VALUES (@fid, @an, @ab)
            ON DUPLICATE KEY UPDATE
                agency_name   = VALUES(agency_name),
                authorized_by = VALUES(authorized_by)", conn) { CommandTimeout = 10 })
        {
            cmd.Parameters.AddWithValue("@fid", financeId);
            cmd.Parameters.AddWithValue("@an", (object?)req.AgencyName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ab", (object?)req.AuthorizedBy ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<SearchResult>> SearchLiteAsync(string sql, string query)
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
                Id: r.GetInt64(0), VehicleNo: S(1), ChassisNo: S(2), Model: S(3),
                Financer: S(4), BranchName: S(5), CreatedOn: S(6),
                EngineNo: "", AgreementNo: "", CustomerName: "", CustomerContact: "",
                CustomerAddress: "", FirstContact: "", SecondContact: "", ThirdContact: "",
                Address: "", Region: "", Area: "", Bucket: "", GV: "", OD: "", Seasoning: "",
                TbrFlag: "", Sec9: "", Sec17: "", Level1: "", Level1Contact: "", Level2: "",
                Level2Contact: "", Level3: "", Level3Contact: "", Level4: "", Level4Contact: "",
                SenderMail1: "", SenderMail2: "", ExecutiveName: "", Pos: "", Toss: "",
                Remark: "", BranchFromExcel: ""));
        return list;
    }

    public async Task<SearchResult?> GetRecordByIdAsync(long id)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand($@"
            SELECT {SelectFields}
            FROM vehicle_records vr
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE vr.id = @id LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        if (!await r.ReadAsync()) return null;
        return new SearchResult(
            r.GetInt64(0), S(1), S(2), S(3), S(4), S(5),
            S(6), S(7), S(8), S(9), S(10), S(11), S(12), S(13),
            S(14), S(15), S(16), S(17), S(18), S(19), S(20), S(21),
            S(22), S(23), S(24), S(25), S(26), S(27), S(28), S(29),
            S(30), S(31), S(32), S(33), S(34), S(35), S(36), S(37),
            S(38), S(39));
    }

    public async Task<List<SubscriptionRecord>> GetUserSubscriptionsAsync(long userId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = @"
            SELECT id, DATE_FORMAT(start_date,'%Y-%m-%d'), DATE_FORMAT(end_date,'%Y-%m-%d'),
                   COALESCE(amount,0), notes,
                   (end_date >= CURDATE()) AS is_active
            FROM subscriptions WHERE user_id=@id ORDER BY end_date DESC";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@id", userId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        var list = new List<SubscriptionRecord>();
        while (await rdr.ReadAsync())
            list.Add(new SubscriptionRecord(
                rdr.GetInt64(0),
                rdr.GetString(1),
                rdr.GetString(2),
                rdr.GetDecimal(3),
                rdr.IsDBNull(4) ? null : rdr.GetString(4),
                rdr.GetBoolean(5)));
        return list;
    }

    public async Task<bool> VerifySubsPasswordAsync(string password)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `value` FROM app_settings WHERE `key`='subs_password' LIMIT 1", conn);
        var stored = await cmd.ExecuteScalarAsync();
        return stored?.ToString() == password;
    }

    public async Task<List<AdminUserItem>> GetAdminUsersAsync()
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = @"
            SELECT u.id, u.name, u.mobile, COALESCE(u.address,''),
                   (SELECT DATE_FORMAT(MAX(s.end_date),'%Y-%m-%d')
                    FROM subscriptions s WHERE s.user_id=u.id) AS sub_end,
                   COALESCE(u.is_active,0), COALESCE(u.is_admin,0),
                   COALESCE(u.is_stopped,0), COALESCE(u.is_blacklisted,0)
            FROM app_users u ORDER BY u.name ASC";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 10 };
        var list = new List<AdminUserItem>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(new AdminUserItem(
                rdr.GetInt64(0), rdr.GetString(1), rdr.GetString(2),
                rdr.GetString(3), rdr.IsDBNull(4) ? null : rdr.GetString(4),
                rdr.GetBoolean(5), rdr.GetBoolean(6), rdr.GetBoolean(7), rdr.GetBoolean(8)));
        return list;
    }

    public async Task<bool> VerifyAdminPasswordAsync(long userId, string password)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT `value` FROM app_settings WHERE `key`='control_panel_password' LIMIT 1",
            conn) { CommandTimeout = 5 };
        var stored = await cmd.ExecuteScalarAsync() as string;
        return !string.IsNullOrWhiteSpace(stored) && stored == password;
    }

    public async Task SetUserActiveAsync(long userId, bool active)   => await SetUserFlagAsync(userId, "is_active", active);
    public async Task SetUserStoppedAsync(long userId, bool stopped) => await SetUserFlagAsync(userId, "is_stopped", stopped);
    public async Task SetUserAdminAsync(long userId, bool admin)     => await SetUserFlagAsync(userId, "is_admin", admin);

    public async Task SetUserBlacklistedAsync(long userId, bool blacklisted)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE app_users SET is_blacklisted=@v, is_stopped=@v WHERE id=@id", conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@v", blacklisted ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetUserKycStatusAsync(long userId, string status, string? note)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        if (status == "failed")
        {
            await using var cmd = new MySqlCommand(
                "UPDATE app_users SET kyc_status='failed', kyc_reject_note=@n, is_active=0 WHERE id=@id",
                conn) { CommandTimeout = 10 };
            cmd.Parameters.AddWithValue("@n", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var cmd = new MySqlCommand(
                "UPDATE app_users SET kyc_status=@s, kyc_reject_note=NULL WHERE id=@id",
                conn) { CommandTimeout = 10 };
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@id", userId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task SetUserFlagAsync(long userId, string col, bool val)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            $"UPDATE app_users SET {col}=@v WHERE id=@id", conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@v", val ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddSubscriptionAsync(long userId, string startDate, string endDate,
        decimal amount, string? notes)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO subscriptions (user_id,start_date,end_date,amount,notes) VALUES (@uid,@s,@e,@a,@n)",
            conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@s",   startDate);
        cmd.Parameters.AddWithValue("@e",   endDate);
        cmd.Parameters.AddWithValue("@a",   amount);
        cmd.Parameters.AddWithValue("@n",   (object?)notes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        InvalidateSubCache(userId);
    }

    public async Task DeleteSubscriptionAsync(long subId)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM subscriptions WHERE id=@id", conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@id", subId);
        await cmd.ExecuteNonQueryAsync();
    }

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
        COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y, %h:%i %p'),'') AS created_on";

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

    public async Task LogSearchAsync(
        long userId, string vehicleNo, string chassisNo, string model,
        double? lat, double? lng, string? address, DateTime deviceTime)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO search_logs
                (user_id, vehicle_no, chassis_no, model, lat, lng, address, device_time)
            VALUES (@uid, @vno, @cno, @mdl, @lat, @lng, @addr, @dt)";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 5 };
        cmd.Parameters.AddWithValue("@uid",  userId);
        cmd.Parameters.AddWithValue("@vno",  vehicleNo);
        cmd.Parameters.AddWithValue("@cno",  chassisNo);
        cmd.Parameters.AddWithValue("@mdl",  model);
        cmd.Parameters.AddWithValue("@lat",  lat.HasValue  ? (object)lat.Value  : DBNull.Value);
        cmd.Parameters.AddWithValue("@lng",  lng.HasValue  ? (object)lng.Value  : DBNull.Value);
        cmd.Parameters.AddWithValue("@addr", (object?)address ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dt",   deviceTime);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(long vehicleRecords, long rcRecords, long chassisRecords)> GetStatsAsync()
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        const string sql = @"
            SELECT
                (SELECT COUNT(*) FROM vehicle_records),
                (SELECT COUNT(*) FROM rc_info),
                (SELECT COUNT(*) FROM chassis_info)";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 15 };
        await using var r   = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2));
    }

    public async Task<long> SubmitRepoAsync(VKmobileapi.Models.RepoSubmitRequest req)
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();

        int?    financeId   = null;
        string? financeName = null;
        string? branchName  = req.Branch;
        if (req.RecordId is > 0)
        {
            await using var lookup = new MySqlCommand(@"
                SELECT b.finance_id, f.name, b.name
                  FROM vehicle_records vr
                  JOIN branches b  ON b.id = vr.branch_id
             LEFT JOIN finances f  ON f.id = b.finance_id
                 WHERE vr.id = @id LIMIT 1", conn) { CommandTimeout = 10 };
            lookup.Parameters.AddWithValue("@id", req.RecordId!.Value);
            await using var lr = await lookup.ExecuteReaderAsync();
            if (await lr.ReadAsync())
            {
                if (!lr.IsDBNull(0)) financeId = lr.GetInt32(0);
                if (!lr.IsDBNull(1)) financeName = lr.GetString(1);
                if (!lr.IsDBNull(2) && string.IsNullOrWhiteSpace(branchName)) branchName = lr.GetString(2);
            }
        }

        string action = (req.BillingAction ?? "immediate").Trim().ToLowerInvariant();
        if (action != "immediate" && action != "hold" && action != "cancel") action = "immediate";

        DateTime? holdUntil = null;
        if (!string.IsNullOrWhiteSpace(req.HoldUntil) &&
            DateTime.TryParse(req.HoldUntil, out var hu)) holdUntil = hu.Date;

        await using var cmd = new MySqlCommand(@"
            INSERT INTO repo_submissions
                (record_id, finance_id, finance_name, branch_name,
                 loan_no, customer_name, vehicle_no, model, chassis_no, engine_no,
                 agent_name, parking_yard_name, parking_yard_mobile, load_details,
                 addl_charges_notes, addl_charges_amount,
                 confirmation_by_name, confirmation_by_mobile, executive_name,
                 collection_update, remark,
                 billing_action, hold_until, hold_days, submitted_by_name)
            VALUES
                (@rid, @fid, @fname, @branch,
                 @loan, @cust, @veh, @model, @chassis, @engine,
                 @agent, @pyn, @pym, @load,
                 @acn, @aca,
                 @cbn, @cbm, @exec,
                 @colup, @rmk,
                 @action, @holdu, @holdd, @subby)", conn) { CommandTimeout = 15 };

        void P(string n, object? v) => cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        P("@rid",   req.RecordId is > 0 ? req.RecordId : (object?)null);
        P("@fid",   financeId);
        P("@fname", financeName);
        P("@branch", string.IsNullOrWhiteSpace(branchName) ? null : branchName);
        P("@loan",  req.LoanNo);
        P("@cust",  req.CustomerName);
        P("@veh",   req.VehicleNo);
        P("@model", req.Model);
        P("@chassis", req.ChassisNo);
        P("@engine", req.EngineNo);
        P("@agent", req.AgentName);
        P("@pyn",   req.ParkingYardName);
        P("@pym",   req.ParkingYardMobile);
        P("@load",  req.LoadDetails);
        P("@acn",   req.AddlChargesNotes);
        P("@aca",   req.AddlChargesAmount);
        P("@cbn",   req.ConfirmationByName);
        P("@cbm",   req.ConfirmationByMobile);
        P("@exec",  req.ExecutiveName);
        P("@colup", req.CollectionUpdate);
        P("@rmk",   req.Remark);
        P("@action", action);
        P("@holdu", holdUntil);
        P("@holdd", req.HoldDays);
        P("@subby", req.SubmittedByName);
        await cmd.ExecuteNonQueryAsync();
        return cmd.LastInsertedId;
    }
}
