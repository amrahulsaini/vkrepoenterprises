namespace VRASDesktopApp.Models;

public class AppUserListItem
{
    public int AppUserId { get; set; }
    public string MobileNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedOn { get; set; }
}
