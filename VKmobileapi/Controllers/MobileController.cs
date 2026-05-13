using Microsoft.AspNetCore.Mvc;
using VKmobileapi.Data;
using VKmobileapi.Models;

namespace VKmobileapi.Controllers;

[ApiController]
[Route("api/mobile")]
public class MobileController : ControllerBase
{
    private readonly MobileRepository _repo = new();

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

            var (success, reason, userId) = await _repo.RegisterAsync(
                req.Mobile.Trim(), req.Name.Trim(),
                req.Address?.Trim(), req.Pincode?.Trim(),
                req.PfpBase64, req.DeviceId.Trim(),
                req.AadhaarFront, req.AadhaarBack, req.PanFront,
                req.AccountNumber?.Trim(), req.IfscCode?.Trim());

            if (!success && reason == "mobile_exists")
                return Conflict(new ApiError(false, "This mobile number is already registered."));

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

            var result = await _repo.LoginAsync(req.Mobile.Trim(), req.DeviceId.Trim());

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
        [FromHeader(Name = "X-User-Id")] long userId)
    {
        try
        {
            if (last4.Length != 4) return BadRequest(new ApiError(false, "last4 must be exactly 4 characters."));
            if (!await _repo.HasActiveSubscriptionAsync(userId))
                return StatusCode(402, new ApiError(false, "subscription_expired"));

            var results = await _repo.SearchByRcAsync(last4);
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
        [FromHeader(Name = "X-User-Id")] long userId)
    {
        try
        {
            if (last5.Length != 5) return BadRequest(new ApiError(false, "last5 must be exactly 5 characters."));
            if (!await _repo.HasActiveSubscriptionAsync(userId))
                return StatusCode(402, new ApiError(false, "subscription_expired"));

            var results = await _repo.SearchByChassisAsync(last5);
            return Ok(new SearchResponse(true, "chassis", last5.ToUpper(), results.Count, results));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Search failed: {ex.Message}"));
        }
    }

    // GET /api/mobile/profile/{userId}
    [HttpGet("profile/{userId:long}")]
    public async Task<IActionResult> GetProfile(long userId)
    {
        try
        {
            var profile = await _repo.GetProfileAsync(userId);
            if (profile == null) return NotFound(new ApiError(false, "User not found."));
            return Ok(profile);
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
            await _repo.UpdatePfpAsync(userId, req.PfpBase64);
            return Ok(new { success = true });
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

    // POST /api/mobile/heartbeat  — updates last_seen + GPS for the user
    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req)
    {
        try
        {
            await _repo.HeartbeatAsync(req.UserId, req.Lat, req.Lng);
            return Ok(new { success = true });
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
            if (!DateTime.TryParse(req.DeviceTimeIso, out var deviceTime))
                deviceTime = DateTime.UtcNow;
            await _repo.LogSearchAsync(req.UserId, req.VehicleNo ?? "", req.ChassisNo ?? "",
                req.Model ?? "", req.Lat, req.Lng, req.Address, deviceTime);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError(false, $"Log failed: {ex.Message}"));
        }
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
}

public record UpdatePfpRequest(string? PfpBase64);
