namespace VRASDesktopApp;

public class FileDownloadInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = "Pending";
    public int Progress { get; set; }
}
