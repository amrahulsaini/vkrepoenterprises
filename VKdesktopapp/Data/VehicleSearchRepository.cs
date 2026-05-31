using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Data;

public class VehicleSearchRepository
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public Task<List<VehicleSearchItem>> SearchByRcLast4Async(string last4, CancellationToken ct = default)
        => SearchAsync(last4, "rc", ct);

    public Task<List<VehicleSearchItem>> SearchByChassisLast5Async(string last5, CancellationToken ct = default)
        => SearchAsync(last5, "chassis", ct);

    // SKINNY search: hits /api/mgr/search/list which returns only the few columns
    // the results grid + branch/finance chooser need (id, vehicle/chassis no,
    // model, branch, finance, date) — NOT the ~40 heavy columns. A common
    // last4/last5 can match thousands of duplicate rows; the old heavy endpoint
    // shipped all 40 columns × every row (hundreds of KB–MBs → "sometimes takes
    // seconds"), while a rare number returned almost nothing ("instant"). The
    // skinny payload is a few KB regardless, so every search is consistently
    // fast. The heavy fields are fetched per-record on tap via GetRecordByIdAsync.
    private static async Task<List<VehicleSearchItem>> SearchAsync(string q, string mode, CancellationToken ct)
    {
        var url = $"{App.ApiBaseUrl}api/mgr/search/list?q={Uri.EscapeDataString(q)}&mode={mode}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Api-Key", App.ApiKey);

        var resp = await App.HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        return (await resp.Content.ReadFromJsonAsync<List<VehicleSearchItem>>(_json, ct))
               ?? new List<VehicleSearchItem>();
    }

    /// <summary>Fetches the FULL record (all ~40 fields) for one search result by
    /// id — called only when the user opens a specific record in the detail
    /// panel, since the search list itself is skinny.</summary>
    public async Task<VehicleSearchItem?> GetRecordByIdAsync(long id, CancellationToken ct = default)
    {
        var url = $"{App.ApiBaseUrl}api/mgr/record/{id}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Api-Key", App.ApiKey);

        var resp = await App.HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        return await resp.Content.ReadFromJsonAsync<VehicleSearchItem>(_json, ct);
    }

    public async Task DeleteRecordAsync(long id)
    {
        var url = $"{App.ApiBaseUrl}api/Records/Delete/{id}";
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Add("X-Api-Key", App.ApiKey);
        var resp = await App.HttpClient.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }
}
