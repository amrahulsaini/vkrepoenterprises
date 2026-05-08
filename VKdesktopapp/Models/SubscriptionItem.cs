namespace VRASDesktopApp.Models;

public class SubscriptionItem
{
    public long     Id        { get; set; }
    public string   StartDate { get; set; } = string.Empty;
    public string   EndDate   { get; set; } = string.Empty;
    public decimal  Amount    { get; set; }
    public string?  Notes     { get; set; }
    public DateTime CreatedAt { get; set; }

    public string StartDisplay  => DateTime.TryParse(StartDate, out var d) ? d.ToString("dd MMM yyyy") : StartDate;
    public string EndDisplay    => DateTime.TryParse(EndDate, out var d) ? d.ToString("dd MMM yyyy") : EndDate;
    public string AmountDisplay => $"₹{Amount:N2}";
    public bool   IsActive      => DateTime.TryParse(EndDate, out var d) && d.Date >= DateTime.Today;
}
