namespace VRASDesktopApp.Models;

public class RecordDetails
{
    public int RecordId { get; set; }
    public string VehicleNo { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string MobileNo { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ChassisNo { get; set; } = string.Empty;
    public string EngineNo { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public DateTime RegisteredOn { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
}
