namespace VRASDesktopApp.Models;

public class UploadRecord
{
    public string VehicleNo { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string MobileNo { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ChassisNo { get; set; } = string.Empty;
    public string EngineNo { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int BranchId { get; set; }
}
