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
        var sql = @"
            SELECT b.id,
                   b.name,
                   COALESCE(b.contact1,'') AS contact1,
                   COALESCE(b.contact2,'') AS contact2,
                   COALESCE(b.contact3,'') AS contact3,
                   COALESCE(b.address,'')  AS address,
                   b.total_records         AS live_count,
                   IFNULL(DATE_FORMAT(b.uploaded_at,'%d %b %y %h:%i %p'),'') AS uploaded_at
            FROM branches b
            WHERE b.finance_id = @fid AND b.is_active = 1
            ORDER BY b.total_records DESC
            LIMIT 100";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 120 };
        cmd.Parameters.AddWithValue("@fid", financeId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add((
                rdr.GetInt32(0),
                rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1),
                rdr.GetString(2),
                rdr.GetString(3),
                rdr.GetString(4),
                rdr.GetString(5),
                rdr.GetInt64(6),
                rdr.GetString(7)
            ));
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

    public async Task DeleteBranchAsync(int id, IProgress<string>? progress = null)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();

        static async Task Exec(string sql, MySqlConnection c, int timeout = 30,
            params (string n, object v)[] ps)
        {
            await using var cmd = new MySqlCommand(sql, c) { CommandTimeout = timeout };
            foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
            await cmd.ExecuteNonQueryAsync();
        }

        // Chunked record purge first — same pattern as ClearBranchRecordsAsync.
        // Deleting the branch directly triggers a cascade on 100k+ rows in one
        // transaction which bloats the InnoDB undo log and causes a timeout.
        await Exec("SET foreign_key_checks=0", conn);
        int totalDeleted = 0;
        while (true)
        {
            var ids = new List<long>(5000);
            await using (var sel = new MySqlCommand(
                "SELECT id FROM vehicle_records WHERE branch_id = @bid LIMIT 5000", conn)
                { CommandTimeout = 30 })
            {
                sel.Parameters.AddWithValue("@bid", id);
                await using var rdr = await sel.ExecuteReaderAsync();
                while (await rdr.ReadAsync()) ids.Add(rdr.GetInt64(0));
            }
            if (ids.Count == 0) break;
            var idList = string.Join(",", ids);
            await Exec($"DELETE FROM rc_info      WHERE vehicle_record_id IN ({idList})", conn, 60);
            await Exec($"DELETE FROM chassis_info WHERE vehicle_record_id IN ({idList})", conn, 60);
            await Exec($"DELETE FROM vehicle_records WHERE id IN ({idList})", conn, 60);
            totalDeleted += ids.Count;
            progress?.Report($"Deleting… {totalDeleted:N0} records removed");
        }
        await Exec("SET foreign_key_checks=1", conn);

        await Exec("DELETE FROM branches WHERE id = @id", conn, 30, ("@id", id));
    }

    public async Task<List<(int Id, string Name, string FinanceName, long TotalRecords, string UploadedAt)>> GetAllBranchesWithFinanceAsync()
    {
        var list = new List<(int, string, string, long, string)>();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            SELECT b.id, b.name,
                   COALESCE(f.name,'')  AS finance_name,
                   b.total_records,
                   IFNULL(DATE_FORMAT(b.uploaded_at,'%d %b %y %h:%i %p'),'') AS uploaded_at
            FROM   branches b
            LEFT JOIN finances f ON f.id = b.finance_id
            WHERE  b.is_active = 1
            ORDER  BY f.name, b.name
            LIMIT  500";
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 60 };
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add((
                rdr.GetInt32(0),
                rdr.GetString(1),
                rdr.GetString(2),
                rdr.IsDBNull(3) ? 0 : rdr.GetInt64(3),
                rdr.GetString(4)
            ));
        }
        return list;
    }
}
