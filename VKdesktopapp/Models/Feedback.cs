namespace VRASDesktopApp.Models;

public class Feedback
{
    public int FeedbackId { get; set; }
    public int AppUserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}
