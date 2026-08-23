using System.Security.Cryptography;
using System.Text;

namespace VKmobileapi;

public static class FingerprintAuth
{
    public static bool VerifySignature(string publicKeyBase64, string message, string signatureBase64)
    {
        try
        {
            var spki = Convert.FromBase64String(publicKeyBase64.Trim());
            var sig = Convert.FromBase64String(signatureBase64.Trim());
            var data = Encoding.UTF8.GetBytes(message);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out _);

            if (ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                return true;

            return ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch
        {
            return false;
        }
    }

    public static string KeyId(string publicKeyBase64)
    {
        var spki = Convert.FromBase64String(publicKeyBase64.Trim());
        var hash = SHA256.HashData(spki);
        return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }

    public static bool LooksLikeEcPublicKey(string publicKeyBase64)
    {
        try
        {
            var spki = Convert.FromBase64String(publicKeyBase64.Trim());
            if (spki.Length is < 40 or > 400) return false;
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
            return ecdsa.KeySize >= 256;
        }
        catch
        {
            return false;
        }
    }
}
