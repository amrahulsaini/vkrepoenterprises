using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;

namespace VRASDesktopApp.Data;

public class ExportRepository
{
    private const string SelectCols = @"
        COALESCE(f.name,'')  AS FinanceName,
        b.name               AS BranchName,
        vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model,
        vr.agreement_no, vr.customer_name, vr.customer_contact, vr.customer_address,
        vr.region, vr.area, vr.bucket, vr.gv, vr.od, vr.seasoning,
        vr.tbr_flag, vr.sec9_available, vr.sec17_available,
        vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
        vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
        vr.sender_mail1, vr.sender_mail2, vr.executive_name,
        vr.pos, vr.toss, vr.remark, vr.branch_name_raw";

    public static readonly string[] Headers =
    {
        "Finance Name", "Branch Name",
        "Vehicle No", "Chassis No", "Engine No", "Model",
        "Agreement No", "Customer Name", "Customer Contact", "Customer Address",
        "Region", "Area", "Bucket", "GV", "OD", "Seasoning",
        "TBR Flag", "Sec9 Available", "Sec17 Available",
        "Level1", "Level1 Contact", "Level2", "Level2 Contact",
        "Level3", "Level3 Contact", "Level4", "Level4 Contact",
        "Sender Mail 1", "Sender Mail 2", "Executive Name",
        "POS", "TOSS", "Remark", "Branch (from file)"
    };

    public async Task<DataTable> GetBranchRecordsAsync(int branchId)
    {
        var sql = $@"SELECT {SelectCols}
            FROM vehicle_records vr
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE vr.branch_id = @bid
            ORDER BY vr.vehicle_no";
        return await ExecuteAsync(sql, ("@bid", branchId));
    }

    public async Task<DataTable> GetFinanceRecordsAsync(int financeId)
    {
        var sql = $@"SELECT {SelectCols}
            FROM vehicle_records vr
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE b.finance_id = @fid
            ORDER BY b.name, vr.vehicle_no";
        return await ExecuteAsync(sql, ("@fid", financeId));
    }

    public async Task ClearBranchRecordsAsync(int branchId)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        await using var del = new MySqlCommand(
            "DELETE FROM vehicle_records WHERE branch_id = @bid", conn);
        del.Parameters.AddWithValue("@bid", branchId);
        await del.ExecuteNonQueryAsync();
        await using var upd = new MySqlCommand(
            "UPDATE branches SET total_records = 0, uploaded_at = NULL WHERE id = @bid", conn);
        upd.Parameters.AddWithValue("@bid", branchId);
        await upd.ExecuteNonQueryAsync();
    }

    private static async Task<DataTable> ExecuteAsync(string sql, (string name, object value) param)
    {
        var dt = new DataTable();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(param.name, param.value);
        await using var rdr = await cmd.ExecuteReaderAsync();
        dt.Load(rdr);
        return dt;
    }
}
