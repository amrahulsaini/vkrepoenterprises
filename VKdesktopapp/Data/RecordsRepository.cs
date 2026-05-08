using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;
using VRASDesktopApp.Models;
using System.Diagnostics;

namespace VRASDesktopApp.Data;

public class RecordsRepository
{
    public async Task<List<Branch>> GetAllBranchesAsync()
    {
        var list = new List<Branch>();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = @"
            SELECT b.id, b.name,
                   COALESCE(f.name,'')           AS head_office,
                   COALESCE(b.branch_code,'')    AS branch_code,
                   COALESCE(b.address,'')        AS address
            FROM   branches b
            LEFT JOIN finances f ON f.id = b.finance_id
            WHERE  b.is_active = 1
            ORDER  BY f.name, b.name";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new Branch
            {
                BranchId       = rdr.GetInt32(0).ToString(),
                BranchName     = rdr.GetString(1),
                HeadOfficeName = rdr.GetString(2),
                BranchCode     = rdr.GetString(3),
                Address        = rdr.GetString(4)
            });
        }
        return list;
    }

    // Overwrites all records for the branch using MySqlBulkCopy (LOAD DATA LOCAL INFILE path).
    // progress: (percent 0-100, message) — called at each stage including during chunked bulk copy.
    public async Task<(int Inserted, double ElapsedSeconds)> UploadRecordsAsync(
        int branchId, List<UploadRecord> records,
        IProgress<(int pct, string msg)>? progress = null)
    {
        if (records.Count == 0) return (0, 0);

        var sw = Stopwatch.StartNew();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();

        // 1. Wipe existing records for this branch
        progress?.Report((5, "Clearing old records..."));
        await using (var del = new MySqlCommand("DELETE FROM vehicle_records WHERE branch_id = @bid", conn))
        {
            del.Parameters.AddWithValue("@bid", branchId);
            await del.ExecuteNonQueryAsync();
        }

        // 2. Chunked bulk copy — each 5 000-row chunk updates progress bar
        const int chunkSize = 5_000;
        int inserted = 0;
        for (int offset = 0; offset < records.Count; offset += chunkSize)
        {
            var chunk   = records.GetRange(offset, Math.Min(chunkSize, records.Count - offset));
            var chunkDt = BuildDataTable(branchId, chunk);
            var bc = new MySqlBulkCopy(conn)
            {
                DestinationTableName = "vehicle_records",
                BulkCopyTimeout      = 300
            };
            AddColumnMappings(bc);
            var res = await bc.WriteToServerAsync(chunkDt);
            inserted += (int)res.RowsInserted;

            int pct = 10 + (int)(75.0 * inserted / records.Count);
            progress?.Report((pct, $"Uploading... {inserted:N0} / {records.Count:N0} records"));
        }

        // 3. Populate rc_info / chassis_info for instant vehicle search
        progress?.Report((87, "Rebuilding search index..."));
        try
        {
            await using var rcIns = new MySqlCommand(@"
                INSERT INTO rc_info (vehicle_record_id, rc_number, model, last4)
                SELECT id, vehicle_no, COALESCE(model,''), RIGHT(vehicle_no, 4)
                FROM   vehicle_records
                WHERE  branch_id = @bid AND vehicle_no IS NOT NULL AND vehicle_no != ''", conn);
            rcIns.Parameters.AddWithValue("@bid", branchId);
            await rcIns.ExecuteNonQueryAsync();

            await using var chIns = new MySqlCommand(@"
                INSERT INTO chassis_info (vehicle_record_id, chassis_number, model, last5)
                SELECT id, chassis_no, COALESCE(model,''), RIGHT(chassis_no, 5)
                FROM   vehicle_records
                WHERE  branch_id = @bid AND chassis_no IS NOT NULL AND chassis_no != ''", conn);
            chIns.Parameters.AddWithValue("@bid", branchId);
            await chIns.ExecuteNonQueryAsync();
        }
        catch { /* rc_info/chassis_info tables may not exist yet — upload still succeeds */ }

        // 4. Update branch metadata
        progress?.Report((97, "Saving metadata..."));
        await using var upd = new MySqlCommand(
            "UPDATE branches SET total_records = @cnt, uploaded_at = NOW() WHERE id = @bid", conn);
        upd.Parameters.AddWithValue("@cnt", inserted);
        upd.Parameters.AddWithValue("@bid", branchId);
        await upd.ExecuteNonQueryAsync();

        sw.Stop();
        progress?.Report((100, $"Done — {inserted:N0} records saved"));
        return (inserted, sw.Elapsed.TotalSeconds);
    }

    private static DataTable BuildDataTable(int branchId, List<UploadRecord> records)
    {
        var dt = new DataTable();
        dt.Columns.Add("branch_id",       typeof(int));
        dt.Columns.Add("vehicle_no",      typeof(string));
        dt.Columns.Add("chassis_no",      typeof(string));
        dt.Columns.Add("engine_no",       typeof(string));
        dt.Columns.Add("model",           typeof(string));
        dt.Columns.Add("agreement_no",    typeof(string));
        dt.Columns.Add("bucket",          typeof(string));
        dt.Columns.Add("gv",              typeof(string));
        dt.Columns.Add("od",              typeof(string));
        dt.Columns.Add("seasoning",       typeof(string));
        dt.Columns.Add("tbr_flag",        typeof(string));
        dt.Columns.Add("sec9_available",  typeof(string));
        dt.Columns.Add("sec17_available", typeof(string));
        dt.Columns.Add("customer_name",   typeof(string));
        dt.Columns.Add("customer_address",typeof(string));
        dt.Columns.Add("customer_contact",typeof(string));
        dt.Columns.Add("region",          typeof(string));
        dt.Columns.Add("area",            typeof(string));
        dt.Columns.Add("branch_name_raw", typeof(string));
        dt.Columns.Add("level1",          typeof(string));
        dt.Columns.Add("level1_contact",  typeof(string));
        dt.Columns.Add("level2",          typeof(string));
        dt.Columns.Add("level2_contact",  typeof(string));
        dt.Columns.Add("level3",          typeof(string));
        dt.Columns.Add("level3_contact",  typeof(string));
        dt.Columns.Add("level4",          typeof(string));
        dt.Columns.Add("level4_contact",  typeof(string));
        dt.Columns.Add("sender_mail1",    typeof(string));
        dt.Columns.Add("sender_mail2",    typeof(string));
        dt.Columns.Add("executive_name",  typeof(string));
        dt.Columns.Add("pos",             typeof(string));
        dt.Columns.Add("toss",            typeof(string));
        dt.Columns.Add("remark",          typeof(string));

        foreach (var r in records)
        {
            dt.Rows.Add(
                branchId,
                Trunc(r.FormatedVehicleNo, 50),
                Trunc(r.ChasisNo,          100),
                Trunc(r.EngineNo,          100),
                Trunc(r.Model,             200),
                Trunc(r.AgreementNo,       100),
                Trunc(r.Bucket,             50),
                Trunc(r.GV,                 50),
                Trunc(r.OD,                 50),
                Trunc(r.Seasoning,          50),
                Trunc(r.TBRFlag,            20),
                Trunc(r.Sec9Available,      20),
                Trunc(r.Sec17Available,     20),
                Trunc(r.CustomerName,      200),
                r.CustomerAddress          ?? "",
                Trunc(r.CustomerContactNos,100),
                Trunc(r.Region,            100),
                Trunc(r.Area,              100),
                Trunc(r.BranchName,        200),
                Trunc(r.Level1,            200),
                Trunc(r.Level1ContactNos,  100),
                Trunc(r.Level2,            200),
                Trunc(r.Level2ContactNos,  100),
                Trunc(r.Level3,            200),
                Trunc(r.Level3ContactNos,  100),
                Trunc(r.Level4,            200),
                Trunc(r.Level4ContactNos,  100),
                Trunc(r.SenderMailId1,     200),
                Trunc(r.SenderMailId2,     200),
                Trunc(r.ExecutiveName,     200),
                Trunc(r.POS,               100),
                Trunc(r.TOSS,              100),
                r.Remark                   ?? ""
            );
        }
        return dt;
    }

    private static void AddColumnMappings(MySqlBulkCopy bc)
    {
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(0,  "branch_id"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(1,  "vehicle_no"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(2,  "chassis_no"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(3,  "engine_no"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(4,  "model"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(5,  "agreement_no"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(6,  "bucket"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(7,  "gv"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(8,  "od"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(9,  "seasoning"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(10, "tbr_flag"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(11, "sec9_available"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(12, "sec17_available"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(13, "customer_name"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(14, "customer_address"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(15, "customer_contact"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(16, "region"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(17, "area"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(18, "branch_name_raw"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(19, "level1"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(20, "level1_contact"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(21, "level2"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(22, "level2_contact"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(23, "level3"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(24, "level3_contact"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(25, "level4"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(26, "level4_contact"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(27, "sender_mail1"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(28, "sender_mail2"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(29, "executive_name"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(30, "pos"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(31, "toss"));
        bc.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(32, "remark"));
    }

    private static string Trunc(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length > max ? value[..max] : value;
    }
}
