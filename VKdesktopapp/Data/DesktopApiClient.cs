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
using VRASDesktopApp.Models;

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
    // Wire format: gzip-compressed UTF-8 text
    //   Line 0 : branchId
    //   Line 1…: 32 pipe-delimited fields per record (| already stripped from values)
    internal static async Task<(int Inserted, double ElapsedSeconds)> UploadRecordsAsync(
        int branchId, List<UploadRecord> records,
        IProgress<(int pct, string msg)>? progress = null)
    {
        // Build pipe-delimited text (~300 bytes/row for 100k = ~30 MB raw)
        var sb = new StringBuilder(records.Count * 300 + 16);
        sb.AppendLine(branchId.ToString());
        foreach (var r in records)
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

        // Gzip — Fastest gives ~5-10x compression on repetitive text, minimal CPU
        var raw = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest))
                gz.Write(raw, 0, raw.Length);
            compressed = ms.ToArray();
        }

        var base_ = App.ApiBaseUrl.TrimEnd('/');
        var req = new HttpRequestMessage(HttpMethod.Post, $"{base_}/api/mgr/records/upload");
        req.Headers.Add("X-Api-Key", App.ApiKey);
        req.Content = new ByteArrayContent(compressed);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        // ResponseHeadersRead lets us stream the ndjson chunks as they arrive
        using var resp = await App.HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        int inserted = 0;
        double elapsedSeconds = 0;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            int pct   = root.GetProperty("pct").GetInt32();
            string msg = root.GetProperty("msg").GetString() ?? "";

            if (pct == -1) throw new Exception(msg);

            if (pct == 100)
            {
                inserted       = root.GetProperty("inserted").GetInt32();
                elapsedSeconds = root.GetProperty("elapsedSeconds").GetDouble();
            }

            progress?.Report((pct, msg));
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
