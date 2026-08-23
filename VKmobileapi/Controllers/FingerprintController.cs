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

    /// Behind the local OpenLiteSpeed proxy the connection is always loopback,
    /// so the forwarded header carries the real client. Safe to trust only
    /// because the proxy is on this machine — nothing remote reaches Kestrel.
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

        // Say no while they are still looking at the scanner, rather than after
        // they have put a finger on the sensor for nothing.
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

    public record ApproveBody(string? ChallengeId, string? Signature);

    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] ApproveBody body)
    {
        if (Session() is not { } s) return Unauthorized(new { success = false, message = "Sign in again." });

        var cid = (body?.ChallengeId ?? "").Trim();
        var sig = (body?.Signature ?? "").Trim();
        if (cid.Length == 0 || sig.Length == 0)
            return BadRequest(new { success = false, message = "Missing request or signature." });

        string nonce, slug, status, desktopIp, policy;
        long claimUserId;
        await using var master = DbFactory.CreateMaster();
        await master.OpenAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT c.nonce, c.slug, c.status, c.expires_at, COALESCE(c.claim_user_id,0), " +
            "COALESCE(c.desktop_ip,''), COALESCE(a.qr_proximity,'warn') " +
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
            claimUserId = rdr.GetInt64(4); desktopIp = rdr.GetString(5); policy = rdr.GetString(6);
        }

        if (!string.Equals(slug, s.Slug, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { success = false, message = "This sign-in belongs to a different agency." });

        // Whoever the desktop asked for is the only person who can answer.
        if (claimUserId > 0 && claimUserId != s.UserId)
        {
            await Refuse(master, cid, "wrong_person");
            return StatusCode(403, new { success = false, code = "wrong_person",
                message = "This sign-in was started for a different person." });
        }

        // Same office internet connection, in or out. This is the check that a
        // forwarded screenshot cannot satisfy from someone's home.
        string phoneIp = ClientIp();
        bool near = desktopIp.Length > 0 && phoneIp.Length > 0 &&
                    string.Equals(desktopIp, phoneIp, StringComparison.OrdinalIgnoreCase);
        string verdict = desktopIp.Length == 0 || phoneIp.Length == 0
            ? "unknown" : (near ? "match" : "mismatch");

        await using (var mark = new MySqlCommand(
            "UPDATE auth_challenges SET phone_ip=@p, proximity=@v WHERE id=@i;", master))
        {
            mark.Parameters.AddWithValue("@p", phoneIp);
            mark.Parameters.AddWithValue("@v", verdict);
            mark.Parameters.AddWithValue("@i", cid);
            await mark.ExecuteNonQueryAsync();
        }

        if (verdict == "mismatch" && policy == "block")
        {
            await Refuse(master, cid, "too_far");
            return StatusCode(403, new { success = false, code = "too_far",
                message = "You are not on the same internet connection as that computer. " +
                          "Connect to the office Wi-Fi and scan again." });
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

        return Ok(new { success = true, name, proximity = verdict });
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
