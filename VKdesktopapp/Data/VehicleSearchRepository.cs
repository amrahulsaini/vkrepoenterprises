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

    private static async Task<List<VehicleSearchItem>> SearchAsync(string q, string mode, CancellationToken ct)
    {
        var url = $"{App.ApiBaseUrl}api/mgr/search?q={Uri.EscapeDataString(q)}&mode={mode}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Api-Key", App.ApiKey);

        var resp = await App.HttpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        return (await resp.Content.ReadFromJsonAsync<List<VehicleSearchItem>>(_json, ct))
               ?? new List<VehicleSearchItem>();
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
