namespace VRASDesktopApp.Models;

public class UploadRecord
{
    public string VehicleNo { get; set; } = string.Empty;
    public string FormatedVehicleNo { get; set; } = string.Empty;
    public string ChasisNo { get; set; } = string.Empty;
    public string ChassisNo
    {
        get => ChasisNo;
        set => ChasisNo = value;
    }
    public string EngineNo { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string AgreementNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string GV { get; set; } = string.Empty;
    public string OD { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string Sec9Available { get; set; } = string.Empty;
    public string Sec17Available { get; set; } = string.Empty;
    public string TBRFlag { get; set; } = string.Empty;
    public string Seasoning { get; set; } = string.Empty;
    public string SenderMailId1 { get; set; } = string.Empty;
    public string SenderMailId2 { get; set; } = string.Empty;
    public string ExecutiveName { get; set; } = string.Empty;
    public string POS { get; set; } = string.Empty;
    public string TOSS { get; set; } = string.Empty;
    public string FinancerName { get; set; } = string.Empty;
    public string CustomerContactNos { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string Level1 { get; set; } = string.Empty;
    public string Level1ContactNos { get; set; } = string.Empty;
    public string Level2 { get; set; } = string.Empty;
    public string Level2ContactNos { get; set; } = string.Empty;
    public string Level3 { get; set; } = string.Empty;
    public string Level3ContactNos { get; set; } = string.Empty;
    public string Level4 { get; set; } = string.Empty;
    public string Level4ContactNos { get; set; } = string.Empty;

    public string OwnerName { get; set; } = string.Empty;
    public string MobileNo { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int BranchId { get; set; }
}
