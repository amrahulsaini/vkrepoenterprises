namespace VRASDesktopApp.Models;

public class Confirmation
{
    public int ConfirmationId { get; set; }
    public int RecordId { get; set; }
    public int AppUserId { get; set; }
    public string VehicleNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
}
