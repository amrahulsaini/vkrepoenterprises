using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VRASDesktopApp.Data;

public class BranchRepository
{
    public async Task<List<(int Id, string Name, string Contact1, string Contact2, string Contact3, string Address, long TotalRecords, string UploadedAt)>> GetBranchesByFinanceAsync(int financeId)
    {
        var dtos = await DesktopApiClient.GetBranchesByFinanceAsync(financeId);
        var list = new List<(int, string, string, string, string, string, long, string)>(dtos.Count);
        foreach (var d in dtos)
            list.Add((d.Id, d.Name, d.Contact1, d.Contact2, d.Contact3, d.Address, d.TotalRecords, d.UploadedAt));
        return list;
    }

    public async Task<int> CreateBranchAsync(
        int financeId, string name,
        string? contact1 = null, string? contact2 = null, string? contact3 = null,
        string? address = null, string? branchCode = null,
        string? city = null, string? state = null, string? postal = null, string? notes = null)
        => await DesktopApiClient.CreateBranchAsync(financeId, name,
            contact1, contact2, contact3, address, branchCode, city, state, postal, notes);

    public async Task<(int Id, string Name, string Contact1, string Contact2, string Contact3, string Address, string BranchCode)?> GetBranchAsync(int id)
    {
        var d = await DesktopApiClient.GetBranchAsync(id);
        if (d == null) return null;
        return (d.Id, d.Name, d.Contact1, d.Contact2, d.Contact3, d.Address, d.BranchCode);
    }

    public async Task UpdateBranchAsync(
        int id, string name,
        string? contact1 = null, string? contact2 = null, string? contact3 = null,
        string? address = null, string? branchCode = null)
        => await DesktopApiClient.UpdateBranchAsync(id, name,
            contact1, contact2, contact3, address, branchCode);

    // Single HTTP call → server purges all records + drops branch at loopback speed
    public async Task DeleteBranchAsync(int id, IProgress<string>? progress = null)
    {
        progress?.Report("Deleting branch…");
        await DesktopApiClient.DeleteBranchAsync(id);
    }

    public async Task<List<(int Id, string Name, string FinanceName, long TotalRecords, string UploadedAt)>> GetAllBranchesWithFinanceAsync()
    {
        var dtos = await DesktopApiClient.GetAllBranchesAsync();
        var list = new List<(int, string, string, long, string)>(dtos.Count);
        foreach (var d in dtos)
            list.Add((d.Id, d.Name, d.FinanceName, d.TotalRecords, d.UploadedAt));
        return list;
    }
}
