using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using VRASDesktopApp.Models;

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

    // Overwrites all records for the branch, then updates branches.total_records + uploaded_at.
    public async Task<int> UploadRecordsAsync(int branchId, List<UploadRecord> records)
    {
        if (records.Count == 0) return 0;

        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            // 1. Wipe existing records for this branch
            await using (var del = new MySqlCommand("DELETE FROM vehicle_records WHERE branch_id = @bid", conn, tx))
            {
                del.Parameters.AddWithValue("@bid", branchId);
                await del.ExecuteNonQueryAsync();
            }

            // 2. Prepared single-row INSERT, re-executed per record (clean + safe)
            const string insertSql = @"
                INSERT INTO vehicle_records
                (branch_id, vehicle_no, chassis_no, engine_no, model,
                 agreement_no, bucket, gv, od, seasoning, tbr_flag, sec9_available, sec17_available,
                 customer_name, customer_address, customer_contact,
                 region, area, branch_name_raw,
                 level1, level1_contact, level2, level2_contact,
                 level3, level3_contact, level4, level4_contact,
                 sender_mail1, sender_mail2, executive_name, pos, toss, remark)
                VALUES
                (@bid,@vn,@cn,@en,@mo,
                 @ag,@bu,@gv,@od,@sea,@tbr,@s9,@s17,
                 @cu,@ca,@cc,
                 @re,@ar,@bn,
                 @l1,@l1c,@l2,@l2c,
                 @l3,@l3c,@l4,@l4c,
                 @sm1,@sm2,@exe,@pos,@tos,@rem)";

            await using var ins = new MySqlCommand(insertSql, conn, tx);
            ins.Parameters.Add("@bid", MySqlDbType.Int32);
            ins.Parameters.Add("@vn",  MySqlDbType.VarChar, 50);
            ins.Parameters.Add("@cn",  MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@en",  MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@mo",  MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@ag",  MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@bu",  MySqlDbType.VarChar, 50);
            ins.Parameters.Add("@gv",  MySqlDbType.VarChar, 50);
            ins.Parameters.Add("@od",  MySqlDbType.VarChar, 50);
            ins.Parameters.Add("@sea", MySqlDbType.VarChar, 50);
            ins.Parameters.Add("@tbr", MySqlDbType.VarChar, 20);
            ins.Parameters.Add("@s9",  MySqlDbType.VarChar, 20);
            ins.Parameters.Add("@s17", MySqlDbType.VarChar, 20);
            ins.Parameters.Add("@cu",  MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@ca",  MySqlDbType.Text);
            ins.Parameters.Add("@cc",  MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@re",  MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@ar",  MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@bn",  MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@l1",  MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@l1c", MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@l2",  MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@l2c", MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@l3",  MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@l3c", MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@l4",  MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@l4c", MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@sm1", MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@sm2", MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@exe", MySqlDbType.VarChar, 200);
            ins.Parameters.Add("@pos", MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@tos", MySqlDbType.VarChar, 100);
            ins.Parameters.Add("@rem", MySqlDbType.Text);
            await ins.PrepareAsync();

            int inserted = 0;
            foreach (var r in records)
            {
                ins.Parameters["@bid"].Value = branchId;
                ins.Parameters["@vn"].Value  = Trunc(r.FormatedVehicleNo, 50);
                ins.Parameters["@cn"].Value  = Trunc(r.ChasisNo, 100);
                ins.Parameters["@en"].Value  = Trunc(r.EngineNo, 100);
                ins.Parameters["@mo"].Value  = Trunc(r.Model, 200);
                ins.Parameters["@ag"].Value  = Trunc(r.AgreementNo, 100);
                ins.Parameters["@bu"].Value  = Trunc(r.Bucket, 50);
                ins.Parameters["@gv"].Value  = Trunc(r.GV, 50);
                ins.Parameters["@od"].Value  = Trunc(r.OD, 50);
                ins.Parameters["@sea"].Value = Trunc(r.Seasoning, 50);
                ins.Parameters["@tbr"].Value = Trunc(r.TBRFlag, 20);
                ins.Parameters["@s9"].Value  = Trunc(r.Sec9Available, 20);
                ins.Parameters["@s17"].Value = Trunc(r.Sec17Available, 20);
                ins.Parameters["@cu"].Value  = Trunc(r.CustomerName, 200);
                ins.Parameters["@ca"].Value  = r.CustomerAddress ?? "";
                ins.Parameters["@cc"].Value  = Trunc(r.CustomerContactNos, 100);
                ins.Parameters["@re"].Value  = Trunc(r.Region, 100);
                ins.Parameters["@ar"].Value  = Trunc(r.Area, 100);
                ins.Parameters["@bn"].Value  = Trunc(r.BranchName, 200);
                ins.Parameters["@l1"].Value  = Trunc(r.Level1, 200);
                ins.Parameters["@l1c"].Value = Trunc(r.Level1ContactNos, 100);
                ins.Parameters["@l2"].Value  = Trunc(r.Level2, 200);
                ins.Parameters["@l2c"].Value = Trunc(r.Level2ContactNos, 100);
                ins.Parameters["@l3"].Value  = Trunc(r.Level3, 200);
                ins.Parameters["@l3c"].Value = Trunc(r.Level3ContactNos, 100);
                ins.Parameters["@l4"].Value  = Trunc(r.Level4, 200);
                ins.Parameters["@l4c"].Value = Trunc(r.Level4ContactNos, 100);
                ins.Parameters["@sm1"].Value = Trunc(r.SenderMailId1, 200);
                ins.Parameters["@sm2"].Value = Trunc(r.SenderMailId2, 200);
                ins.Parameters["@exe"].Value = Trunc(r.ExecutiveName, 200);
                ins.Parameters["@pos"].Value = Trunc(r.POS, 100);
                ins.Parameters["@tos"].Value = Trunc(r.TOSS, 100);
                ins.Parameters["@rem"].Value = r.Remark ?? "";
                await ins.ExecuteNonQueryAsync();
                inserted++;
            }

            // 3. Update branch metadata
            await using var upd = new MySqlCommand(
                "UPDATE branches SET total_records = @cnt, uploaded_at = NOW() WHERE id = @bid", conn, tx);
            upd.Parameters.AddWithValue("@cnt", inserted);
            upd.Parameters.AddWithValue("@bid", branchId);
            await upd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            return inserted;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static string Trunc(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length > max ? value[..max] : value;
    }
}
