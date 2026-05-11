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
        string FinanceName = "");

    internal record BranchDetailDto(
        int Id, string Name,
        string Contact1, string Contact2, string Contact3,
        string Address, string BranchCode);

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
