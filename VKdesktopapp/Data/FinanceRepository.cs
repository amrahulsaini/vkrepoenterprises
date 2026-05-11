using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VRASDesktopApp.Data;

public class FinanceRepository
{
    public async Task<List<(int Id, string Name, long BranchCount, long TotalRecords)>> GetFinancesAsync()
    {
        var dtos = await DesktopApiClient.GetFinancesAsync();
        var list = new List<(int, string, long, long)>(dtos.Count);
        foreach (var d in dtos)
            list.Add((d.Id, d.Name, d.BranchCount, d.TotalRecords));
        return list;
    }

    public async Task<int> CreateFinanceAsync(string name, string? description = null)
        => await DesktopApiClient.CreateFinanceAsync(name, description);

    public async Task UpdateFinanceAsync(int id, string name)
        => await DesktopApiClient.UpdateFinanceAsync(id, name);

    public async Task DeleteFinanceAsync(int id)
        => await DesktopApiClient.DeleteFinanceAsync(id);
}
