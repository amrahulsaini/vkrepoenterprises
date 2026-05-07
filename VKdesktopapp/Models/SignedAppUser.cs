namespace VRASDesktopApp.Models;

public class SignedAppUser
{
    public int AppUserId { get; set; }
    public string MobileNo { get; set; } = string.Empty;
    public string MDeviceId { get; set; } = string.Empty;
    public string DDeviceId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public bool StrictGPS { get; set; }
    public bool SearchSelectedFinances { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}
