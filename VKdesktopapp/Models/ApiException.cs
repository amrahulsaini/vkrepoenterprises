namespace VRASDesktopApp.Models;

public class ApiException
{
    public string Message { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public int Status { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
