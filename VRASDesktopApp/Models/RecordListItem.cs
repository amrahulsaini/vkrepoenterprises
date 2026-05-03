namespace VRASDesktopApp.Models;

public class RecordListItem
{
    public int RecordId { get; set; }
    public string VehicleNo { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string ChassisNo { get; set; } = string.Empty;
    public string EngineNo { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}
