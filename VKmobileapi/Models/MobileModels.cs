namespace VKmobileapi.Models;

// ── Auth ────────────────────────────────────────────────
public record RegisterRequest(
    string  Mobile,
    string  Name,
    string? Address,
    string? Pincode,
    string? PfpBase64,
    string  DeviceId,
    // KYC
    string? AadhaarFront,
    string? AadhaarBack,
    string? PanFront,
    string? AccountNumber,
    string? IfscCode);

public record LoginRequest(
    string Mobile,
    string DeviceId);

public record HeartbeatRequest(long UserId, double? Lat, double? Lng);

public record SearchLogRequest(
    long    UserId,
    string  VehicleNo,
    string  ChassisNo,
    string  Model,
    double? Lat,
    double? Lng,
    string? Address,
    string  DeviceTimeIso);

public record AuthResponse(
    bool   Success,
    string Message,
    string Reason,
    long?  UserId,
    string? Name,
    string? Mobile,
    bool   IsAdmin,
    string? PfpUrl,
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
    string BranchFromExcel,
    string CreatedOn);

public record SearchResponse(
    bool   Success,
    string Mode,
    string Query,
    int    Count,
    List<SearchResult> Results);

// ── Profile ─────────────────────────────────────────────
public record KycInfo(
    bool    KycSubmitted,
    string? AadhaarFront,
    string? AadhaarBack,
    string? PanFront);

public record SubscriptionRecord(
    long    Id,
    string  StartDate,
    string  EndDate,
    decimal Amount,
    string? Notes,
    bool    IsActive);

public record ProfileResponse(
    long    UserId,
    string  Name,
    string  Mobile,
    string? Address,
    string? Pincode,
    string? PfpUrl,
    bool    IsActive,
    bool    IsAdmin,
    decimal Balance,
    string  CreatedAt,
    string? AccountNumber,
    string? IfscCode,
    KycInfo Kyc,
    List<SubscriptionRecord> Subscriptions);

public record ApiError(bool Success, string Message);

public record UserStatusDto(bool IsActive, bool IsStopped, bool IsBlacklisted);
public record VerifySubsPassRequest(string Password);
public record AdminAddSubRequest(string StartDate, string EndDate, decimal Amount, string? Notes);
public record AdminUserItem(long Id, string Name, string Mobile, string? Address, string? SubEnd);

public record SyncBranch(
    int     BranchId,
    string  BranchName,
    string  FinancerName,
    long    TotalRecords,
    string? UploadedAt);

public record SyncBranchResponse(
    bool             Success,
    int              BranchCount,
    long             TotalRecords,
    List<SyncBranch> Branches);

public record SyncRecord(
    long   Id,
    string VehicleNo,
    string ChassisNo,
    string EngineNo,
    string Model,
    string CustomerName,
    string Last4,
    string Last5);

public record SyncRecordsResponse(
    bool             Success,
    int              BranchId,
    int              Page,
    int              PageSize,
    bool             HasMore,
    List<SyncRecord> Records);

public record StatsResponse(
    bool Success,
    long VehicleRecords,
    long RcRecords,
    long ChassisRecords);

// ── Live users ───────────────────────────────────────────
public record LiveUserItem(
    long    Id,
    string  Name,
    string  Mobile,
    string  LastSeen,
    double? Lat,
    double? Lng);

public record LiveUsersResponse(
    bool               Success,
    List<LiveUserItem> Users);
