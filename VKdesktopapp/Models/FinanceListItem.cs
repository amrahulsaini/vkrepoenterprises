namespace VRASDesktopApp.Models;

public class FinanceListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long BranchCount { get; set; }
    public long TotalRecords { get; set; }
}
