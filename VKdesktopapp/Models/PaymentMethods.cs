namespace VRASDesktopApp.Models;

public class PaymentMethods
{
    public int PaymentMethodId { get; set; }
    public string MethodName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedOn { get; set; }
}
