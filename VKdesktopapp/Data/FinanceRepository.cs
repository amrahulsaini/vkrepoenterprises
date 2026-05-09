using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Data;

public class FinanceRepository
{
    public async Task<List<(int Id, string Name, long BranchCount, long TotalRecords)>> GetFinancesAsync()
    {
        var list = new List<(int, string, long, long)>();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();

        // Live COUNT from vehicle_records — never trusts the denormalized total_records column
        var sql = @"
            SELECT f.id, f.name,
                   COALESCE(b.branch_cnt, 0) AS branch_count,
                   COALESCE(b.record_cnt, 0) AS total_records
            FROM finances f
            LEFT JOIN (
                SELECT br.finance_id,
                       COUNT(DISTINCT br.id)  AS branch_cnt,
                       COUNT(vr.id)           AS record_cnt
                FROM   branches br
                LEFT JOIN vehicle_records vr ON vr.branch_id = br.id
                WHERE  br.is_active = 1
                GROUP  BY br.finance_id
            ) b ON b.finance_id = f.id
            ORDER BY f.name
            LIMIT 100";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add((rdr.GetInt32(0), rdr.GetString(1), rdr.IsDBNull(2) ? 0 : rdr.GetInt64(2), rdr.IsDBNull(3) ? 0 : rdr.GetInt64(3)));
        }

        return list;
    }

    public async Task<int> CreateFinanceAsync(string name, string? description = null)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "INSERT INTO finances (name, description) VALUES (@name, @descr); SELECT LAST_INSERT_ID();";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@descr", description ?? string.Empty);
        var scalar = await cmd.ExecuteScalarAsync();
        var id = Convert.ToInt32(scalar);
        return id;
    }

    public async Task UpdateFinanceAsync(int id, string name)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "UPDATE finances SET name = @name WHERE id = @id";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteFinanceAsync(int id)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "DELETE FROM finances WHERE id = @id";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
