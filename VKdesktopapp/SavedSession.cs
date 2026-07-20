using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CRMRSDesktopApp;

internal static class SavedSession
{
    private static string Path_ => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CRMRS", "session.dat");

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CRMRS.desktop.session.v1");

    public static bool Exists => File.Exists(Path_);

    public static void Save(string email, string password)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            var plain = Encoding.UTF8.GetBytes(email + "\n" + password);
            var enc = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path_, enc);
        }
        catch { }
    }

    public static (string Email, string Password)? Load()
    {
        try
        {
            if (!File.Exists(Path_)) return null;
            var enc = File.ReadAllBytes(Path_);
            var plain = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
            var parts = Encoding.UTF8.GetString(plain).Split('\n');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0])) return null;
            return (parts[0], parts[1]);
        }
        catch { return null; }
    }

    public static void Clear()
    {
        try { if (File.Exists(Path_)) File.Delete(Path_); }
        catch { }
    }
}
