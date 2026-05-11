using System.Threading.Tasks;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Data;

public class MappingRepository
{
    public async Task<MappingDetails> GetMappingDetailsAsync()
    {
        var dto     = await DesktopApiClient.GetColumnMappingsAsync();
        var details = new MappingDetails();

        foreach (var t in dto.ColumnTypes)
            details.ColumnTypes.Add(new ColumnType { ColumnTypeId = t.Id, ColumnTypeName = t.Name });

        foreach (var m in dto.Mappings)
            details.Mappings.Add(new Mapping { MappingId = m.Id, ColumnTypeId = m.ColumnTypeId, Name = m.Name });

        return details;
    }

    public async Task<Mapping> CreateMappingAsync(int columnTypeId, string rawName)
    {
        var dto = await DesktopApiClient.CreateMappingAsync(columnTypeId, rawName);
        return new Mapping { MappingId = dto.Id, ColumnTypeId = dto.ColumnTypeId, Name = dto.Name };
    }

    public async Task DeleteMappingAsync(int mappingId)
    {
        await DesktopApiClient.DeleteColumnMappingAsync(mappingId);
    }

    public async Task<ColumnType> CreateColumnTypeAsync(string name)
    {
        var dto = await DesktopApiClient.CreateColumnTypeAsync(name);
        return new ColumnType { ColumnTypeId = dto.Id, ColumnTypeName = dto.Name };
    }
}
