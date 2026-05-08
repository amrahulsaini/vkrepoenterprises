using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using VRASDesktopApp.Models;

namespace VRASDesktopApp.Data;

public class VehicleSearchRepository
{
    private const string SelectFields = @"
        vr.id, vr.vehicle_no, vr.chassis_no, vr.engine_no, vr.model,
        vr.agreement_no, vr.bucket, vr.gv, vr.od, vr.seasoning, vr.tbr_flag,
        vr.sec9_available, vr.sec17_available, vr.customer_name, vr.customer_address, vr.customer_contact,
        vr.region, vr.area, vr.branch_name_raw,
        vr.level1, vr.level1_contact, vr.level2, vr.level2_contact,
        vr.level3, vr.level3_contact, vr.level4, vr.level4_contact,
        vr.sender_mail1, vr.sender_mail2, vr.executive_name, vr.pos, vr.toss, vr.remark,
        COALESCE(DATE_FORMAT(vr.created_at,'%d %b %Y'),'') AS created_on,
        b.name AS branch_name,
        COALESCE(f.name,'') AS financer,
        COALESCE(b.contact1,'') AS b_c1,
        COALESCE(b.contact2,'') AS b_c2,
        COALESCE(b.contact3,'') AS b_c3,
        COALESCE(b.address,'') AS b_addr";

    public async Task<List<VehicleSearchItem>> SearchByRcLast4Async(string last4, CancellationToken ct = default)
    {
        var sql = $@"
            SELECT {SelectFields}
            FROM rc_info ri
            INNER JOIN vehicle_records vr ON vr.id = ri.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE ri.last4 = @q
            ORDER BY b.name, vr.vehicle_no
            LIMIT 500";

        return await ExecuteSearchAsync(sql, last4, ct);
    }

    public async Task<List<VehicleSearchItem>> SearchByChassisLast5Async(string last5, CancellationToken ct = default)
    {
        var sql = $@"
            SELECT {SelectFields}
            FROM chassis_info ci
            INNER JOIN vehicle_records vr ON vr.id = ci.vehicle_record_id
            INNER JOIN branches b ON b.id = vr.branch_id
            LEFT  JOIN finances f ON f.id = b.finance_id
            WHERE ci.last5 = @q
            ORDER BY b.name, vr.chassis_no
            LIMIT 500";

        return await ExecuteSearchAsync(sql, last5, ct);
    }

    private static async Task<List<VehicleSearchItem>> ExecuteSearchAsync(string sql, string query, CancellationToken ct)
    {
        var list = new List<VehicleSearchItem>();
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@q", query.ToUpper());
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
            list.Add(MapRow(rdr));
        return list;
    }

    public async Task DeleteRecordAsync(long id)
    {
        await using var conn = MySqlFactory.CreateConnection();
        await conn.OpenAsync();
        const string sql = "DELETE FROM vehicle_records WHERE id = @id";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static VehicleSearchItem MapRow(MySqlDataReader r)
    {
        string S(int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);
        return new VehicleSearchItem
        {
            Id                  = r.GetInt64(0).ToString(),
            VehicleNo           = S(1),
            ChassisNo           = S(2),
            EngineNo            = S(3),
            Model               = S(4),
            AgreementNo         = S(5),
            Bucket              = S(6),
            GV                  = S(7),
            OD                  = S(8),
            Seasoning           = S(9),
            TBRFlag             = S(10),
            Sec9Available       = S(11),
            Sec17Available      = S(12),
            CustomerName        = S(13),
            CustomerAddress     = S(14),
            CustomerContactNos  = S(15),
            Region              = S(16),
            Area                = S(17),
            BranchFromExcel     = S(18),
            Level1              = S(19),
            Level1ContactNos    = S(20),
            Level2              = S(21),
            Level2ContactNos    = S(22),
            Level3              = S(23),
            Level3ContactNos    = S(24),
            Level4              = S(25),
            Level4ContactNos    = S(26),
            SenderMailId1       = S(27),
            SenderMailId2       = S(28),
            ExecutiveName       = S(29),
            POS                 = S(30),
            TOSS                = S(31),
            Remark              = S(32),
            CreatedOn           = S(33),
            BranchName          = S(34),
            Financer            = S(35),
            FirstContactDetails = S(36),
            SecondContactDetails= S(37),
            ThirdContactDetails = S(38),
            Address             = S(39),
        };
    }
}
