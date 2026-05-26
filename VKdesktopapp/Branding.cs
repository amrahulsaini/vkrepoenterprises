using System.IO;
using System.Text.Json;

namespace VRASDesktopApp;

/// <summary>
/// Build-time agency identity, baked into each per-tenant installer.
///
/// When <c>tools/build_wpf_all.py</c> produces a per-agency installer it writes
/// <c>Resources/branding.json</c> into the publish output. On startup this
/// class reads that file (if present) so the login screen, taskbar window
/// title, and any other UI that needs the agency name can do so without a
/// network call or a sign-in first.
///
/// For the generic CRMS build (no branding.json shipped), <see cref="Name"/>
/// stays "CRMS" and <see cref="IsTenantBuild"/> is false — the runtime cache
/// fallback in <see cref="LoginWindow"/> kicks in instead.
/// </summary>
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
        Name          = "CRMS";
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
        catch { /* fall back to generic CRMS defaults */ }
    }
}
