using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CRMRSDesktopApp;

/// Holds only the server-issued device token. The account password is never
/// written to disk. The token is still encrypted at rest because it is a
/// bearer credential, and the server can revoke it at any time.
internal static class SavedSession
{
    private static string Path_ => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CRMRS", "device.dat");

    private static string LegacyPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CRMRS", "session.dat");

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CRMRS.desktop.device.v2");

    public static void Save(string deviceToken)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            var enc = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(deviceToken), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path_, enc);
        }
        catch { }
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(Path_)) return null;
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(Path_), Entropy, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(plain).Trim();
            return string.IsNullOrEmpty(token) ? null : token;
        }
        catch { return null; }
    }

    public static void Clear()
    {
        foreach (var p in new[] { Path_, LegacyPath })
        {
            try { if (File.Exists(p)) File.Delete(p); }
            catch { }
        }
    }

    /// Removes any credentials written by older builds, which stored the
    /// email and password rather than a revocable token.
    public static void PurgeLegacy()
    {
        try { if (File.Exists(LegacyPath)) File.Delete(LegacyPath); }
        catch { }
    }
}
