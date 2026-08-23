namespace VKApiServer;

public static class Modules
{
    public sealed record Item(string Key, string Label, string Group);

    public static readonly Item[] All =
    {
        new("Home",          "Home",            "Overview"),
        new("Search",        "Find Vehicles",   "Records"),
        new("UploadRecords", "Upload Records",  "Records"),
        new("DetailsViews",  "Details Views",   "Records"),
        new("DirectData",    "Direct Data",     "Records"),
        new("Blacklist",     "Blacklist",       "Records"),
        new("Finances",      "Finances",        "Records"),

        new("Users",         "Users",           "People"),
        new("IdCards",       "Id Cards",        "People"),
        new("RepoKits",      "Repo Kits",       "People"),
        new("Hrms",          "HRMS",            "People"),

        new("Confirmations", "Confirmations",   "Operations"),
        new("Couriers",      "Couriers",        "Operations"),
        new("Allocations",   "Allocations",     "Operations"),

        new("Billing",       "Billing",         "Finance"),
        new("Accounts",      "Accounts",        "Finance"),
        new("Reports",       "Reports",         "Finance"),

        new("Messages",      "Statements",      "System"),
        new("Support",       "Support Tickets", "System"),
        new("Settings",      "Settings",        "System"),
    };

    private static readonly HashSet<string> Valid =
        new(All.Select(m => m.Key), StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(string key) => Valid.Contains(key);

    /// Drops anything not in the catalogue, de-duplicates, and keeps catalogue
    /// order so a stored role never carries a key the desktop cannot honour.
    public static string Normalise(IEnumerable<string>? keys)
    {
        if (keys is null) return "";
        var picked = new HashSet<string>(keys.Where(IsValid), StringComparer.OrdinalIgnoreCase);
        return string.Join(",", All.Where(m => picked.Contains(m.Key)).Select(m => m.Key));
    }

    public static string[] Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Where(IsValid).ToArray();

    public static string[] Effective(bool isSuperadmin, string? csv) =>
        isSuperadmin ? All.Select(m => m.Key).ToArray() : Split(csv);
}
