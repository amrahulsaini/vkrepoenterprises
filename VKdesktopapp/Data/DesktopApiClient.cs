using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace VRASDesktopApp.Data;

// All desktop manager operations go through this client.
// SQL runs server-side → 1 HTTP round-trip instead of N WAN SQL round-trips.
internal static class DesktopApiClient
{
    // DTOs ──────────────────────────────────────────────────────────────────

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
    internal record MgrUserDto(
        long Id, string Name, string Mobile,
        string? Address, string? Pincode, string? PfpBase64, string? DeviceId,
        bool IsActive, bool IsAdmin, decimal Balance, DateTime CreatedAt, string? SubEndDate);
    internal record MgrUsersResponseDto(MgrStatsDto Stats, List<MgrUserDto> Users);
    internal record MgrSubDto(long Id, string StartDate, string EndDate, decimal Amount, string? Notes, DateTime CreatedAt);

    internal record DashboardStatsDto(long TotalRecords, int TotalFinances, int TotalBranches);

    internal record DeviceRequestDto(
        long   Id, long UserId,
        string UserName, string UserMobile,
        string NewDeviceId, string RequestedAt);

    internal record LiveUserDto(
        long    Id, string Name, string Mobile,
        string  LastSeen,
        double? Lat, double? Lng);

    // JSON options — case-insensitive to tolerate camelCase from server
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true };

    // ── Finance ─────────────────────────────────────────────────────────────

    internal static async Task<List<FinanceDto>> GetFinancesAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/finances");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<FinanceDto>>(_json))!;
    }

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

    // ── Branches ────────────────────────────────────────────────────────────

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

    // Clear: 1 HTTP call → server does all chunked SQL at loopback speed
    internal static async Task<int> ClearBranchRecordsAsync(int branchId)
    {
        var resp = await Send(HttpMethod.Post, $"api/mgr/branches/{branchId}/clear");
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return r.GetProperty("deletedCount").GetInt32();
    }

    // Delete: 1 HTTP call → server purges records + drops branch at loopback
    internal static async Task DeleteBranchAsync(int id)
    {
        var resp = await Send(HttpMethod.Delete, $"api/mgr/branches/{id}");
        resp.EnsureSuccessStatusCode();
    }

    // ── App Users ────────────────────────────────────────────────────────────

    internal static async Task<MgrUsersResponseDto> GetUsersWithStatsAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/users");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<MgrUsersResponseDto>(_json))!;
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

    internal static async Task ResetUserDeviceAsync(long userId)
    {
        var resp = await Send(HttpMethod.Post, $"api/mgr/users/{userId}/reset-device");
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

    // ── Dashboard stats ─────────────────────────────────────────────────────

    internal static async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        var resp = await Send(HttpMethod.Get, "api/mgr/dashboard-stats");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<DashboardStatsDto>(_json))!;
    }

    // ── Device change requests ──────────────────────────────────────────────

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

    // ── Live users ──────────────────────────────────────────────────────────

    internal static async Task<List<LiveUserDto>> GetLiveUsersAsync(string? since = null)
    {
        var url = string.IsNullOrWhiteSpace(since)
            ? "api/mgr/live-users"
            : $"api/mgr/live-users?since={Uri.EscapeDataString(since)}";
        var resp = await Send(HttpMethod.Get, url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<LiveUserDto>>(_json))!;
    }

    // ── Column mappings ─────────────────────────────────────────────────────

    // ── Records upload ──────────────────────────────────────────────────────

    internal record UploadRecordDto(
        string VehicleNo, string ChasisNo, string EngineNo, string Model,
        string AgreementNo, string Bucket, string GV, string OD, string Seasoning,
        string TBRFlag, string Sec9Available, string Sec17Available,
        string CustomerName, string CustomerAddress, string CustomerContact,
        string Region, string Area, string BranchNameRaw,
        string Level1, string Level1Contact,
        string Level2, string Level2Contact,
        string Level3, string Level3Contact,
        string Level4, string Level4Contact,
        string SenderMail1, string SenderMail2, string ExecutiveName,
        string Pos, string Toss, string Remark);

    internal static async Task<(int Inserted, double ElapsedSeconds)> UploadRecordsAsync(
        int branchId, List<UploadRecordDto> records)
    {
        var resp = await Send(HttpMethod.Post, "api/mgr/records/upload",
            new { BranchId = branchId, Records = records });
        resp.EnsureSuccessStatusCode();
        var r = await resp.Content.ReadFromJsonAsync<JsonElement>(_json);
        return (r.GetProperty("inserted").GetInt32(),
                r.GetProperty("elapsedSeconds").GetDouble());
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

    // ── HTTP helper ─────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> Send(
        HttpMethod method, string relativeUrl, object? body = null)
    {
        var base_ = App.ApiBaseUrl.TrimEnd('/');
        var req = new HttpRequestMessage(method, $"{base_}/{relativeUrl}");
        req.Headers.Add("X-Api-Key", App.ApiKey);
        if (body != null)
            req.Content = JsonContent.Create(body);
        return App.HttpClient.SendAsync(req);
    }
}
