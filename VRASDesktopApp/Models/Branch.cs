using System.Text.Json.Serialization;

namespace VRASDesktopApp.Models;

public class Branch
{
    [JsonPropertyName("branchId")]
    public int BranchId { get; set; }
    
    [JsonPropertyName("branchName")]
    public string BranchName { get; set; } = string.Empty;
    
    public string BranchCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}
