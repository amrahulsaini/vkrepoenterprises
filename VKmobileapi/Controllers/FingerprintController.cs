using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using VKmobileapi.Data;

namespace VKmobileapi.Controllers;

[ApiController]
[Route("api/mobile/fingerprint")]
public class FingerprintController : ControllerBase
{
    private MobileToken.Session? Session()
    {
        var token = Request.Headers["X-Tenant-Token"].FirstOrDefault();
        var s = MobileToken.VerifyFull(token);
        if (s is null || !s.Value.HasIdentity || s.Value.UserId <= 0) return null;
        return s;
    }

    private static DateTime IstNow() => DateTime.UtcNow.AddMinutes(330);

    private string ClientIp()
    {
        var direct = HttpContext.Connection.RemoteIpAddress;
        if (direct == null || System.Net.IPAddress.IsLoopback(direct))
        {
            var fwd = Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(fwd))
            {
                var first = fwd.Split(',')[0].Trim();
                if (first.Length > 0) return Trim(first);
            }
            var real = Request.Headers["X-Real-IP"].ToString().Trim();
            if (real.Length > 0) return Trim(real);
        }
        return direct == null ? "" : Trim(direct.ToString());
    }

    private static string Trim(string ip)
    {
        ip = ip.Trim();
        if (ip.StartsWith("[")) { var e = ip.IndexOf(']'); if (e > 0) ip = ip.Substring(1, e - 1); }
        else if (ip.Split(':').Length == 2) ip = ip.Split(':')[0];
        if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase)) ip = ip.Substring(7);
        return ip.Length > 45 ? ip.Substring(0, 45) : ip;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        if (Session() is not { } s) return Unauthorized(new { success = false, message = "Sign in again." });

        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT key_id, device_label, enrolled_at FROM device_keys " +
            "WHERE user_id=@u AND revoked=0 ORDER BY id DESC LIMIT 1;", conn);
        cmd.Parameters.AddWithValue("@u", s.UserId);
        await using var rdr = await cmd.ExecuteReaderAsync();

        if (!await rdr.ReadAsync())
            return Ok(new { success = true, enrolled = false });

        return Ok(new
        {
            success = true,
            enrolled = true,
            keyId = rdr.GetString(0),
            device = rdr.GetString(1),
            enrolledAt = rdr.GetDateTime(2).ToString("yyyy-MM-dd HH:mm")
        });
    }

    public record EnrolBody(string? PublicKey, string? DeviceLabel);

    [HttpPost("enrol")]
    public async Task<IActionResult> Enrol([FromBody] EnrolBody body)
    {
        if (Session() is not { } s) return Unauthorized(new { success = false, message = "Sign in again." });

        var pub = (body?.PublicKey ?? "").Trim();
        if (!FingerprintAuth.LooksLikeEcPublicKey(pub))
            return BadRequest(new { success = false, message = "That key is not valid." });

        var keyId = FingerprintAuth.KeyId(pub);

        await using var conn = DbFactory.Create();
        await conn.OpenAsync();

        await using (var old = new MySqlCommand(
            "UPDATE device_keys SET revoked=1, revoked_at=@t WHERE user_id=@u AND revoked=0;", conn))
        {
            old.Parameters.AddWithValue("@t", IstNow());
            old.Parameters.AddWithValue("@u", s.UserId);
            await old.ExecuteNonQueryAsync();
        }

        await using (var ins = new MySqlCommand(
            "INSERT INTO device_keys (user_id, key_id, public_key, device_label, device_id, enrolled_at) " +
            "VALUES (@u, @k, @p, @l, @d, @t) " +
            "ON DUPLICATE KEY UPDATE revoked=0, revoked_at=NULL, public_key=VALUES(public_key), " +
            "device_label=VALUES(device_label), enrolled_at=VALUES(enrolled_at);", conn))
        {
            ins.Parameters.AddWithValue("@u", s.UserId);
            ins.Parameters.AddWithValue("@k", keyId);
            ins.Parameters.AddWithValue("@p", pub);
            ins.Parameters.AddWithValue("@l", (body?.DeviceLabel ?? "").Trim());
            ins.Parameters.AddWithValue("@d", (object?)s.DeviceId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@t", IstNow());
            await ins.ExecuteNonQueryAsync();
        }

        return Ok(new { success = true, keyId });
    }

    [HttpDelete]
    public async Task<IActionResult> Remove()
    {
        if (Session() is not { } s) return Unauthorized(new { success = false, message = "Sign in again." });

        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE device_keys SET revoked=1, revoked_at=@t WHERE user_id=@u AND revoked=0;", conn);
        cmd.Parameters.AddWithValue("@t", IstNow());
        cmd.Parameters.AddWithValue("@u", s.UserId);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { success = true, enrolled = false });
    }

    [HttpGet("challenge/{id}")]
    public async Task<IActionResult> Challenge(string id)
    {
        if (Session() is not { } s) return Unauthorized(new { success = false, message = "Sign in again." });

        await using var conn = DbFactory.CreateMaster();
        await conn.OpenAsync();

        string status, slug, mode, device, pair, nonce, agencyName, claimMobile;
        long claimUserId;
        await using (var cmd = new MySqlCommand(
            "SELECT c.status, c.slug, c.mode, c.device_label, c.pair_code, c.nonce, c.expires_at, a.name, " +
            "COALESCE(c.claim_user_id,0), COALESCE(c.claim_mobile,'') " +
            "FROM auth_challenges c JOIN agencies a ON a.id = c.agency_id WHERE c.id=@i LIMIT 1;", conn))
        {
            cmd.Parameters.AddWithValue("@i", id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync())
                return NotFound(new { success = false, code = "unknown", message = "This sign-in request is not valid." });

            status = rdr.GetString(0); slug = rdr.GetString(1); mode = rdr.GetString(2);
            device = rdr.GetString(3); pair = rdr.GetString(4); nonce = rdr.GetString(5);
            bool gone = rdr.GetDateTime(6) < DateTime.UtcNow;
            agencyName = rdr.GetString(7);
            claimUserId = rdr.GetInt64(8); claimMobile = rdr.GetString(9);

            if (gone || status == "expired")
                return StatusCode(410, new { success = false, code = "expired", message = "This request has expired. Start again on the desktop." });
            if (status == "approved" || status == "denied")
                return Conflict(new { success = false, code = "used", message = "This request has already been used." });
        }

        if (!string.Equals(slug, s.Slug, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { success = false, code = "wrong_agency", message = "This sign-in belongs to a different agency." });

        if (claimUserId > 0 && claimUserId != s.UserId)
            return StatusCode(403, new { success = false, code = "wrong_person",
                message = "This sign-in was started for a different person. Ask them to scan it, " +
                          "or start again on the desktop with your own number." });

        await using (var upd = new MySqlCommand(
            "UPDATE auth_challenges SET status='scanned' WHERE id=@i AND status='pending'", conn))
        {
            upd.Parameters.AddWithValue("@i", id);
            await upd.ExecuteNonQueryAsync();
        }

        return Ok(new
        {
            success = true,
            agencyName,
            mode,
            deviceLabel = device,
            pairCode = pair,
            nonce,
            forMobile = claimMobile
        });
    }

    public record ApproveBody(
        string? ChallengeId, string? Signature,
        double? Lat, double? Lng, double? Accuracy, bool? Mock, bool? GeoTried);

    private static double MetresBetween(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371000.0;
        double p1 = lat1 * Math.PI / 180.0, p2 = lat2 * Math.PI / 180.0;
        double dp = (lat2 - lat1) * Math.PI / 180.0;
        double dl = (lng2 - lng1) * Math.PI / 180.0;
        double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) +
                   Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static string Away(double metres) =>
        metres >= 1000 ? (metres / 1000.0).ToString("0.#") + " km" : ((int)metres) + " m";

    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] ApproveBody body)
    {
        if (Session() is not { } s) return Unauthorized(new { success = false, message = "Sign in again." });

        var cid = (body?.ChallengeId ?? "").Trim();
        var sig = (body?.Signature ?? "").Trim();
        if (cid.Length == 0 || sig.Length == 0)
            return BadRequest(new { success = false, message = "Missing request or signature." });

        string nonce, slug, status, policy;
        long claimUserId;
        double? fenceLat = null, fenceLng = null;
        int fenceRadius = 200;
        await using var master = DbFactory.CreateMaster();
        await master.OpenAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT c.nonce, c.slug, c.status, c.expires_at, COALESCE(c.claim_user_id,0), " +
            "COALESCE(a.qr_proximity,'warn'), a.geo_lat, a.geo_lng, COALESCE(a.geo_radius_m,200) " +
            "FROM auth_challenges c JOIN agencies a ON a.id = c.agency_id WHERE c.id=@i LIMIT 1;", master))
        {
            cmd.Parameters.AddWithValue("@i", cid);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync())
                return NotFound(new { success = false, message = "This sign-in request is not valid." });
            nonce = rdr.GetString(0); slug = rdr.GetString(1); status = rdr.GetString(2);
            if (rdr.GetDateTime(3) < DateTime.UtcNow || status == "expired")
                return StatusCode(410, new { success = false, code = "expired", message = "This request has expired." });
            if (status == "approved" || status == "denied")
                return Conflict(new { success = false, code = "used", message = "This request has already been used." });
            claimUserId = rdr.GetInt64(4); policy = rdr.GetString(5);
            if (!rdr.IsDBNull(6)) fenceLat = rdr.GetDouble(6);
            if (!rdr.IsDBNull(7)) fenceLng = rdr.GetDouble(7);
            fenceRadius = rdr.GetInt32(8);
        }

        if (!string.Equals(slug, s.Slug, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { success = false, message = "This sign-in belongs to a different agency." });

        if (claimUserId > 0 && claimUserId != s.UserId)
        {
            await Refuse(master, cid, "wrong_person");
            return StatusCode(403, new { success = false, code = "wrong_person",
                message = "This sign-in was started for a different person." });
        }

        bool fenced   = fenceLat.HasValue && fenceLng.HasValue && policy != "off";
        bool haveFix  = body?.Lat is double && body?.Lng is double &&
                        !(body.Lat == 0 && body.Lng == 0);
        bool mocked   = body?.Mock == true;

        bool clientChecks = body?.GeoTried == true || haveFix;

        int? distance = null;
        string verdict = "unknown";
        string refusal = "";

        if (fenced && !clientChecks)
        {
            verdict = "unknown";   // recorded, never refused
        }
        else if (fenced)
        {
            if (mocked)
            {
                verdict = "mismatch";
                refusal = "Your phone is reporting a fake location. Turn off any mock-location app.";
            }
            else if (!haveFix)
            {
                verdict = "unknown";
                refusal = "CRMRS could not read your location. Turn location on for CRMRS and try again.";
            }
            else
            {
                double m = MetresBetween(fenceLat!.Value, fenceLng!.Value, body!.Lat!.Value, body.Lng!.Value);
                double allow = fenceRadius + Math.Min(body.Accuracy ?? 0, 50);
                distance = (int)Math.Round(m);
                verdict = m <= allow ? "match" : "mismatch";
                if (verdict == "mismatch")
                    refusal = "You are " + Away(m) + " from the office. Sign in from there.";
            }
        }

        await using (var mark = new MySqlCommand(
            "UPDATE auth_challenges SET phone_ip=@p, proximity=@v, phone_lat=@la, phone_lng=@lo, " +
            "phone_acc=@ac, distance_m=@d, mock_gps=@mk WHERE id=@i;", master))
        {
            mark.Parameters.AddWithValue("@p", ClientIp());
            mark.Parameters.AddWithValue("@v", verdict);
            mark.Parameters.AddWithValue("@la", haveFix ? body!.Lat : (object)DBNull.Value);
            mark.Parameters.AddWithValue("@lo", haveFix ? body!.Lng : (object)DBNull.Value);
            mark.Parameters.AddWithValue("@ac", body?.Accuracy is double a2 ? (int)a2 : (object)DBNull.Value);
            mark.Parameters.AddWithValue("@d", distance ?? (object)DBNull.Value);
            mark.Parameters.AddWithValue("@mk", mocked ? 1 : 0);
            mark.Parameters.AddWithValue("@i", cid);
            await mark.ExecuteNonQueryAsync();
        }

        if (refusal.Length > 0 && policy == "block")
        {
            await Refuse(master, cid, mocked ? "mock_gps" : verdict == "unknown" ? "no_location" : "outside_area");
            return StatusCode(403, new { success = false, code = "outside_area", message = refusal });
        }

        string pub = "", keyId = "", name = "";
        await using (var tconn = DbFactory.Create())
        {
            await tconn.OpenAsync();
            await using (var cmd = new MySqlCommand(
                "SELECT k.public_key, k.key_id, COALESCE(u.name,'') FROM device_keys k " +
                "JOIN app_users u ON u.id = k.user_id " +
                "WHERE k.user_id=@u AND k.revoked=0 ORDER BY k.id DESC LIMIT 1;", tconn))
            {
                cmd.Parameters.AddWithValue("@u", s.UserId);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    pub = rdr.GetString(0); keyId = rdr.GetString(1); name = rdr.GetString(2);
                }
            }

            if (pub.Length == 0)
                return StatusCode(409, new { success = false, code = "not_enrolled", message = "No fingerprint is set up on this phone." });

            string message = cid + ":" + nonce;
            if (!FingerprintAuth.VerifySignature(pub, message, sig))
            {
                await using var bad = new MySqlCommand(
                    "UPDATE auth_challenges SET status='denied', fail_reason='bad_signature', resolved_at=@t WHERE id=@i;", master);
                bad.Parameters.AddWithValue("@t", DateTime.UtcNow);
                bad.Parameters.AddWithValue("@i", cid);
                await bad.ExecuteNonQueryAsync();
                return BadRequest(new { success = false, code = "bad_signature", message = "That fingerprint could not be verified." });
            }

            await using var touch = new MySqlCommand(
                "UPDATE device_keys SET last_used_at=@t WHERE key_id=@k;", tconn);
            touch.Parameters.AddWithValue("@t", IstNow());
            touch.Parameters.AddWithValue("@k", keyId);
            await touch.ExecuteNonQueryAsync();
        }

        await using (var ok = new MySqlCommand(
            "UPDATE auth_challenges SET status='approved', approved_user_id=@u, approved_name=@n, key_id=@k, resolved_at=@t " +
            "WHERE id=@i AND status IN ('pending','scanned');", master))
        {
            ok.Parameters.AddWithValue("@u", s.UserId);
            ok.Parameters.AddWithValue("@n", name);
            ok.Parameters.AddWithValue("@k", keyId);
            ok.Parameters.AddWithValue("@t", DateTime.UtcNow);
            ok.Parameters.AddWithValue("@i", cid);
            if (await ok.ExecuteNonQueryAsync() == 0)
                return Conflict(new { success = false, code = "used", message = "This request has already been used." });
        }

        return Ok(new { success = true, name, proximity = verdict, distanceM = distance });
    }

    private static async Task Refuse(MySqlConnection master, string cid, string reason)
    {
        await using var cmd = new MySqlCommand(
            "UPDATE auth_challenges SET status='denied', fail_reason=@r, resolved_at=@t " +
            "WHERE id=@i AND status IN ('pending','scanned');", master);
        cmd.Parameters.AddWithValue("@r", reason);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@i", cid);
        await cmd.ExecuteNonQueryAsync();
    }
}
