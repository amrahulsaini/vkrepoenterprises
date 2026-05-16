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
    internal record PickerUserDto(long Id, string Name, string Mobile, string Address, bool IsActive);
    internal record MgrUserDto(
        long Id, string Name, string Mobile,
        string? Address, string? Pincode, string? PfpBase64, string? DeviceId,
        bool IsActive, bool IsAdmin, decimal Balance, DateTime CreatedAt, string? SubEndDate,
        bool IsStopped = false, bool IsBlacklisted = false);
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
        double? Lat, double? Lng);

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

    // ── KYC documents ──────────────────────────────────────────────────────
    internal record KycDocsDto(string AadhaarFront, string AadhaarBack, string PanFront);

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

    // ── Search logs ─────────────────────────────────────────────────────────

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

    // ── Export DTOs ─────────────────────────────────────────────────────────────

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

    // ── Export methods ──────────────────────────────────────────────────────────

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

    // ── Per-branch / per-finance record export (Finances page Excel download) ────

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

    // Loops all pages and returns the full record set for one branch.
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

    // Loops all pages and returns the full record set for one finance.
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
