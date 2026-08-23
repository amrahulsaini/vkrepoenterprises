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

        string status, slug, mode, device, pair, nonce, agencyName;
        await using (var cmd = new MySqlCommand(
            "SELECT c.status, c.slug, c.mode, c.device_label, c.pair_code, c.nonce, c.expires_at, a.name " +
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

            if (gone || status == "expired")
                return StatusCode(410, new { success = false, code = "expired", message = "This request has expired. Start again on the desktop." });
            if (status == "approved" || status == "denied")
                return Conflict(new { success = false, code = "used", message = "This request has already been used." });
        }

        if (!string.Equals(slug, s.Slug, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { success = false, code = "wrong_agency", message = "This sign-in belongs to a different agency." });

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
            nonce
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

        string nonce, slug, status;
        await using var master = DbFactory.CreateMaster();
        await master.OpenAsync();
        await using (var cmd = new MySqlCommand(
            "SELECT nonce, slug, status, expires_at FROM auth_challenges WHERE id=@i LIMIT 1;", master))
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
        }

        if (!string.Equals(slug, s.Slug, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { success = false, message = "This sign-in belongs to a different agency." });

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

        return Ok(new { success = true, name });
    }
}
