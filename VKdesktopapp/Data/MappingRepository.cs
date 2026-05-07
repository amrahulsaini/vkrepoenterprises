using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MySqlConnector;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Data;

public class MappingRepository
{
    public async Task<MappingDetails> GetMappingDetailsAsync()
    {
        var details = new MappingDetails();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();

        const string sqlTypes = "SELECT id, name FROM column_types ORDER BY sort_order, id";
        await using var cmdTypes = new MySqlCommand(sqlTypes, conn);
        await using var rdrTypes = await cmdTypes.ExecuteReaderAsync();
        while (await rdrTypes.ReadAsync())
            details.ColumnTypes.Add(new ColumnType
            {
                ColumnTypeId   = rdrTypes.GetInt32(0),
                ColumnTypeName = rdrTypes.GetString(1)
            });
        await rdrTypes.CloseAsync();

        const string sqlMaps = "SELECT id, column_type_id, name FROM column_mappings ORDER BY column_type_id, name";
        await using var cmdMaps = new MySqlCommand(sqlMaps, conn);
        await using var rdrMaps = await cmdMaps.ExecuteReaderAsync();
        while (await rdrMaps.ReadAsync())
            details.Mappings.Add(new Mapping
            {
                MappingId    = rdrMaps.GetInt32(0),
                ColumnTypeId = rdrMaps.GetInt32(1),
                Name         = rdrMaps.GetString(2)
            });

        return details;
    }

    public async Task<Mapping> CreateMappingAsync(int columnTypeId, string rawName)
    {
        // Normalize exactly as MapColumns does: strip non-alphanumeric, lowercase
        var normalized = Regex.Replace(rawName, "[^A-Za-z0-9]", "").ToLower();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO column_mappings (column_type_id, name) VALUES (@tid, @name); SELECT LAST_INSERT_ID();";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tid", columnTypeId);
        cmd.Parameters.AddWithValue("@name", normalized);
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return new Mapping { MappingId = id, ColumnTypeId = columnTypeId, Name = normalized };
    }

    public async Task DeleteMappingAsync(int mappingId)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "DELETE FROM column_mappings WHERE id = @id";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", mappingId);
        await cmd.ExecuteNonQueryAsync();
    }
}
