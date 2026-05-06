using System.Text.Json.Serialization;

namespace VRASDesktopApp.Models;

public class Branch
{
    [JsonPropertyName("branchId")]
    public string BranchId { get; set; } = string.Empty;
    
    [JsonPropertyName("branchName")]
    public string BranchName { get; set; } = string.Empty;
    
    [JsonPropertyName("headOfficeName")]
    public string HeadOfficeName { get; set; } = string.Empty;
    
    public string BranchCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}
