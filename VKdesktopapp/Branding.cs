using System.IO;
using System.Text.Json;

namespace VRASDesktopApp;

public static class Branding
{
    public static readonly bool   IsTenantBuild;
    public static readonly string Name;
    public static readonly string Slug;
    public static readonly string Mobile;
    public static readonly string Address;
    public static readonly string PrimaryColor;

    static Branding()
    {
        Name          = "CRMRS";
        Slug          = string.Empty;
        Mobile        = string.Empty;
        Address       = string.Empty;
        PrimaryColor  = "#FF6B35";
        IsTenantBuild = false;

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "branding.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            string? S(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                                       ? v.GetString() : null;
            var name    = S("name");
            var slug    = S("slug");
            var mobile  = S("mobile");
            var address = S("address");
            var color   = S("primaryColor");
            if (!string.IsNullOrWhiteSpace(name))    Name         = name!;
            if (!string.IsNullOrWhiteSpace(slug))    Slug         = slug!;
            if (!string.IsNullOrWhiteSpace(mobile))  Mobile       = mobile!;
            if (!string.IsNullOrWhiteSpace(address)) Address      = address!;
            if (!string.IsNullOrWhiteSpace(color))   PrimaryColor = color!;
            IsTenantBuild = !string.IsNullOrWhiteSpace(slug);
        }
        catch { }
    }
}
