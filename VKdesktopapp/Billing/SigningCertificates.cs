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
        "CRMRS", "signing-cert.txt");

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

    public static string? SavedThumbprint()
    {
        try { return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath).Trim() : null; }
        catch { return null; }
    }

    public static void SaveThumbprint(string? thumbprint)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
            }
            else File.WriteAllText(ConfigPath, thumbprint);
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

    public static X509Certificate2? Saved() => Find(SavedThumbprint());
}
