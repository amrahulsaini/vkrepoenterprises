namespace VRASDesktopApp.Models;

public struct MappedColumns
{
    public short CI_VehicleNo { get; set; }
    public short CI_ChasisNo { get; set; }
    public short CI_Model { get; set; }
    public short CI_EngineNo { get; set; }
    public short CI_AgreementNo { get; set; }
    public short CI_CustomerName { get; set; }
    public short CI_CustomerAddress { get; set; }
    public short CI_Region { get; set; }
    public short CI_Area { get; set; }
    public short CI_Bucket { get; set; }
    public short CI_GV { get; set; }
    public short CI_OD { get; set; }
    public short CI_Branch { get; set; }
    public short CI_Level1 { get; set; }
    public short CI_Level1ContactNo { get; set; }
    public short CI_Level2 { get; set; }
    public short CI_Level2ContactNo { get; set; }
    public short CI_Level3 { get; set; }
    public short CI_Level3ContactNo { get; set; }
    public short CI_Level4 { get; set; }
    public short CI_Level4ContactNo { get; set; }
    public short CI_Sec9Available { get; set; }
    public short CI_Sec17Available { get; set; }
    public short CI_TBRFlag { get; set; }
    public short CI_Seasoning { get; set; }
    public short CI_SenderMailId1 { get; set; }
    public short CI_SenderMailId2 { get; set; }
    public short CI_ExecutiveName { get; set; }
    public short CI_POS { get; set; }
    public short CI_TOSS { get; set; }
    public short CI_CustomerContactNos { get; set; }
    public short CI_Remark { get; set; }

    public void Reset()
    {
        CI_VehicleNo = 0;
        CI_ChasisNo = 0;
        CI_Model = 0;
        CI_EngineNo = 0;
        CI_AgreementNo = 0;
        CI_CustomerName = 0;
        CI_CustomerAddress = 0;
        CI_Region = 0;
        CI_Area = 0;
        CI_Bucket = 0;
        CI_GV = 0;
        CI_OD = 0;
        CI_Branch = 0;
        CI_Level1 = 0;
        CI_Level1ContactNo = 0;
        CI_Level2 = 0;
        CI_Level2ContactNo = 0;
        CI_Level3 = 0;
        CI_Level3ContactNo = 0;
        CI_Level4 = 0;
        CI_Level4ContactNo = 0;
        CI_Sec9Available = 0;
        CI_Sec17Available = 0;
        CI_TBRFlag = 0;
        CI_Seasoning = 0;
        CI_SenderMailId1 = 0;
        CI_SenderMailId2 = 0;
        CI_ExecutiveName = 0;
        CI_POS = 0;
        CI_TOSS = 0;
        CI_CustomerContactNos = 0;
        CI_Remark = 0;
    }
}
