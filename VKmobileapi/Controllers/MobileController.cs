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
        if (string.IsNullOrWhiteSpace(req.Mobile) || string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new ApiError(false, "Mobile and name are required."));
        if (string.IsNullOrWhiteSpace(req.DeviceId))
            return BadRequest(new ApiError(false, "Device ID is required."));

        var (success, reason, userId) = await _repo.RegisterAsync(
            req.Mobile.Trim(), req.Name.Trim(),
            req.Address?.Trim(), req.Pincode?.Trim(),
            req.PfpBase64, req.DeviceId.Trim());

        if (!success && reason == "mobile_exists")
            return Conflict(new ApiError(false, "This mobile number is already registered."));

        return Ok(new { success = true, message = "Registered! Waiting for admin approval.", userId });
    }

    // POST /api/mobile/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
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

    // GET /api/mobile/search/rc/{last4}
    [HttpGet("search/rc/{last4}")]
    public async Task<IActionResult> SearchRc(
        string last4,
        [FromHeader(Name = "X-User-Id")] long userId)
    {
        if (last4.Length != 4) return BadRequest(new ApiError(false, "last4 must be exactly 4 characters."));
        if (!await _repo.HasActiveSubscriptionAsync(userId))
            return StatusCode(402, new ApiError(false, "subscription_expired"));

        var results = await _repo.SearchByRcAsync(last4);
        return Ok(new SearchResponse(true, "rc", last4.ToUpper(), results.Count, results));
    }

    // GET /api/mobile/search/chassis/{last5}
    [HttpGet("search/chassis/{last5}")]
    public async Task<IActionResult> SearchChassis(
        string last5,
        [FromHeader(Name = "X-User-Id")] long userId)
    {
        if (last5.Length != 5) return BadRequest(new ApiError(false, "last5 must be exactly 5 characters."));
        if (!await _repo.HasActiveSubscriptionAsync(userId))
            return StatusCode(402, new ApiError(false, "subscription_expired"));

        var results = await _repo.SearchByChassisAsync(last5);
        return Ok(new SearchResponse(true, "chassis", last5.ToUpper(), results.Count, results));
    }

    // GET /api/mobile/pfp/{userId}
    [HttpGet("pfp/{userId:long}")]
    public async Task<IActionResult> GetPfp(long userId)
    {
        var pfp = await _repo.GetPfpAsync(userId);
        if (pfp == null) return NotFound();
        return Ok(new { pfpBase64 = pfp });
    }
}
