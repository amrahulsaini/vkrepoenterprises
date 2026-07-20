namespace CRMRSDesktopApp.Models;

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

    public bool   IsAgency   { get; set; }
    public int    AgencyId   { get; set; }
    public string AgencyName { get; set; } = string.Empty;
    public string Slug       { get; set; } = string.Empty;
    public string Email      { get; set; } = string.Empty;
    public string Mobile1    { get; set; } = string.Empty;
    public string Address    { get; set; } = string.Empty;
    public string LogoPath   { get; set; } = string.Empty;

    /// Server-issued token that lets this device resume without the password.
    public string DeviceToken { get; set; } = string.Empty;
}
