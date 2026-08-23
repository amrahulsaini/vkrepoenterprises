using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CRMRSDesktopApp;

/// The signed-in agency's name and logo, cached on disk so every sign-in
/// window can show who is being signed in to before the server has answered.
internal static class AgencyBranding
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CRMRS");

    internal static string LogoPath => Path.Combine(Dir, "agency-logo.png");
    internal static string NamePath => Path.Combine(Dir, "agency-name.txt");

    internal static string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(App.SignedAppUser?.AgencyName))
                return App.SignedAppUser!.AgencyName.Trim();
            try
            {
                if (File.Exists(NamePath))
                {
                    var cached = File.ReadAllText(NamePath).Trim();
                    if (cached.Length > 0) return cached;
                }
            }
            catch { }
            return Branding.IsTenantBuild ? Branding.Name : "CRMRS";
        }
    }

    internal static BitmapImage? LoadLogo()
    {
        try
        {
            if (!File.Exists(LogoPath)) return null;
            using var ms = new MemoryStream(File.ReadAllBytes(LogoPath));
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    internal static BitmapImage? DefaultLogo()
    {
        try
        {
            return new BitmapImage(
                new Uri("pack://application:,,,/public/crmrs-fulllogo.png", UriKind.Absolute));
        }
        catch { return null; }
    }

    internal static async Task SaveAsync(string agencyName, string logoPath)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (!string.IsNullOrWhiteSpace(agencyName))
                await File.WriteAllTextAsync(NamePath, agencyName.Trim());
        }
        catch { }

        if (string.IsNullOrWhiteSpace(logoPath)) return;
        try
        {
            var url = logoPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? logoPath
                : App.ApiBaseUrl.TrimEnd('/') + "/" + logoPath.TrimStart('/');
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            await File.WriteAllBytesAsync(LogoPath, await http.GetByteArrayAsync(url));
        }
        catch { }
    }

    internal static void Clear()
    {
        foreach (var p in new[] { NamePath, LogoPath })
        {
            try { if (File.Exists(p)) File.Delete(p); }
            catch { }
        }
    }
}
