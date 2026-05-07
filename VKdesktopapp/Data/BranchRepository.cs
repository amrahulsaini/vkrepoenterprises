using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;

namespace VRASDesktopApp.Data;

public class BranchRepository
{
    public async Task<List<(int Id, string Name, string Contact1, string Contact2, string Contact3, string Address, long TotalRecords, string UploadedAt)>> GetBranchesByFinanceAsync(int financeId)
    {
        var list = new List<(int, string, string, string, string, string, long, string)>();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        var sql = @"SELECT id, name, contact, total_records, IFNULL(DATE_FORMAT(uploaded_at, '%d %b %y %h:%i %p'),'') as uploaded_at
FROM branches
WHERE finance_id = @fid AND is_active = 1
ORDER BY total_records DESC LIMIT 100";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@fid", financeId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            var id = rdr.GetInt32(0);
            var name = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
            // existing legacy `contact` column may still be present; prefer contact1 if available
            string contact1 = string.Empty;
            string contact2 = string.Empty;
            string contact3 = string.Empty;
            string address = string.Empty;
            // attempt to read by column name safely
            try { contact1 = rdr.HasRows && !rdr.IsDBNull(rdr.GetOrdinal("contact1")) ? rdr.GetString(rdr.GetOrdinal("contact1")) : (rdr.IsDBNull(2) ? string.Empty : rdr.GetString(2)); } catch { contact1 = rdr.IsDBNull(2) ? string.Empty : rdr.GetString(2); }
            try { contact2 = !rdr.IsDBNull(rdr.GetOrdinal("contact2")) ? rdr.GetString(rdr.GetOrdinal("contact2")) : string.Empty; } catch { contact2 = string.Empty; }
            try { contact3 = !rdr.IsDBNull(rdr.GetOrdinal("contact3")) ? rdr.GetString(rdr.GetOrdinal("contact3")) : string.Empty; } catch { contact3 = string.Empty; }
            try { address = !rdr.IsDBNull(rdr.GetOrdinal("address")) ? rdr.GetString(rdr.GetOrdinal("address")) : string.Empty; } catch { address = string.Empty; }
            var total = rdr.IsDBNull(3) ? 0 : rdr.GetInt64(3);
            var uploaded = rdr.IsDBNull(4) ? string.Empty : rdr.GetString(4);
            list.Add((id, name, contact1, contact2, contact3, address, total, uploaded));
        }
        return list;
    }

    public async Task<int> CreateBranchAsync(int financeId, string name, string? contact1 = null, string? contact2 = null, string? contact3 = null, string? address = null, string? branchCode = null, string? city = null, string? state = null, string? postal = null, string? notes = null)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        var sql = @"INSERT INTO branches (finance_id, name, contact1, contact2, contact3, address, branch_code, city, state, postal_code, notes)
VALUES (@fid, @name, @c1, @c2, @c3, @addr, @bcode, @city, @state, @postal, @notes); SELECT LAST_INSERT_ID();";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@fid", financeId);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@c1", contact1 ?? string.Empty);
        cmd.Parameters.AddWithValue("@c2", contact2 ?? string.Empty);
        cmd.Parameters.AddWithValue("@c3", contact3 ?? string.Empty);
        cmd.Parameters.AddWithValue("@addr", address ?? string.Empty);
        cmd.Parameters.AddWithValue("@bcode", branchCode ?? string.Empty);
        cmd.Parameters.AddWithValue("@city", city ?? string.Empty);
        cmd.Parameters.AddWithValue("@state", state ?? string.Empty);
        cmd.Parameters.AddWithValue("@postal", postal ?? string.Empty);
        cmd.Parameters.AddWithValue("@notes", notes ?? string.Empty);
        var scalar = await cmd.ExecuteScalarAsync();
        var id = Convert.ToInt32(scalar);
        return id;
    }

    public async Task<(int Id, string Name, string Contact1, string Contact2, string Contact3, string Address, string BranchCode)?> GetBranchAsync(int id)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "SELECT id, name, COALESCE(contact1,'') AS c1, COALESCE(contact2,'') AS c2, COALESCE(contact3,'') AS c3, COALESCE(address,'') AS addr, COALESCE(branch_code,'') AS bcode FROM branches WHERE id = @id LIMIT 1";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;
        return (rdr.GetInt32(0), rdr.GetString(1), rdr.GetString(2), rdr.GetString(3), rdr.GetString(4), rdr.GetString(5), rdr.GetString(6));
    }

    public async Task UpdateBranchAsync(int id, string name, string? contact1 = null, string? contact2 = null, string? contact3 = null, string? address = null, string? branchCode = null)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "UPDATE branches SET name=@name, contact1=@c1, contact2=@c2, contact3=@c3, address=@addr, branch_code=@bcode WHERE id=@id";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@c1", contact1 ?? string.Empty);
        cmd.Parameters.AddWithValue("@c2", contact2 ?? string.Empty);
        cmd.Parameters.AddWithValue("@c3", contact3 ?? string.Empty);
        cmd.Parameters.AddWithValue("@addr", address ?? string.Empty);
        cmd.Parameters.AddWithValue("@bcode", branchCode ?? string.Empty);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteBranchAsync(int id)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "UPDATE branches SET is_active = 0 WHERE id = @id";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
