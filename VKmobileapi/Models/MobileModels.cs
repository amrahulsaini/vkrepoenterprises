namespace VKmobileapi.Models;

// ── Auth ────────────────────────────────────────────────
public record RegisterRequest(
    string Mobile,
    string Name,
    string? Address,
    string? Pincode,
    string? PfpBase64,
    string DeviceId);

public record LoginRequest(
    string Mobile,
    string DeviceId);

public record AuthResponse(
    bool   Success,
    string Message,
    string Reason,     // "ok" | "pending_approval" | "device_mismatch" | "not_found" | "inactive"
    long?  UserId,
    string? Name,
    string? Mobile,
    bool   IsAdmin,
    string? PfpBase64,
    string? SubscriptionEndDate);

// ── Search ──────────────────────────────────────────────
public record SearchResult(
    long   Id,
    string VehicleNo,
    string ChassisNo,
    string EngineNo,
    string Model,
    string AgreementNo,
    string CustomerName,
    string CustomerContact,
    string CustomerAddress,
    string Financer,
    string BranchName,
    string FirstContact,
    string SecondContact,
    string ThirdContact,
    string Address,
    string Region,
    string Area,
    string Bucket,
    string GV,
    string OD,
    string Seasoning,
    string TbrFlag,
    string Sec9,
    string Sec17,
    string Level1,
    string Level1Contact,
    string Level2,
    string Level2Contact,
    string Level3,
    string Level3Contact,
    string Level4,
    string Level4Contact,
    string SenderMail1,
    string SenderMail2,
    string ExecutiveName,
    string Pos,
    string Toss,
    string Remark,
    string ReleaseStatus,
    string BranchFromExcel,
    string CreatedOn);

public record SearchResponse(
    bool   Success,
    string Mode,
    string Query,
    int    Count,
    List<SearchResult> Results);

public record ApiError(bool Success, string Message);
