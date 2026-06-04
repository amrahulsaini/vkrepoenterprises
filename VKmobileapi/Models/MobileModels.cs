namespace VKmobileapi.Models;

// ── Auth ────────────────────────────────────────────────
public record RegisterRequest(
    string  Mobile,
    string  Name,
    string? Address,
    string? Pincode,
    string? PfpBase64,
    string  DeviceId,
    // KYC document photos (base64) — reviewed by the agency admin in WPF.
    string? AadhaarFront,
    string? AadhaarBack,
    string? PanFront,
    string? AccountNumber,
    string? IfscCode,
    // Agency the user is joining + the agency's primary mobile (verification gate)
    string? Slug = null,
    string? AgencyMobile = null,
    // ── Registration-time KYC (new flow) ──────────────────────────────────
    // Selfie of the agent holding their Aadhaar in hand (base64). Aadhaar OKYC
    // (OTP) is verified live on the device before submit; the verified
    // demographics + the agent's live location are carried here and stored on
    // the user row. AadhaarNumber is the full 12-digit number — only the last 4
    // are persisted (kyc_aadhaar_last4).
    string? SelfieWithAadhaar = null,
    string? AadhaarNumber = null,
    string? AadhaarName = null,
    string? AadhaarDob = null,
    string? AadhaarGender = null,
    string? AadhaarAddress = null,
    bool    AadhaarVerified = false,
    double? RegLat = null,
    double? RegLng = null,
    string? RegLocation = null,
    // The photo UIDAI returns in the OKYC verify response (base64 JPEG) — the
    // admin compares it against the selfie in WPF.
    string? AadhaarPhoto = null);

public record LoginRequest(
    string  Mobile,
    string  DeviceId,
    string? Slug = null);

// One entry of the agency picker shown on the register / login screens.
// LogoPath is the server-relative path (e.g. /agency-uploads/<slug>.jpg); the
// mobile app prepends its base URL to render the image.
public record AgencyListItem(long Id, string Name, string Slug, string LogoPath);

// Full agency profile shown in the in-app "Agency" detail panel — primary
// mobile, secondary mobile, and any number of extras the admin added via
// the manage portal, all flattened into one Mobiles list.
public record AgencyInfo(string Name, string Address, List<string> Mobiles, string LogoPath);

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
    string? SubscriptionEndDate,
    // Signed tenant token — the app sends it back as the X-Tenant-Token header
    // so every subsequent request is routed to this agency's database.
    string? TenantToken = null);

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
// Mirrors the desktop (WPF) KYC review payload so the mobile Control Panel can
// show the same review surface: document/photo URLs, the registration-time
// Aadhaar OKYC demographics, capture location, and the review status + reject
// note. The trailing fields are optional so older callers/clients still work.
public record KycInfo(
    bool    KycSubmitted,
    string? AadhaarFront,
    string? AadhaarBack,
    string? PanFront,
    string? Selfie          = null,
    string? AadhaarPhoto    = null,
    string  KycStatus       = "success",
    string? RejectNote      = null,
    bool    AadhaarVerified = false,
    string? AadhaarNumber   = null,
    string? AadhaarLast4    = null,
    string? AadhaarName     = null,
    string? AadhaarDob      = null,
    string? AadhaarGender   = null,
    string? AadhaarAddress  = null,
    double? Lat             = null,
    double? Lng             = null,
    string? LocationLabel   = null);

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
public record AdminUserItem(long Id, string Name, string Mobile, string? Address, string? SubEnd,
    bool IsActive = false, bool IsAdmin = false, bool IsStopped = false, bool IsBlacklisted = false);
public record VerifyAdminPassRequest(string Password);
public record SetUserFlagRequest(bool Value);
// KYC review outcome set from the mobile Control Panel. Status: success | failed | pending.
public record SetKycStatusRequest(string Status, string? Note);

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
