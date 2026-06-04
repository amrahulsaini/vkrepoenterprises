using Microsoft.AspNetCore.Mvc;
using VKmobileapi;
using VKmobileapi.Data;
using VKmobileapi.Models;

namespace VKmobileapi.Controllers;

[ApiController]
[Route("api/mobile")]
public class MobileController : ControllerBase
{
    private readonly MobileRepository _repo = new();

    // Builds a full URL for a relative uploads path stored in the DB
    // (e.g. "pfp/user_1.jpg" → "https://api.crmrecoverysoftware.com/uploads/pfp/user_1.jpg").
    //
    // The PUBLIC_BASE_URL env var lets ops switch hosts without a rebuild
    // (e.g. characterverse.tech for the old install). We do NOT use
    // Request.Scheme/Request.Host: this controller is invoked by the
    // VKApiServer reverse-proxy at http://localhost:5001 — those values
    // would yield "http://localhost:5001/uploads/..." URLs that the
    // mobile client cannot resolve.
    private static readonly string _publicBase =
        (Environment.GetEnvironmentVariable("PUBLIC_BASE_URL")
         ?? "https://api.crmrecoverysoftware.com").TrimEnd('/');

    private string? AbsUrl(string? relativePath) =>
        string.IsNullOrEmpty(relativePath)
            ? null
            : $"{_publicBase}/uploads/{relativePath.TrimStart('/')}";

    // Keeps only digits — tolerant compare of mobile numbers regardless of
    // spaces, +91 prefixes or punctuation.
    private static string Digits(string? s) =>
        string.IsNullOrEmpty(s) ? "" : new string(s.Where(char.IsDigit).ToArray());

    // Strips digit-only mobile of a leading "0" (Indian STD prefix) or "91"
    // country code so "09850637363", "+919850637363" and "9850637363" all
    // compare equal. Indian mobiles are 10 digits — anything else is left alone.
    private static string NormalizeMobile(string? s)
    {
        var d = Digits(s);
        if (d.Length == 12 && d.StartsWith("91")) d = d.Substring(2);
        else if (d.Length == 11 && d.StartsWith("0")) d = d.Substring(1);
        return d;
    }

    // GET /api/mobile/agencies — approved agencies for the register / login picker
    [HttpGet("agencies")]
    public async Task<IActionResult> GetAgencies()
    {
        try
        {
            return Ok(await _repo.GetApprovedAgenciesAsync());
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Failed to load agencies: {ex.Message}"));
        }
    }

    // GET /api/mobile/agency — full agency profile (name, address, all
    // mobile numbers). Slug comes from the X-Tenant-Token, so the response
    // is always for the agency this app is signed into. Used by the in-app
    // "Agency" panel on the vehicle detail screen.
    [HttpGet("agency")]
    public async Task<IActionResult> GetAgencyInfo()
    {
        var slug = TenantContext.Key;
        if (string.IsNullOrEmpty(slug) || slug == "default")
            return Unauthorized(new ApiError(false, "No tenant context"));
        try
        {
            var info = await _repo.GetAgencyInfoAsync(slug);
            if (info == null) return NotFound(new ApiError(false, "Agency not found"));
            return Ok(info);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Failed to load agency: {ex.Message}"));
        }
    }

    // POST /api/mobile/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Mobile) || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new ApiError(false, "Mobile and name are required."));
            if (string.IsNullOrWhiteSpace(req.DeviceId))
                return BadRequest(new ApiError(false, "Device ID is required."));
            if (string.IsNullOrWhiteSpace(req.Slug))
                return BadRequest(new ApiError(false, "Please select your agency."));

            // Verify the agency exists & is approved, and that the entered
            // agency mobile number matches the agency's registered one. This
            // gate stops anyone registering under an agency they don't belong to.
            var agency = await _repo.GetAgencyBySlugAsync(req.Slug.Trim());
            if (!agency.Found || agency.Status != "approved")
                return BadRequest(new ApiError(false, "That agency is not available. Please pick another."));

            // Mobile must be SMS-OTP verified first (the app does /otp/send +
            // /otp/verify before calling register). Soft-disable via OTP_REQUIRED=0.
            if (Msg91Otp.Required && !Msg91Otp.IsRecentlyVerified(req.Mobile))
                return StatusCode(403, new ApiError(false, "otp_required"));
            // Agency-mobile verification removed per UX request — the
            // white-label per-flavor build already pins the slug at compile
            // time, so the user can't pick the wrong agency. If a value is
            // sent and it's non-blank, we still match it as a soft check.
            var enteredAgencyMobile = NormalizeMobile(req.AgencyMobile);
            if (enteredAgencyMobile.Length > 0
                && enteredAgencyMobile != NormalizeMobile(agency.Mobile1))
                return BadRequest(new ApiError(false, "The agency's mobile number does not match. Please confirm it with your agency."));

            // ── Cross-agency uniqueness gate ─────────────────────────────
            // crm_master.app_user_registry keeps a (mobile, device) → slug
            // record for every approved agency user. A given mobile or device
            // may only belong to ONE agency. We normalize the user's mobile
            // (strip leading 0 / +91) so the same person can't slip past the
            // UNIQUE(mobile) check by varying the format.
            var clean = new {
                Mobile = NormalizeMobile(req.Mobile),
                Device = req.DeviceId.Trim(),
                Slug   = req.Slug.Trim(),
            };
            if (clean.Mobile.Length < 10)
                return BadRequest(new ApiError(false, "Please enter a valid 10-digit mobile number."));
            var existingSlug = await _repo.FindExistingAgencyForMobileOrDevice(
                clean.Mobile, clean.Device, clean.Slug);
            if (existingSlug != null)
            {
                // Auto-heal stale registry rows. If an admin removed the user
                // directly from the previous agency's app_users (without going
                // through the desktop DELETE endpoint), the registry row is
                // orphaned and would block every future re-registration. Open
                // the previous agency's DB; if no app_users row matches, drop
                // the orphan and let registration proceed.
                var stillLive = await _repo.IsMobileOrDeviceLiveInAgencyAsync(
                    clean.Mobile, clean.Device, existingSlug);
                if (stillLive)
                    return Conflict(new ApiError(false,
                        "This mobile number or device is already registered with another agency. " +
                        "A user can only belong to one agency. Please contact your current agency to be removed first."));

                await _repo.PurgeRegistryForMobileOrDeviceAsync(
                    clean.Mobile, clean.Device, existingSlug);
            }

            // All checks passed → register into that agency's own database.
            TenantContext.UseAgency(clean.Slug);

            var (success, reason, userId) = await _repo.RegisterAsync(
                clean.Mobile, req.Name.Trim(),
                req.Address?.Trim(), req.Pincode?.Trim(),
                req.PfpBase64, clean.Device,
                req.AadhaarFront, req.AadhaarBack, req.PanFront,
                req.AccountNumber?.Trim(), req.IfscCode?.Trim(),
                req.SelfieWithAadhaar,
                req.AadhaarNumber, req.AadhaarName, req.AadhaarDob,
                req.AadhaarGender, req.AadhaarAddress, req.AadhaarVerified,
                req.RegLat, req.RegLng, req.RegLocation,
                req.AadhaarPhoto);

            if (!success && reason == "mobile_exists")
                return Conflict(new ApiError(false, "This mobile number is already registered with this agency."));

            // Record in the cross-agency registry. If a concurrent registration
            // sneaks in first, this throws on the UNIQUE(mobile) and we report
            // the conflict — the tenant row is harmless (login will fail until
            // an admin approves, and the registry row prevents this device from
            // registering elsewhere too).
            try
            {
                await _repo.RegisterInMasterAsync(clean.Mobile, clean.Device, clean.Slug);
            }
            catch (MySqlConnector.MySqlException)
            {
                return Conflict(new ApiError(false,
                    "This mobile number or device was just registered with another agency. Please try again."));
            }

            Msg91Otp.ClearVerified(req.Mobile);  // consume the OTP verification
            return Ok(new { success = true, message = "Registered! Waiting for admin approval.", userId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Registration failed: {ex.Message}"));
        }
    }

    // POST /api/mobile/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Mobile) || string.IsNullOrWhiteSpace(req.DeviceId))
                return BadRequest(new ApiError(false, "Mobile and device ID are required."));
            if (string.IsNullOrWhiteSpace(req.Slug))
                return BadRequest(new ApiError(false, "Please select your agency."));

            // Validate the agency, then route this login to its database.
            var agency = await _repo.GetAgencyBySlugAsync(req.Slug.Trim());
            if (!agency.Found || agency.Status != "approved")
                return BadRequest(new ApiError(false, "That agency is not available."));

            // NOTE: OTP at login is enforced by the LOGIN SCREEN UI (send +
            // verify before it ever calls this endpoint). We deliberately do NOT
            // gate the login endpoint on OTP: the status screens (App Stopped /
            // Awaiting Approval / KYC) all "Check again" by calling login WITHOUT
            // a fresh OTP, and gating here returned otp_required which the client
            // mis-rendered as "Awaiting Approval" for already-approved agents.
            // (Register still enforces OTP server-side — it's the account-creation
            // step and is only ever reached from the register screen's OTP flow.)
            TenantContext.UseAgency(req.Slug.Trim());

            var result = await _repo.LoginAsync(NormalizeMobile(req.Mobile), req.DeviceId.Trim());
            if (result.Reason == "ok")
                result = result with
                {
                    PfpUrl      = AbsUrl(result.PfpUrl),
                    TenantToken = MobileToken.Issue(req.Slug.Trim())
                };

            return result.Reason switch
            {
                "ok"               => Ok(result),
                "pending_approval" => StatusCode(403, result),
                "device_mismatch"  => StatusCode(403, result),
                "not_found"        => NotFound(result),
                _                  => BadRequest(result)
            };
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Login failed: {ex.Message}"));
        }
    }

    // GET /api/mobile/search/rc/{last4}
    [HttpGet("search/rc/{last4}")]
    public async Task<IActionResult> SearchRc(
        string last4,
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromQuery] bool lite = false)
    {
        try
        {
            if (last4.Length != 4) return BadRequest(new ApiError(false, "last4 must be exactly 4 characters."));

            var status = await _repo.GetUserStatusAsync(userId);
            if (status.IsBlacklisted) return StatusCode(403, new ApiError(false, "blacklisted"));
            if (!status.IsActive)     return StatusCode(403, new ApiError(false, "inactive"));
            if (status.IsStopped)     return StatusCode(403, new ApiError(false, "app_stopped"));

            if (!await _repo.HasActiveSubscriptionAsync(userId))
                return StatusCode(402, new ApiError(false, "subscription_expired"));

            // lite=true → skinny list (instant); full detail fetched per-record via record/{id}.
            var results = lite ? await _repo.SearchByRcLiteAsync(last4, userId)
                               : await _repo.SearchByRcAsync(last4, userId);
            return Ok(new SearchResponse(true, "rc", last4.ToUpper(), results.Count, results));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Search failed: {ex.Message}"));
        }
    }

    // GET /api/mobile/search/chassis/{last5}
    [HttpGet("search/chassis/{last5}")]
    public async Task<IActionResult> SearchChassis(
        string last5,
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromQuery] bool lite = false)
    {
        try
        {
            if (last5.Length != 5) return BadRequest(new ApiError(false, "last5 must be exactly 5 characters."));

            var status = await _repo.GetUserStatusAsync(userId);
            if (status.IsBlacklisted) return StatusCode(403, new ApiError(false, "blacklisted"));
            if (!status.IsActive)     return StatusCode(403, new ApiError(false, "inactive"));
            if (status.IsStopped)     return StatusCode(403, new ApiError(false, "app_stopped"));

            if (!await _repo.HasActiveSubscriptionAsync(userId))
                return StatusCode(402, new ApiError(false, "subscription_expired"));

            var results = lite ? await _repo.SearchByChassisLiteAsync(last5, userId)
                               : await _repo.SearchByChassisAsync(last5, userId);
            return Ok(new SearchResponse(true, "chassis", last5.ToUpper(), results.Count, results));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Search failed: {ex.Message}"));
        }
    }

    // GET /api/mobile/record/{id} — full detail for one record, fetched only
    // when a (skinny) search result is opened.
    [HttpGet("record/{id:long}")]
    public async Task<IActionResult> GetRecord(
        long id,
        [FromHeader(Name = "X-User-Id")] long userId)
    {
        try
        {
            var status = await _repo.GetUserStatusAsync(userId);
            if (status.IsBlacklisted) return StatusCode(403, new ApiError(false, "blacklisted"));
            if (!status.IsActive)     return StatusCode(403, new ApiError(false, "inactive"));
            if (status.IsStopped)     return StatusCode(403, new ApiError(false, "app_stopped"));
            if (!await _repo.HasActiveSubscriptionAsync(userId))
                return StatusCode(402, new ApiError(false, "subscription_expired"));

            var rec = await _repo.GetRecordByIdAsync(id);
            if (rec is null) return NotFound(new ApiError(false, "Record not found."));
            return Ok(rec);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Fetch failed: {ex.Message}"));
        }
    }

    // POST /api/mobile/admin/verify-subs-pass
    [HttpPost("admin/verify-subs-pass")]
    public async Task<IActionResult> VerifySubsPass(
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromBody] VerifySubsPassRequest req)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            var valid = await _repo.VerifySubsPasswordAsync(req.Password);
            if (!valid) return StatusCode(403, new ApiError(false, "Incorrect password."));
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Verification failed: {ex.Message}"));
        }
    }

    // GET /api/mobile/profile/{userId}/subscriptions
    [HttpGet("profile/{userId:long}/subscriptions")]
    public async Task<IActionResult> GetUserSubscriptions(
        long userId,
        [FromHeader(Name = "X-User-Id")] long adminId)
    {
        try
        {
            if (!await _repo.IsAdminAsync(adminId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            var subs = await _repo.GetUserSubscriptionsAsync(userId);
            return Ok(subs);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Failed: {ex.Message}"));
        }
    }

    // GET /api/mobile/admin/users
    [HttpGet("admin/users")]
    public async Task<IActionResult> GetAdminUsers(
        [FromHeader(Name = "X-User-Id")] long userId)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            var users = await _repo.GetAdminUsersAsync();
            return Ok(users);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Failed: {ex.Message}"));
        }
    }

    // POST /api/mobile/admin/users/{targetUserId}/subscriptions
    [HttpPost("admin/users/{targetUserId:long}/subscriptions")]
    public async Task<IActionResult> AdminAddSubscription(
        long targetUserId,
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromBody] AdminAddSubRequest req)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            await _repo.AddSubscriptionAsync(targetUserId, req.StartDate, req.EndDate, req.Amount, req.Notes);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Failed: {ex.Message}"));
        }
    }

    // DELETE /api/mobile/admin/subscriptions/{subId}
    [HttpDelete("admin/subscriptions/{subId:long}")]
    public async Task<IActionResult> AdminDeleteSubscription(
        long subId,
        [FromHeader(Name = "X-User-Id")] long userId)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            await _repo.DeleteSubscriptionAsync(subId);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Failed: {ex.Message}"));
        }
    }

    // ── Control Panel ──────────────────────────────────────────────────────
    // POST /api/mobile/admin/verify-admin-pass — checks the caller's own admin_pass
    [HttpPost("admin/verify-admin-pass")]
    public async Task<IActionResult> VerifyAdminPass(
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromBody] VerifyAdminPassRequest req)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            if (!await _repo.VerifyAdminPasswordAsync(userId, req.Password))
                return StatusCode(403, new ApiError(false, "Incorrect Control Panel password."));
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Verification failed: {ex.Message}"));
        }
    }

    // PATCH /api/mobile/admin/users/{targetUserId}/active
    [HttpPatch("admin/users/{targetUserId:long}/active")]
    public async Task<IActionResult> AdminSetActive(
        long targetUserId,
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromBody] SetUserFlagRequest req)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            await _repo.SetUserActiveAsync(targetUserId, req.Value);
            return Ok(new { success = true });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, $"Failed: {ex.Message}")); }
    }

    // PATCH /api/mobile/admin/users/{targetUserId}/stopped
    [HttpPatch("admin/users/{targetUserId:long}/stopped")]
    public async Task<IActionResult> AdminSetStopped(
        long targetUserId,
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromBody] SetUserFlagRequest req)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            await _repo.SetUserStoppedAsync(targetUserId, req.Value);
            return Ok(new { success = true });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, $"Failed: {ex.Message}")); }
    }

    // PATCH /api/mobile/admin/users/{targetUserId}/blacklisted
    [HttpPatch("admin/users/{targetUserId:long}/blacklisted")]
    public async Task<IActionResult> AdminSetBlacklisted(
        long targetUserId,
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromBody] SetUserFlagRequest req)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            await _repo.SetUserBlacklistedAsync(targetUserId, req.Value);
            return Ok(new { success = true });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, $"Failed: {ex.Message}")); }
    }

    // PATCH /api/mobile/admin/users/{targetUserId}/admin — promote/demote a
    // user to/from admin from inside the mobile Control Panel.
    [HttpPatch("admin/users/{targetUserId:long}/admin")]
    public async Task<IActionResult> AdminSetAdmin(
        long targetUserId,
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromBody] SetUserFlagRequest req)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            await _repo.SetUserAdminAsync(targetUserId, req.Value);
            return Ok(new { success = true });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, $"Failed: {ex.Message}")); }
    }

    // PATCH /api/mobile/admin/users/{targetUserId}/kyc-status — KYC review
    // outcome from the mobile Control Panel. Mirrors the desktop "Mark Verified"
    // / "Reject" actions. status: success | failed | pending. Rejecting also
    // deactivates the account (handled in the repo).
    [HttpPatch("admin/users/{targetUserId:long}/kyc-status")]
    public async Task<IActionResult> AdminSetKycStatus(
        long targetUserId,
        [FromHeader(Name = "X-User-Id")] long userId,
        [FromBody] SetKycStatusRequest req)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            var status = (req.Status ?? "").Trim().ToLowerInvariant();
            if (status != "success" && status != "failed" && status != "pending")
                return BadRequest(new ApiError(false, "status must be success, failed or pending."));
            await _repo.SetUserKycStatusAsync(targetUserId, status,
                string.IsNullOrWhiteSpace(req.Note) ? null : req.Note);
            return Ok(new { success = true });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, $"Failed: {ex.Message}")); }
    }

    // GET /api/mobile/profile/{userId}
    [HttpGet("profile/{userId:long}")]
    public async Task<IActionResult> GetProfile(long userId)
    {
        try
        {
            var profile = await _repo.GetProfileAsync(userId);
            if (profile == null) return NotFound(new ApiError(false, "User not found."));
            // Convert every stored path (relative — e.g. "pfp/user_10.jpg")
            // into a full https URL the mobile app can hand straight to
            // AsyncImage. Previously only PfpUrl was AbsUrl()'d, so the KYC
            // image fields shipped as raw paths and the Android client
            // silently tried to Base64.decode them as if they were image
            // bytes → broken thumbnails on the "My Account" screen.
            return Ok(profile with {
                PfpUrl = AbsUrl(profile.PfpUrl),
                Kyc    = profile.Kyc with {
                    AadhaarFront = AbsUrl(profile.Kyc.AadhaarFront),
                    AadhaarBack  = AbsUrl(profile.Kyc.AadhaarBack),
                    PanFront     = AbsUrl(profile.Kyc.PanFront),
                    Selfie       = AbsUrl(profile.Kyc.Selfie),
                    AadhaarPhoto = AbsUrl(profile.Kyc.AadhaarPhoto),
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Profile fetch failed: {ex.Message}"));
        }
    }

    // PUT /api/mobile/profile/{userId}/pfp
    [HttpPut("profile/{userId:long}/pfp")]
    public async Task<IActionResult> UpdatePfp(long userId, [FromBody] UpdatePfpRequest req)
    {
        try
        {
            var relativePath = await _repo.UpdatePfpAsync(userId, req.PfpBase64);
            return Ok(new { success = true, pfpUrl = AbsUrl(relativePath) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Update failed: {ex.Message}"));
        }
    }

    // POST /api/mobile/cache/invalidate  — call after desktop uploads records
    [HttpPost("cache/invalidate")]
    public IActionResult InvalidateCache()
    {
        MobileRepository.InvalidateSearchCache();
        return Ok(new { success = true, message = "Search cache cleared." });
    }

    // POST /api/mobile/cache/invalidate-sub/{userId}  — call after admin grants/revokes subscription
    [HttpPost("cache/invalidate-sub/{userId:long}")]
    public IActionResult InvalidateSubCache(long userId)
    {
        MobileRepository.InvalidateSubCache(userId);
        return Ok(new { success = true, message = "Subscription cache cleared." });
    }

    // GET /api/mobile/pfp/{userId}
    [HttpGet("pfp/{userId:long}")]
    public async Task<IActionResult> GetPfp(long userId)
    {
        var pfp = await _repo.GetPfpAsync(userId);
        if (pfp == null) return NotFound();
        return Ok(new { pfpBase64 = pfp });
    }

    // GET /api/mobile/sync/branches
    [HttpGet("sync/branches")]
    public async Task<IActionResult> GetSyncBranches()
    {
        try
        {
            var branches = await _repo.GetSyncBranchesAsync();
            var total    = branches.Sum(b => b.TotalRecords);
            return Ok(new SyncBranchResponse(true, branches.Count, total, branches));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Sync branches failed: {ex.Message}"));
        }
    }

    // GET /api/mobile/stats
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var (vr, rc, ch) = await _repo.GetStatsAsync();
            return Ok(new StatsResponse(true, vr, rc, ch));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Stats failed: {ex.Message}"));
        }
    }

    // GET /api/mobile/me/status  — lightweight foreground poll; no last_seen update
    [HttpGet("me/status")]
    public async Task<IActionResult> GetMyStatus([FromHeader(Name = "X-User-Id")] long userId)
    {
        try
        {
            var status = await _repo.GetUserStatusAsync(userId);
            return Ok(new { isStopped = status.IsStopped, isBlacklisted = status.IsBlacklisted });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Status check failed: {ex.Message}"));
        }
    }

    // POST /api/mobile/heartbeat  — updates last_seen + GPS; returns stopped/blacklisted flags
    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req)
    {
        try
        {
            await _repo.HeartbeatAsync(req.UserId, req.Lat, req.Lng);
            var status = await _repo.GetUserStatusAsync(req.UserId);
            return Ok(new {
                success      = true,
                isStopped    = status.IsStopped,
                isBlacklisted = status.IsBlacklisted
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Heartbeat failed: {ex.Message}"));
        }
    }

    // GET /api/mobile/live-users  — admin only; users active in last 15 min
    [HttpGet("live-users")]
    public async Task<IActionResult> GetLiveUsers(
        [FromHeader(Name = "X-User-Id")] long userId)
    {
        try
        {
            if (!await _repo.IsAdminAsync(userId))
                return StatusCode(403, new ApiError(false, "Admin access required."));
            var users = await _repo.GetLiveUsersAsync();
            return Ok(new LiveUsersResponse(true, users));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Live users failed: {ex.Message}"));
        }
    }

    // POST /api/mobile/search-log  — fire-and-forget from mobile when a vehicle detail is viewed
    [HttpPost("search-log")]
    public async Task<IActionResult> LogSearch([FromBody] SearchLogRequest req)
    {
        try
        {
            // Parse the device's ISO timestamp AS UTC regardless of this server's
            // local timezone, so search_logs.device_time is always stored in UTC.
            // The read side (CONVERT_TZ +00:00 -> +05:30) then shows it in IST.
            if (!DateTime.TryParse(req.DeviceTimeIso,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var deviceTime))
                deviceTime = DateTime.UtcNow;

            var address = req.Address;
            if (string.IsNullOrWhiteSpace(address) && req.Lat.HasValue && req.Lng.HasValue)
                address = await NominatimReverseAsync(req.Lat.Value, req.Lng.Value);

            await _repo.LogSearchAsync(req.UserId, req.VehicleNo ?? "", req.ChassisNo ?? "",
                req.Model ?? "", req.Lat, req.Lng, address, deviceTime);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Log failed: {ex.Message}"));
        }
    }

    private static readonly HttpClient _geo = new()
    {
        Timeout = TimeSpan.FromSeconds(6),
        DefaultRequestHeaders = { { "User-Agent", "VKRepoCar/1.0" } }
    };

    private static async Task<string?> NominatimReverseAsync(double lat, double lng)
    {
        try
        {
            var json = await _geo.GetStringAsync(
                $"https://nominatim.openstreetmap.org/reverse?lat={lat:F6}&lon={lng:F6}&format=json&zoom=16&accept-language=en");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("display_name", out var dp)
                ? dp.GetString() : null;
        }
        catch { return null; }
    }

    // GET /api/mobile/sync/records/{branchId}?page=0&size=500
    [HttpGet("sync/records/{branchId}")]
    public async Task<IActionResult> GetSyncRecords(
        int branchId, [FromQuery] int page = 0, [FromQuery] int size = 500)
    {
        try
        {
            if (size > 5000) size = 5000;
            var records = await _repo.GetSyncRecordsAsync(branchId, page, size);
            return Ok(new SyncRecordsResponse(true, branchId, page, size, records.Count == size, records));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Sync records failed: {ex.Message}"));
        }
    }

    // ═══════════════════════ KYC verification (Sandbox) ═══════════════════════
    // Agents verify Aadhaar (OTP), PAN and bank during registration; each
    // verified result is stored on the agent's app_users row so the desktop
    // (WPF) can display it read-only. Sandbox credentials live only in the
    // service env (never in the app).
    private static string JStr(System.Text.Json.JsonElement d, string k) =>
        d.TryGetProperty(k, out var v)
            ? (v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
            : "";

    // POST /api/mobile/check-mobile — is this number already registered with the
    // agency? The app calls this the moment the number is entered, BEFORE sending
    // an OTP, so an already-registered agent is told to log in instead.
    [HttpPost("check-mobile")]
    public async Task<IActionResult> CheckMobile([FromBody] CheckMobileReq req)
    {
        if (string.IsNullOrWhiteSpace(req?.Slug))
            return BadRequest(new ApiError(false, "Please select your agency."));
        var mobile = NormalizeMobile(req.Mobile);
        if (mobile.Length < 10)
            return BadRequest(new ApiError(false, "Enter a valid 10-digit mobile number."));
        var agency = await _repo.GetAgencyBySlugAsync(req.Slug.Trim());
        if (!agency.Found || agency.Status != "approved")
            return BadRequest(new ApiError(false, "That agency is not available."));
        TenantContext.UseAgency(req.Slug.Trim());
        var registered = await _repo.IsMobileRegisteredAsync(mobile);
        return Ok(new { registered });
    }

    // ── Mobile SMS OTP (MSG91) — verify the phone number at register / login ──
    // No tenant token: these run before login. Stateless OTP store is in-process.
    [HttpPost("otp/send")]
    public async Task<IActionResult> OtpSend([FromBody] OtpSendReq req)
    {
        if (!Msg91Otp.Configured) return StatusCode(503, new ApiError(false, "OTP service is not configured on the server."));
        var (ok, msg) = await Msg91Otp.SendAsync(req?.Mobile);
        return ok ? Ok(new { ok = true, message = msg })
                  : BadRequest(new { ok = false, message = msg });
    }

    [HttpPost("otp/verify")]
    public IActionResult OtpVerify([FromBody] OtpVerifyReq req)
    {
        var (ok, msg) = Msg91Otp.Verify(req?.Mobile, req?.Otp);
        return ok ? Ok(new { ok = true, verified = true, message = msg })
                  : BadRequest(new { ok = false, verified = false, message = msg });
    }

    [HttpPost("kyc/aadhaar/otp")]
    public async Task<IActionResult> KycAadhaarOtp([FromBody] KycAadhaarOtpReq req)
    {
        if (!SandboxKyc.Configured) return StatusCode(503, new ApiError(false, "KYC is not configured on the server."));
        var aadhaar = Digits(req?.AadhaarNumber);
        if (aadhaar.Length != 12) return BadRequest(new ApiError(false, "Enter a valid 12-digit Aadhaar number."));
        try
        {
            var r = await SandboxKyc.AadhaarOtpAsync(aadhaar);
            if (r.TryGetProperty("data", out var d) && d.TryGetProperty("reference_id", out var refId))
                return Ok(new { ok = true, referenceId = refId.ToString() });
            return BadRequest(new { ok = false, message = SandboxKyc.Message(r) });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, ex.Message)); }
    }

    // Verifies the Aadhaar OKYC OTP. X-User-Id is OPTIONAL: during NEW-agent
    // registration there is no user row yet, so the header is absent — we then
    // just return the verified demographics and the register call persists them.
    // For an existing user (re-verify from inside the app) the header is sent and
    // we store the result directly onto their row.
    [HttpPost("kyc/aadhaar/verify")]
    public async Task<IActionResult> KycAadhaarVerify(
        [FromHeader(Name = "X-User-Id")] long? userId, [FromBody] KycAadhaarVerifyReq req)
    {
        if (!SandboxKyc.Configured) return StatusCode(503, new ApiError(false, "KYC is not configured on the server."));
        if (req == null || string.IsNullOrWhiteSpace(req.ReferenceId) || (req.Otp ?? "").Length < 4)
            return BadRequest(new ApiError(false, "Reference id and the 6-digit OTP are required."));
        try
        {
            // Sandbox OKYC verify requires reference_id as a STRING (sending it as
            // a JSON number is rejected with "Invalid request body"). otp is a
            // string too. Pass the reference id through verbatim.
            var r = await SandboxKyc.AadhaarVerifyAsync(req.ReferenceId!, req.Otp!);
            if (!r.TryGetProperty("data", out var d))
                return BadRequest(new { ok = false, message = SandboxKyc.Message(r) });
            // Sandbox returns HTTP 200 with data even on a WRONG OTP — the body is
            // then just { "message": "Invalid OTP..." } with no identity fields.
            // Treat the absence of name AND date_of_birth as a failure so we don't
            // report "verified" for a bad OTP.
            string vName = JStr(d, "name");
            string vDob  = JStr(d, "date_of_birth");
            if (vName.Length == 0 && vDob.Length == 0)
            {
                var dm = JStr(d, "message");
                return BadRequest(new { ok = false,
                    message = dm.Length > 0 ? dm : "OTP verification failed. Please check the OTP and try again." });
            }
            string addr = JStr(d, "full_address");
            if (addr.Length == 0 && d.TryGetProperty("address", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Object)
                addr = a.ToString();
            // Only persist if this is an existing user re-verifying. During
            // registration (no user yet) the data rides along in the register call.
            if (userId is long uid && uid > 0)
            {
                var digits = Digits(req.AadhaarNumber);
                var last4 = digits.Length >= 4 ? digits[^4..] : digits;
                await _repo.UpdateKycFieldsAsync(uid, new()
                {
                    ["kyc_aadhaar_last4"]    = last4.Length > 0 ? last4 : null,
                    ["kyc_aadhaar_name"]     = JStr(d, "name"),
                    ["kyc_aadhaar_dob"]      = JStr(d, "date_of_birth"),
                    ["kyc_aadhaar_gender"]   = JStr(d, "gender"),
                    ["kyc_aadhaar_address"]  = addr,
                    ["kyc_aadhaar_verified"] = 1,
                    ["kyc_verified_at"]      = DateTime.UtcNow
                });
            }
            return Ok(new { ok = true, verified = true, name = JStr(d, "name"), dob = JStr(d, "date_of_birth"),
                            gender = JStr(d, "gender"), address = addr, photo = JStr(d, "photo") });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, ex.Message)); }
    }

    [HttpPost("kyc/pan")]
    public async Task<IActionResult> KycPan(
        [FromHeader(Name = "X-User-Id")] long userId, [FromBody] KycPanReq req)
    {
        if (!SandboxKyc.Configured) return StatusCode(503, new ApiError(false, "KYC is not configured on the server."));
        var pan = (req?.Pan ?? "").Trim().ToUpper();
        if (pan.Length != 10) return BadRequest(new ApiError(false, "Enter a valid 10-character PAN."));
        try
        {
            var r = await SandboxKyc.PanVerifyAsync(pan, req!.Name ?? "", req.Dob ?? "");
            if (!r.TryGetProperty("data", out var d))
                return BadRequest(new { ok = false, message = SandboxKyc.Message(r) });
            bool ok = JStr(d, "status").Equals("valid", StringComparison.OrdinalIgnoreCase);
            await _repo.UpdateKycFieldsAsync(userId, new()
            {
                ["kyc_pan"]          = pan,
                ["kyc_pan_name"]     = req.Name ?? "",
                ["kyc_pan_verified"] = ok ? 1 : 0
            });
            bool B(string k) => d.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
            return Ok(new { ok = true, verified = ok, status = JStr(d, "status"),
                            nameMatch = B("name_as_per_pan_match"), dobMatch = B("date_of_birth_match"), category = JStr(d, "category") });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, ex.Message)); }
    }

    [HttpPost("kyc/bank")]
    public async Task<IActionResult> KycBank(
        [FromHeader(Name = "X-User-Id")] long userId, [FromBody] KycBankReq req)
    {
        if (!SandboxKyc.Configured) return StatusCode(503, new ApiError(false, "KYC is not configured on the server."));
        var ifsc = (req?.Ifsc ?? "").Trim().ToUpper();
        var acct = (req?.AccountNumber ?? "").Trim();
        if (ifsc.Length != 11 || acct.Length == 0)
            return BadRequest(new ApiError(false, "Enter a valid account number and 11-character IFSC."));
        try
        {
            var r = await SandboxKyc.BankVerifyAsync(ifsc, acct, req!.Name ?? "");
            if (!r.TryGetProperty("data", out var d))
                return BadRequest(new { ok = false, message = SandboxKyc.Message(r) });
            bool exists = d.TryGetProperty("account_exists", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.True;
            string holder = d.TryGetProperty("name_at_bank", out var n) ? (n.GetString() ?? "") : "";
            await _repo.UpdateKycFieldsAsync(userId, new()
            {
                ["account_number"]    = acct,
                ["ifsc_code"]         = ifsc,
                ["kyc_bank_holder"]   = holder,
                ["kyc_bank_verified"] = exists ? 1 : 0
            });
            return Ok(new { ok = true, verified = exists, nameAtBank = holder });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, ex.Message)); }
    }

    [HttpPost("kyc/location")]
    public async Task<IActionResult> KycLocation(
        [FromHeader(Name = "X-User-Id")] long userId, [FromBody] KycLocationReq req)
    {
        try
        {
            await _repo.UpdateKycFieldsAsync(userId, new()
            {
                ["kyc_reg_lat"]      = req?.Lat,
                ["kyc_reg_lng"]      = req?.Lng,
                ["kyc_reg_location"] = req?.Location
            });
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, ex.Message)); }
    }

    // ── KYC re-submission ─────────────────────────────────────────────────
    // For an agent whose KYC was rejected. They can't log in (blocked), so this
    // is tenant-bound-by-body like register: it identifies the user by mobile +
    // slug, re-saves the documents, and resets the status to 'pending'.
    [HttpPost("kyc/resubmit")]
    public async Task<IActionResult> KycResubmit([FromBody] KycResubmitReq req)
    {
        try
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Slug) || string.IsNullOrWhiteSpace(req.Mobile))
                return BadRequest(new ApiError(false, "Missing details."));
            var agency = await _repo.GetAgencyBySlugAsync(req.Slug.Trim());
            if (!agency.Found || agency.Status != "approved")
                return BadRequest(new ApiError(false, "That agency is not available."));
            TenantContext.UseAgency(req.Slug.Trim());
            var mobile = NormalizeMobile(req.Mobile);
            var ok = await _repo.ResubmitKycAsync(
                mobile, req.AadhaarFront, req.AadhaarBack, req.PanFront,
                req.SelfieWithAadhaar, req.AadhaarPhoto,
                req.AadhaarNumber, req.AadhaarName, req.AadhaarDob,
                req.AadhaarGender, req.AadhaarAddress, req.AadhaarVerified,
                req.RegLat, req.RegLng, req.RegLocation);
            if (!ok) return NotFound(new ApiError(false, "No matching account found. Please register first."));
            return Ok(new { success = true, message = "KYC re-submitted. Please wait for the agency to verify it." });
        }
        catch (Exception ex) { return StatusCode(500, new ApiError(false, $"Re-submit failed: {ex.Message}")); }
    }
}

public record UpdatePfpRequest(string? PfpBase64);

public record KycResubmitReq(
    string? Slug, string? Mobile,
    string? AadhaarFront, string? AadhaarBack, string? PanFront,
    string? SelfieWithAadhaar, string? AadhaarPhoto,
    string? AadhaarNumber, string? AadhaarName, string? AadhaarDob,
    string? AadhaarGender, string? AadhaarAddress, bool AadhaarVerified = false,
    double? RegLat = null, double? RegLng = null, string? RegLocation = null);

// ── Mobile SMS-OTP request bodies ────────────────────────────────────────────
public record CheckMobileReq(string? Mobile, string? Slug);
public record OtpSendReq(string? Mobile);
public record OtpVerifyReq(string? Mobile, string? Otp);

// ── KYC request bodies ──────────────────────────────────────────────────────
public record KycAadhaarOtpReq(string? AadhaarNumber);
public record KycAadhaarVerifyReq(string? ReferenceId, string? Otp, string? AadhaarNumber);
public record KycPanReq(string? Pan, string? Name, string? Dob);
public record KycBankReq(string? Ifsc, string? AccountNumber, string? Name);
public record KycLocationReq(double? Lat, double? Lng, string? Location);
