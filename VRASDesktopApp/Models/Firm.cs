using System;

namespace VRASDesktopApp.Models;

public class Firm
{
    public int FirmId { get; set; }
    public string FirmName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ContactNos { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
    public int BillingTypeId { get; set; }
    public decimal BillingAmount { get; set; }
    public int FeedbackPortalFirmId { get; set; }
    public bool AppDeactivated { get; set; }
}
