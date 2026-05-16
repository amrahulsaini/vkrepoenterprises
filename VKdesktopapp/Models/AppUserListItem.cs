namespace VRASDesktopApp.Models;

public class AppUserListItem
{
    public long     Id         { get; set; }
    public string   Name       { get; set; } = string.Empty;
    public string   Mobile     { get; set; } = string.Empty;
    public string?  Address    { get; set; }
    public string?  Pincode    { get; set; }
    public string?  PfpBase64  { get; set; }
    public string?  DeviceId   { get; set; }
    public bool     IsActive      { get; set; }
    public bool     IsAdmin       { get; set; }
    public bool     IsStopped     { get; set; }
    public bool     IsBlacklisted { get; set; }
    public decimal  Balance    { get; set; }
    public DateTime CreatedAt  { get; set; }
    public string?  SubEndDate { get; set; }

    // Derived display
    public string CreatedDisplay  => CreatedAt.ToString("dd MMM yyyy");
    public string BalanceDisplay  => Balance.ToString("N2");
    public string SubEndDisplay   => SubEndDate is { Length: > 0 } s
        ? DateTime.TryParse(s, out var d) ? d.ToString("dd MMM yyyy") : s
        : "—";
    public bool HasPfp => !string.IsNullOrWhiteSpace(PfpBase64);
    public bool HasNoPfp => !HasPfp;

    // True when PfpBase64 is actually a URL (server now returns absolute URLs
    // for file-stored PFPs). Used by WPF Image.Source which can fetch URLs natively.
    public string? PfpUrl =>
        !string.IsNullOrWhiteSpace(PfpBase64) && PfpBase64.StartsWith("http")
            ? PfpBase64 : null;

    public string Initials => Name.Length > 0
        ? string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(w => char.ToUpper(w[0])))
        : "?";
}
