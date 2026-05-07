namespace VRASDesktopApp.Models;

public class Finance
{
    public int FinanceId { get; set; }
    public int RecordId { get; set; }
    public string VehicleNo { get; set; } = string.Empty;
    public string FinanceType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime FinanceDate { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
}
