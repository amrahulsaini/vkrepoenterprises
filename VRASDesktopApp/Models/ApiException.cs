namespace VRASDesktopApp.Models;

public class ApiException
{
    public string Message { get; set; } = string.Empty;
    public int StatusCode { get; set; }
}
