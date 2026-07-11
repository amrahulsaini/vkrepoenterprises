using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CRMRSDesktopApp.Models;

namespace CRMRSDesktopApp.Data;

internal static class DesktopApiClient
{

    internal record FinanceDto(int Id, string Name, long BranchCount, long TotalRecords);

    internal record BranchDto(
        int Id, string Name,
        string Contact1, string Contact2, string Contact3,
        string Address, long TotalRecords, string UploadedAt,
        string FinanceName = "", int FinanceId = 0);

    internal record BranchDetailDto(
        int Id, string Name,
        string Contact1, string Contact2, string Contact3,
        string Address, string BranchCode);

    internal record MgrStatsDto(int Total, int Active, int Admins, int WithSub);
    internal record PickerUserDto(long Id, string Name, string Mobile, string Address, bool IsActive);
    internal record MgrUserDto(
        long Id, string Name, string Mobile,
        string? Address, string? Pincode, string? PfpBase64, string? DeviceId,
        bool IsActive, bool IsAdmin, decimal Balance, DateTime CreatedAt, string? SubEndDate,
        bool IsStopped = false, bool IsBlacklisted = false,
        int? BillingDemand = null, int? BillingTarget = null, int BilledThisMonth = 0);
    internal record MgrUsersResponseDto(MgrStatsDto Stats, List<MgrUserDto> Users);
    internal record MgrSubDto(long Id, string StartDate, string EndDate, decimal Amount, string? Notes, DateTime CreatedAt);

    internal record DashboardStatsDto(long TotalRecords, int TotalFinances, int TotalBranches);

    internal record BlacklistUserDto(long Id, string Name, string Mobile, string Address, DateTime CreatedAt);
    internal record AllSimpleUserDto(long Id, string Name, string Mobile, string Address,
        bool IsActive, bool IsAdmin, bool IsStopped, bool IsBlacklisted);
    internal record SubsPasswordDto(string Password);

    internal record DeviceRequestDto(
        long   Id, long UserId,
        string UserName, string UserMobile,
        string NewDeviceId, string RequestedAt);

    internal record LiveUserDto(
        long    Id, string Name, string Mobile,
        string  LastSeen,
        double? Lat, double? Lng,
        string? Pfp = null);

    internal record SearchLogRow(
        long    Id,
        long    UserId,
        string  UserName,
        string  UserMobile,
        string  VehicleNo,
        string  ChassisNo,
        string  Model,
        double? Lat,
        double? Lng,
        string? Address,
        string  UserAddress,
        string  DeviceTime,
        string  ServerTime);

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true };


    internal static async Task<List<FinanceDto>> GetFinancesAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/finances");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<FinanceDto>>(_json))!;
    }

    internal record BillingSettingsDto(
        string AgencyName, string PanNo, string GstState, string BankAccountName,
        string AccountNo, string IfscCode, string BankBranch, string ParkingYard,
        string PaymentName, string FooterLine, string? LetterheadUrl, string? BackgroundUrl);

    private record UrlResult(string? Url);

    internal static async Task<BillingSettingsDto?> GetBillingSettingsAsync(int financeId)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/billing/settings?financeId={financeId}");
        return await resp.Content.ReadFromJsonAsync<BillingSettingsDto>(_json);
    }

    internal static async Task SaveBillingSettingsAsync(object dto)
    {
        (await Send(HttpMethod.Put, "api/mgr/billing/settings", dto)).Dispose();
    }

    internal static async Task<string?> UploadBillingImageAsync(string kind, string base64, int financeId)
    {
        var resp = await Send(HttpMethod.Post, $"api/mgr/billing/{kind}", new { ImageBase64 = base64, FinanceId = financeId });
        return (await resp.Content.ReadFromJsonAsync<UrlResult>(_json))?.Url;
    }

    internal record BillingMemberDto(
        long Id, string Name, string Mobile, string Email,
        string Username, string Password, bool IsActive, List<int> FinanceIds);

    internal record MemberLoginResult(long Id, string Name, List<int> FinanceIds);

    internal record RepoSubmissionDto(
        long Id, long? RecordId, int? FinanceId, string FinanceName, string BranchName,
        string LoanNo, string CustomerName, string VehicleNo, string Model, string ChassisNo, string EngineNo,
        string AgentName, string ParkingYardName, string ParkingYardMobile, string LoadDetails,
        string AddlChargesNotes, decimal? AddlChargesAmount,
        string ConfirmationByName, string ConfirmationByMobile, string ExecutiveName,
        string CollectionUpdate, string Remark,
        string BillingAction, string? HoldUntil, int? HoldDays, string BillStatus, string? BilledAt,
        string SubmittedByName, string CreatedAt);

    internal static async Task<List<BillingMemberDto>> GetBillingMembersAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/billing/members");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<BillingMemberDto>>(_json))!;
    }

    internal static async Task<long> CreateBillingMemberAsync(object dto)
    {
        var resp = await Send(HttpMethod.Post, "api/mgr/billing/members", dto);
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return r.GetProperty("id").GetInt64();
    }

    internal static async Task UpdateBillingMemberAsync(long id, object dto)
        => (await Send(HttpMethod.Put, $"api/mgr/billing/members/{id}", dto)).Dispose();

    internal static async Task DeleteBillingMemberAsync(long id)
        => (await Send(HttpMethod.Delete, $"api/mgr/billing/members/{id}")).Dispose();

    internal static async Task SetMemberFinancesAsync(long id, List<int> financeIds)
        => (await Send(HttpMethod.Put, $"api/mgr/billing/members/{id}/finances", new { FinanceIds = financeIds })).Dispose();

    internal static async Task<MemberLoginResult?> BillingMemberLoginAsync(string username, string password)
    {
        var resp = await Send(HttpMethod.Post, "api/mgr/billing/member-login", new { Username = username, Password = password });
        return await resp.Content.ReadFromJsonAsync<MemberLoginResult>(_json);
    }

    internal static async Task<List<RepoSubmissionDto>> GetRepoSubmissionsAsync(
        string? from, string? to, IEnumerable<int> financeIds, string? status)
    {
        var ids = string.Join(",", financeIds);
        var url = $"api/mgr/billing/submissions?financeIds={Uri.EscapeDataString(ids)}";
        if (!string.IsNullOrWhiteSpace(from)) url += $"&from={Uri.EscapeDataString(from)}";
        if (!string.IsNullOrWhiteSpace(to))   url += $"&to={Uri.EscapeDataString(to)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";
        var resp = await Send(HttpMethod.Get, url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<RepoSubmissionDto>>(_json))!;
    }

    internal static async Task MarkSubmissionBilledAsync(long id, long memberId)
        => (await Send(HttpMethod.Post, $"api/mgr/billing/submissions/{id}/billed", new { MemberId = memberId })).Dispose();

    internal static async Task<int> CreateFinanceAsync(string name, string? description)
    {
        var resp = await Send(HttpMethod.Post, "api/mgr/finances",
            new { Name = name, Description = description });
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return r.GetProperty("id").GetInt32();
    }

    internal static async Task UpdateFinanceAsync(int id, string name)
    {
        var resp = await Send(HttpMethod.Put, $"api/mgr/finances/{id}",
            new { Name = name });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task DeleteFinanceAsync(int id)
    {
        var resp = await Send(HttpMethod.Delete, $"api/mgr/finances/{id}");
        resp.EnsureSuccessStatusCode();
    }


    internal static async Task<List<BranchDto>> GetBranchesByFinanceAsync(int financeId)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/branches?financeId={financeId}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<BranchDto>>(_json))!;
    }

    internal static async Task<List<BranchDto>> GetAllBranchesAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/branches");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<BranchDto>>(_json))!;
    }

    internal static async Task<BranchDetailDto?> GetBranchAsync(int id)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/branches/{id}");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<BranchDetailDto>(_json);
    }

    internal static async Task<int> CreateBranchAsync(
        int financeId, string name,
        string? contact1 = null, string? contact2 = null, string? contact3 = null,
        string? address = null, string? branchCode = null,
        string? city = null, string? state = null, string? postal = null, string? notes = null)
    {
        var resp = await Send(HttpMethod.Post, "api/mgr/branches", new
        {
            FinanceId = financeId, Name = name,
            Contact1 = contact1, Contact2 = contact2, Contact3 = contact3,
            Address = address, BranchCode = branchCode,
            City = city, State = state, Postal = postal, Notes = notes
        });
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return r.GetProperty("id").GetInt32();
    }

    internal static async Task UpdateBranchAsync(
        int id, string name,
        string? contact1 = null, string? contact2 = null, string? contact3 = null,
        string? address = null, string? branchCode = null)
    {
        var resp = await Send(HttpMethod.Put, $"api/mgr/branches/{id}", new
        {
            Name = name,
            Contact1 = contact1, Contact2 = contact2, Contact3 = contact3,
            Address = address, BranchCode = branchCode
        });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task<int> ClearBranchRecordsAsync(int branchId)
    {
        var resp = await Send(HttpMethod.Post, $"api/mgr/branches/{branchId}/clear");
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return r.GetProperty("deletedCount").GetInt32();
    }

    internal static async Task DeleteBranchAsync(int id)
    {
        var resp = await Send(HttpMethod.Delete, $"api/mgr/branches/{id}");
        resp.EnsureSuccessStatusCode();
    }


    internal static async Task<MgrUsersResponseDto> GetUsersWithStatsAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/users");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<MgrUsersResponseDto>(_json))!;
    }

    internal static async Task<IntegrationMessagesDto> GetIntegrationMessagesAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/integration-messages");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IntegrationMessagesDto>(_json))!;
    }

    internal static async Task MarkIntegrationMessagesReadAsync()
    {
        var resp = await Send(HttpMethod.Post, "api/mgr/integration-messages/read");
        resp.EnsureSuccessStatusCode();
    }

    internal sealed class IntegrationMessagesDto
    {
        public List<IntegrationMessageItem> Messages { get; set; } = new();
        public int Unread { get; set; }
    }

    internal sealed class IntegrationMessageItem
    {
        public int Id { get; set; }
        public string FromFinance { get; set; } = "";
        public string FromEmail { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsRead { get; set; }
        public string CreatedAt { get; set; } = "";
    }

    internal static async Task<List<PickerUserDto>> GetPickerUsersAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/users/picker");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<PickerUserDto>>(_json))!;
    }

    internal static async Task<MgrStatsDto> GetUserStatsAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/users/stats");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<MgrStatsDto>(_json))!;
    }

    internal static async Task SetUserActiveAsync(long userId, bool active)
    {
        var resp = await Send(HttpMethod.Patch, $"api/mgr/users/{userId}/active", new { Active = active });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task SetUserAdminAsync(long userId, bool admin)
    {
        var resp = await Send(HttpMethod.Patch, $"api/mgr/users/{userId}/admin", new { Admin = admin });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task SetUserBillingTargetsAsync(long userId, int? demand, int? target)
    {
        var resp = await Send(HttpMethod.Patch, $"api/mgr/users/{userId}/billing-targets",
            new { Demand = demand, Target = target });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task ResetUserDeviceAsync(long userId)
    {
        var resp = await Send(HttpMethod.Post, $"api/mgr/users/{userId}/reset-device");
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task SetUserStoppedAsync(long userId, bool stopped)
    {
        var resp = await Send(HttpMethod.Patch, $"api/mgr/users/{userId}/stopped", new { Stopped = stopped });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task SetUserBlacklistedAsync(long userId, bool blacklisted)
    {
        var resp = await Send(HttpMethod.Patch, $"api/mgr/users/{userId}/blacklisted", new { Blacklisted = blacklisted });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task DeleteUserAsync(long userId)
    {
        var resp = await Send(HttpMethod.Delete, $"api/mgr/users/{userId}");
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task<List<int>> GetUserFinanceRestrictionsAsync(long userId)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/users/{userId}/finance-restrictions");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<int>>(_json))!;
    }

    internal static async Task SetUserFinanceRestrictionsAsync(long userId, List<int> financeIds)
    {
        var resp = await Send(HttpMethod.Put, $"api/mgr/users/{userId}/finance-restrictions",
            new { FinanceIds = financeIds });
        resp.EnsureSuccessStatusCode();
    }

    internal record KycDocsDto(
        string AadhaarFront, string AadhaarBack, string PanFront, string? Selfie,
        string? AadhaarPhoto, string? KycStatus, string? RejectNote,
        KycAadhaarDto? Aadhaar, KycLocationDto? Location);
    internal record KycAadhaarDto(
        bool Verified, string? Last4, string? Number, string? Name, string? Dob, string? Gender,
        string? Address, DateTime? VerifiedAt);
    internal record KycLocationDto(double? Lat, double? Lng, string? Label);

    internal static async Task<KycDocsDto> GetUserKycAsync(long userId)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/users/{userId}/kyc");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<KycDocsDto>(_json))!;
    }

    internal static async Task DeleteUserKycAsync(long userId, string docType)
    {
        var resp = await Send(HttpMethod.Delete, $"api/mgr/users/{userId}/kyc/{docType}");
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task SetUserKycStatusAsync(long userId, string status, string? note)
    {
        var resp = await Send(HttpMethod.Patch, $"api/mgr/users/{userId}/kyc-status",
            new { status, note });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task DeleteUserKycUidaiAsync(long userId)
    {
        var resp = await Send(HttpMethod.Delete, $"api/mgr/users/{userId}/kyc-uidai");
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task<bool> VerifyLoginPasswordAsync(string email, string password)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{App.ApiBaseUrl.TrimEnd('/')}/api/agency/desktop/login")
            {
                Content = JsonContent.Create(new { email = (email ?? "").Trim().ToLowerInvariant(), password })
            };
            req.Headers.Authorization = null;
            using var resp = await App.HttpClient.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    internal static async Task SetUserAdminPassAsync(long userId, string password)
    {
        var resp = await Send(HttpMethod.Patch, $"api/mgr/users/{userId}/admin-pass",
            new { Password = password });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task<bool> IsUserAdminPassSetAsync(long userId)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/users/{userId}/admin-pass");
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return r.GetProperty("isSet").GetBoolean();
    }

    internal static async Task<List<BlacklistUserDto>> GetBlacklistedUsersAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/blacklist");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<BlacklistUserDto>>(_json))!;
    }

    internal static async Task<List<AllSimpleUserDto>> GetAllSimpleUsersAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/users/all-simple");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<AllSimpleUserDto>>(_json))!;
    }

    internal static async Task<string> GetSubsPasswordAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/settings/subs-password");
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<SubsPasswordDto>(_json);
        return r?.Password ?? "";
    }

    internal static async Task SetSubsPasswordAsync(string password)
    {
        var resp = await Send(HttpMethod.Put, "api/mgr/settings/subs-password", new { Password = password });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task<string> GetControlPasswordAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/settings/control-password");
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<SubsPasswordDto>(_json);
        return r?.Password ?? "";
    }

    internal static async Task SetControlPasswordAsync(string password)
    {
        var resp = await Send(HttpMethod.Put, "api/mgr/settings/control-password", new { Password = password });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task<string> GetAllocationPasswordAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/settings/allocation-password");
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<SubsPasswordDto>(_json);
        return r?.Password ?? "";
    }

    internal static async Task SetAllocationPasswordAsync(string password)
    {
        var resp = await Send(HttpMethod.Put, "api/mgr/settings/allocation-password", new { Password = password });
        resp.EnsureSuccessStatusCode();
    }

    internal record AgencyProfileDto(
        int Id, string Name, string Address, string Mobile1, string Mobile2,
        List<string> Extras, string LogoPath);

    internal static async Task<AgencyProfileDto?> GetAgencyProfileAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/agency/desktop/profile");
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AgencyProfileDto>(_json);
    }

    internal static async Task SaveAgencyProfileAsync(
        string name, string address, string mobile1, string mobile2, List<string> extras)
    {
        var resp = await Send(HttpMethod.Post, "api/agency/desktop/profile",
            new { name, address, mobile1, mobile2, extras });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task<List<MgrSubDto>> GetSubscriptionsAsync(long userId)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/users/{userId}/subscriptions");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<MgrSubDto>>(_json))!;
    }

    internal static async Task AddSubscriptionAsync(
        long userId, string startDate, string endDate, decimal amount, string? notes)
    {
        var resp = await Send(HttpMethod.Post, $"api/mgr/users/{userId}/subscriptions",
            new { StartDate = startDate, EndDate = endDate, Amount = amount, Notes = notes });
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task DeleteSubscriptionAsync(long subId)
    {
        var resp = await Send(HttpMethod.Delete, $"api/mgr/subscriptions/{subId}");
        resp.EnsureSuccessStatusCode();
    }


    internal static async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/dashboard-stats");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<DashboardStatsDto>(_json))!;
    }


    internal static async Task<List<DeviceRequestDto>> GetDeviceRequestsAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/device-requests");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<DeviceRequestDto>>(_json))!;
    }

    internal static async Task ApproveDeviceRequestAsync(long requestId)
    {
        var resp = await Send(HttpMethod.Post, $"api/mgr/device-requests/{requestId}/approve");
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task DenyDeviceRequestAsync(long requestId)
    {
        var resp = await Send(HttpMethod.Delete, $"api/mgr/device-requests/{requestId}");
        resp.EnsureSuccessStatusCode();
    }


    internal static async Task<List<LiveUserDto>> GetLiveUsersAsync(string? since = null)
    {
        var url = string.IsNullOrWhiteSpace(since)
            ? "api/mgr/live-users"
            : $"api/mgr/live-users?since={Uri.EscapeDataString(since)}";
        var resp = await Send(HttpMethod.Get, url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<LiveUserDto>>(_json))!;
    }


    internal static async Task<List<SearchLogRow>> GetSearchLogsAsync(
        string? fromDate = null, string? toDate = null,
        long? userId = null, string? q = null, bool export = false)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(fromDate)) qs.Add($"fromDate={Uri.EscapeDataString(fromDate)}");
        if (!string.IsNullOrWhiteSpace(toDate))   qs.Add($"toDate={Uri.EscapeDataString(toDate)}");
        if (userId.HasValue)                       qs.Add($"userId={userId.Value}");
        if (!string.IsNullOrWhiteSpace(q))         qs.Add($"q={Uri.EscapeDataString(q)}");
        if (export)                                qs.Add("export=true");
        var url = "api/mgr/search-logs" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        var resp = await Send(HttpMethod.Get, url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<SearchLogRow>>(_json))!;
    }


    internal static async Task<(int Inserted, double ElapsedSeconds)> UploadRecordsAsync(
        int branchId, List<UploadRecord> records,
        IProgress<(int pct, string msg)>? progress = null)
    {
        const int CHUNK = 25_000;
        int total  = records.Count;
        int chunks = Math.Max(1, (total + CHUNK - 1) / CHUNK);

        int inserted = 0;
        double elapsedSeconds = 0;

        static byte[] BuildPayload(int bId, List<UploadRecord> rows)
        {
            var sb = new StringBuilder(rows.Count * 300 + 16);
            sb.AppendLine(bId.ToString());
            foreach (var r in rows)
            {
                sb.Append(r.FormatedVehicleNo).Append('|')
                  .Append(r.ChasisNo).Append('|')
                  .Append(r.EngineNo).Append('|')
                  .Append(r.Model).Append('|')
                  .Append(r.AgreementNo).Append('|')
                  .Append(r.Bucket).Append('|')
                  .Append(r.GV).Append('|')
                  .Append(r.OD).Append('|')
                  .Append(r.Seasoning).Append('|')
                  .Append(r.TBRFlag).Append('|')
                  .Append(r.Sec9Available).Append('|')
                  .Append(r.Sec17Available).Append('|')
                  .Append(r.CustomerName).Append('|')
                  .Append(r.CustomerAddress).Append('|')
                  .Append(r.CustomerContactNos).Append('|')
                  .Append(r.Region).Append('|')
                  .Append(r.Area).Append('|')
                  .Append(r.BranchName).Append('|')
                  .Append(r.Level1).Append('|')
                  .Append(r.Level1ContactNos).Append('|')
                  .Append(r.Level2).Append('|')
                  .Append(r.Level2ContactNos).Append('|')
                  .Append(r.Level3).Append('|')
                  .Append(r.Level3ContactNos).Append('|')
                  .Append(r.Level4).Append('|')
                  .Append(r.Level4ContactNos).Append('|')
                  .Append(r.SenderMailId1).Append('|')
                  .Append(r.SenderMailId2).Append('|')
                  .Append(r.ExecutiveName).Append('|')
                  .Append(r.POS).Append('|')
                  .Append(r.TOSS).Append('|')
                  .AppendLine(r.Remark);
            }
            var raw = Encoding.UTF8.GetBytes(sb.ToString());
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest))
                gz.Write(raw, 0, raw.Length);
            return ms.ToArray();
        }

        static async Task<(int Inserted, double Elapsed)> PostOnce(
            byte[] payload, string mode, Action<int, string> rep)
        {
            var base_ = App.ApiBaseUrl.TrimEnd('/');
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"{base_}/api/mgr/records/upload?mode={mode}");
            req.Headers.Add("X-Api-Key", App.ApiKey);
            req.Content = new ByteArrayContent(payload);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var resp = await App.HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            int ins = 0; double el = 0;
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                int pct = root.GetProperty("pct").GetInt32();
                string msg = root.GetProperty("msg").GetString() ?? "";
                if (pct == -1) throw new Exception(msg);
                if (pct == 100 && root.TryGetProperty("inserted", out var insEl))
                {
                    ins = insEl.GetInt32();
                    el  = root.GetProperty("elapsedSeconds").GetDouble();
                }
                rep(pct, msg);
            }
            return (ins, el);
        }

        static bool IsTransient(Exception ex)
        {
            if (ex is IOException or TaskCanceledException or System.Net.Sockets.SocketException)
                return true;
            if (ex is HttpRequestException hre)
                return hre.StatusCode is null
                    || (int)hre.StatusCode.Value is 408 or 429 or >= 500;
            return false;
        }

        for (int ci = 0; ci < chunks; ci++)
        {
            int start = ci * CHUNK;
            int count = Math.Min(CHUNK, total - start);
            var slice = records.GetRange(start, count);
            string mode = chunks == 1        ? "replace"
                        : ci == 0            ? "begin"
                        : ci == chunks - 1   ? "finish"
                        :                      "append";

            byte[] payload = BuildPayload(branchId, slice);

            int lo = (int)(100.0 * ci / chunks);
            int hi = (int)(100.0 * (ci + 1) / chunks);
            int doneBefore = start;
            void Report(int chunkPct, string msg) =>
                progress?.Report((
                    Math.Clamp(lo + (hi - lo) * chunkPct / 100, 0, 100),
                    chunks == 1 ? msg : $"Batch {ci + 1}/{chunks} ({doneBefore:N0}/{total:N0}) — {msg}"));

            const int MAX_ATTEMPTS = 4;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    (inserted, elapsedSeconds) = await PostOnce(payload, mode, Report);
                    break;
                }
                catch (Exception ex) when (attempt < MAX_ATTEMPTS && IsTransient(ex))
                {
                    int wait = 2 * attempt;
                    Report(0, $"network issue — retrying in {wait}s (try {attempt + 1}/{MAX_ATTEMPTS})");
                    await Task.Delay(TimeSpan.FromSeconds(wait));
                }
            }
        }

        return (inserted, elapsedSeconds);
    }

    internal record ColumnTypeDto(int Id, string Name);
    internal record MappingDto(int Id, int ColumnTypeId, string Name);
    internal record ColumnMappingsDto(List<ColumnTypeDto> ColumnTypes, List<MappingDto> Mappings);

    internal static async Task<ColumnMappingsDto> GetColumnMappingsAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/column-mappings");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ColumnMappingsDto>(_json))!;
    }

    internal static async Task<MappingDto> CreateMappingAsync(int columnTypeId, string rawName)
    {
        var resp = await Send(HttpMethod.Post, "api/mgr/column-mappings",
            new { ColumnTypeId = columnTypeId, RawName = rawName });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<MappingDto>(_json))!;
    }

    internal static async Task DeleteColumnMappingAsync(int mappingId)
    {
        var resp = await Send(HttpMethod.Delete, $"api/mgr/column-mappings/{mappingId}");
        resp.EnsureSuccessStatusCode();
    }

    internal static async Task<ColumnTypeDto> CreateColumnTypeAsync(string name)
    {
        var resp = await Send(HttpMethod.Post, "api/mgr/column-types", new { Name = name });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ColumnTypeDto>(_json))!;
    }


    internal record ExportUserRow(long Id, string Name, string Mobile, string? Address, string? Pincode,
        bool IsActive, bool IsAdmin, bool IsStopped, bool IsBlacklisted,
        decimal Balance, string CreatedAt, string? SubEnd);

    internal record ExportSubRow(long Id, long UserId, string UserName, string UserMobile,
        string StartDate, string EndDate, decimal Amount, string? Notes, string CreatedAt);

    internal record ExportVehicleRow(
        string VehicleNo, string ChassisNo, string EngineNo, string Model,
        string AgreementNo, string CustomerName, string CustomerContact, string CustomerAddress,
        string Financer, string BranchName, string Bucket, string Gv, string Od, string Seasoning,
        string TbrFlag, string Sec9, string Sec17, string Level1, string Level1Contact,
        string Level2, string Level2Contact, string Level3, string Level3Contact,
        string Level4, string Level4Contact, string SenderMail1, string SenderMail2,
        string ExecutiveName, string Pos, string Toss, string Remark, string Region, string Area, string CreatedOn);

    internal record ExportPage<T>(long Total, int Page, int Size, bool HasMore, List<T> Rows);


    internal static async Task<List<ExportUserRow>> ExportUsersAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/export/users");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<ExportUserRow>>(_json))!;
    }

    internal static async Task<List<ExportSubRow>> ExportSubscriptionsAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/export/subscriptions");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<ExportSubRow>>(_json))!;
    }

    internal static async Task<ExportPage<ExportVehicleRow>> ExportVehicleRecordsPageAsync(int page, int size = 5000)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/export/vehicle-records?page={page}&size={size}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ExportPage<ExportVehicleRow>>(_json))!;
    }

    internal static async Task<ExportPage<ExportVehicleRow>> ExportRcRecordsPageAsync(int page, int size = 5000)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/export/rc-records?page={page}&size={size}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ExportPage<ExportVehicleRow>>(_json))!;
    }

    internal static async Task<ExportPage<ExportVehicleRow>> ExportChassisRecordsPageAsync(int page, int size = 5000)
    {
        var resp = await Send(HttpMethod.Get, $"api/mgr/export/chassis-records?page={page}&size={size}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ExportPage<ExportVehicleRow>>(_json))!;
    }


    internal static async Task<ExportPage<ExportVehicleRow>> ExportBranchRecordsPageAsync(
        int branchId, int page, int size = 5000)
    {
        var resp = await Send(HttpMethod.Get,
            $"api/mgr/export/branch-records?branchId={branchId}&page={page}&size={size}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ExportPage<ExportVehicleRow>>(_json))!;
    }

    internal static async Task<ExportPage<ExportVehicleRow>> ExportFinanceRecordsPageAsync(
        int financeId, int page, int size = 5000)
    {
        var resp = await Send(HttpMethod.Get,
            $"api/mgr/export/finance-records?financeId={financeId}&page={page}&size={size}");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ExportPage<ExportVehicleRow>>(_json))!;
    }

    internal static async Task<List<ExportVehicleRow>> ExportBranchRecordsAsync(int branchId)
    {
        var all = new List<ExportVehicleRow>();
        for (int page = 0; ; page++)
        {
            var p = await ExportBranchRecordsPageAsync(branchId, page);
            all.AddRange(p.Rows);
            if (!p.HasMore || p.Rows.Count == 0) break;
        }
        return all;
    }

    internal static async Task<List<ExportVehicleRow>> ExportFinanceRecordsAsync(int financeId)
    {
        var all = new List<ExportVehicleRow>();
        for (int page = 0; ; page++)
        {
            var p = await ExportFinanceRecordsPageAsync(financeId, page);
            all.AddRange(p.Rows);
            if (!p.HasMore || p.Rows.Count == 0) break;
        }
        return all;
    }

    internal static Task DownloadFinanceXlsxAsync(int financeId, string name, string savePath, IProgress<long>? onBytes = null)
        => DownloadXlsxAsync($"api/mgr/export/finance-records.xlsx?financeId={financeId}&name={Uri.EscapeDataString(name)}", savePath, onBytes);

    internal static Task DownloadBranchXlsxAsync(int branchId, string name, string savePath, IProgress<long>? onBytes = null)
        => DownloadXlsxAsync($"api/mgr/export/branch-records.xlsx?branchId={branchId}&name={Uri.EscapeDataString(name)}", savePath, onBytes);

    internal static Task DownloadFinanceXlsxChunkAsync(int financeId, string name, long offset, int count, string savePath, IProgress<long>? onBytes = null)
        => DownloadXlsxAsync($"api/mgr/export/finance-records.xlsx?financeId={financeId}&name={Uri.EscapeDataString(name)}&offset={offset}&limit={count}", savePath, onBytes);

    internal static Task DownloadBranchXlsxChunkAsync(int branchId, string name, long offset, int count, string savePath, IProgress<long>? onBytes = null)
        => DownloadXlsxAsync($"api/mgr/export/branch-records.xlsx?branchId={branchId}&name={Uri.EscapeDataString(name)}&offset={offset}&limit={count}", savePath, onBytes);

    internal static Task DownloadRecordsXlsxChunkAsync(string recordType, string name, long offset, int count, string savePath, IProgress<long>? onBytes = null)
        => DownloadXlsxAsync($"api/mgr/export/{recordType}.xlsx?name={Uri.EscapeDataString(name)}&offset={offset}&limit={count}", savePath, onBytes);

    internal static Task DownloadSearchLogsXlsxAsync(string? fromDate, string? toDate, long? userId, string? q, string savePath, IProgress<long>? onBytes = null)
    {
        var qs = $"?fromDate={Uri.EscapeDataString(fromDate ?? "")}&toDate={Uri.EscapeDataString(toDate ?? "")}" +
                 $"&q={Uri.EscapeDataString(q ?? "")}" + (userId.HasValue ? $"&userId={userId.Value}" : "");
        return DownloadXlsxAsync($"api/mgr/search-logs.xlsx{qs}", savePath, onBytes);
    }

    private static async Task DownloadXlsxAsync(string relativeUrl, string savePath, IProgress<long>? onBytes)
    {
        var base_ = App.ApiBaseUrl.TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{base_}/{relativeUrl}");
        req.Headers.Add("X-Api-Key", App.ApiKey);
        using var resp = await App.HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = await resp.Content.ReadAsStringAsync();
            throw new Exception($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {msg}");
        }
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var fs  = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        var buf = new byte[1 << 20];
        long total = 0; int n;
        while ((n = await src.ReadAsync(buf)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, n));
            total += n;
            onBytes?.Report(total);
        }
    }

    internal record TicketMsg(int Id, string Sender, string Body, string CreatedAt);
    internal record TicketDto(
        int Id, string Subject, string Message, string ScreenshotUrl,
        string Status, string CreatedAt, string UpdatedAt,
        string AgencyName, string AgencySlug, List<TicketMsg>? Messages);

    internal static async Task<List<TicketDto>> GetMyTicketsAsync()
    {
        var url  = $"{App.ApiBaseUrl.TrimEnd('/')}/api/agency/desktop/tickets";
        var resp = await App.HttpClient.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<TicketDto>>(_json)) ?? new();
    }

    internal static async Task CreateTicketAsync(string subject, string message, string? screenshotBase64)
    {
        var url  = $"{App.ApiBaseUrl.TrimEnd('/')}/api/agency/desktop/tickets";
        var resp = await App.HttpClient.PostAsJsonAsync(url, new { subject, message, screenshotBase64 });
        if (!resp.IsSuccessStatusCode)
            throw new Exception(await resp.Content.ReadAsStringAsync());
    }

    internal static async Task PostTicketMessageAsync(int ticketId, string body)
    {
        var url  = $"{App.ApiBaseUrl.TrimEnd('/')}/api/agency/desktop/tickets/{ticketId}/messages";
        var resp = await App.HttpClient.PostAsJsonAsync(url, new { body });
        if (!resp.IsSuccessStatusCode)
            throw new Exception(await resp.Content.ReadAsStringAsync());
    }

    internal static async Task<int> GetAdminMessageCountAsync()
    {
        try
        {
            var tickets = await GetMyTicketsAsync();
            int n = 0;
            foreach (var t in tickets)
                if (t.Messages != null)
                    foreach (var m in t.Messages)
                        if (m.Sender == "admin") n++;
            return n;
        }
        catch { return -1; }
    }


    private static bool IsTransientNet(Exception ex)
    {
        if (ex is IOException or TaskCanceledException or System.Net.Sockets.SocketException)
            return true;
        if (ex is HttpRequestException hre)
            return hre.StatusCode is null
                || (int)hre.StatusCode.Value is 408 or 429 or >= 500;
        return false;
    }

    private static async Task<HttpResponseMessage> Send(
        HttpMethod method, string relativeUrl, object? body = null)
    {
        var base_ = App.ApiBaseUrl.TrimEnd('/');
        string? bodyJson = body != null ? JsonSerializer.Serialize(body) : null;

        const int MAX_ATTEMPTS = 3;
        for (int attempt = 1; ; attempt++)
        {
            var req = new HttpRequestMessage(method, $"{base_}/{relativeUrl}");
            req.Headers.Add("X-Api-Key", App.ApiKey);
            if (bodyJson != null)
                req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await App.HttpClient.SendAsync(req);
            }
            catch (Exception ex) when (attempt < MAX_ATTEMPTS && IsTransientNet(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
                continue;
            }

            if (resp.IsSuccessStatusCode) return resp;

            if (attempt < MAX_ATTEMPTS && (int)resp.StatusCode is 408 or 429 or >= 500)
            {
                resp.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
                continue;
            }

            string body_ = "";
            try { body_ = await resp.Content.ReadAsStringAsync(); } catch { }
            if (body_.Length > 1500) body_ = body_.Substring(0, 1500) + "…";
            throw new HttpRequestException(
                $"{(int)resp.StatusCode} {resp.ReasonPhrase} — {method} {relativeUrl}\n\n{body_}",
                null, resp.StatusCode);
        }
    }
}
