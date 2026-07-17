using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace CRMRSDesktopApp.Billing;

internal static class SigningCertificates
{
    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CRMRS", "signing-certs.txt");

    private static Dictionary<string, string> ReadMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(ConfigPath)) return map;
            foreach (var line in File.ReadAllLines(ConfigPath))
            {
                var i = line.IndexOf('=');
                if (i > 0) map[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
        }
        catch { }
        return map;
    }

    public static List<X509Certificate2> List()
    {
        var found = new List<X509Certificate2>();
        foreach (var loc in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.My, loc);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (var c in store.Certificates)
                {
                    if (found.Any(x => string.Equals(x.Thumbprint, c.Thumbprint, StringComparison.OrdinalIgnoreCase))) continue;
                    found.Add(c);
                }
            }
            catch { }
        }
        return found
            .OrderBy(c => !CanSign(c))
            .ThenBy(c => c.NotAfter < DateTime.Now)
            .ThenBy(c => DisplayName(c), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool CanSign(X509Certificate2 c)
    {
        try
        {
            if (c.HasPrivateKey) return true;
            using var rsa = c.GetRSAPrivateKey();
            if (rsa != null) return true;
            using var ecdsa = c.GetECDsaPrivateKey();
            return ecdsa != null;
        }
        catch { return false; }
    }

    public static string DisplayName(X509Certificate2 c)
    {
        var n = c.GetNameInfo(X509NameType.SimpleName, false);
        return string.IsNullOrWhiteSpace(n) ? c.Subject : n;
    }

    public static string IssuerName(X509Certificate2 c)
    {
        var n = c.GetNameInfo(X509NameType.SimpleName, true);
        return string.IsNullOrWhiteSpace(n) ? c.Issuer : n;
    }

    public static string? SavedThumbprint(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity)) return null;
        return ReadMap().TryGetValue(identity, out var tp) && !string.IsNullOrWhiteSpace(tp) ? tp : null;
    }

    public static void SaveThumbprint(string identity, string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(identity)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var map = ReadMap();
            if (string.IsNullOrWhiteSpace(thumbprint)) map.Remove(identity);
            else map[identity] = thumbprint;
            File.WriteAllLines(ConfigPath, map.Select(kv => kv.Key + "=" + kv.Value));
        }
        catch { }
    }

    public static List<X509Certificate2> ChainFor(X509Certificate2 cert)
    {
        var chain = new List<X509Certificate2>();
        try
        {
            using var builder = new X509Chain();
            builder.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            builder.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            if (builder.Build(cert))
                foreach (var el in builder.ChainElements)
                    chain.Add(el.Certificate);
        }
        catch { }
        if (chain.Count == 0) chain.Add(cert);
        return chain;
    }

    public static X509Certificate2? Find(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) return null;
        return List().FirstOrDefault(c =>
            string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));
    }

    public static X509Certificate2? Saved(string identity) => Find(SavedThumbprint(identity));

    private static string LayoutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CRMRS", "signing-layout.txt");

    public const float DefaultX = 443f, DefaultY = 20f, DefaultW = 132f, DefaultH = 58f;

    public static (float X, float Y, float W, float H) LoadLayout()
    {
        var v = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(LayoutPath))
                foreach (var line in File.ReadAllLines(LayoutPath))
                {
                    var i = line.IndexOf('=');
                    if (i > 0 && float.TryParse(line.Substring(i + 1).Trim(), out var f))
                        v[line.Substring(0, i).Trim()] = f;
                }
        }
        catch { }
        return (v.TryGetValue("x", out var x) ? x : DefaultX,
                v.TryGetValue("y", out var y) ? y : DefaultY,
                v.TryGetValue("w", out var w) ? w : DefaultW,
                v.TryGetValue("h", out var h) ? h : DefaultH);
    }

    public static void SaveLayout(float x, float y, float w, float h)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath)!);
            File.WriteAllLines(LayoutPath, new[] { $"x={x}", $"y={y}", $"w={w}", $"h={h}" });
        }
        catch { }
    }
}
