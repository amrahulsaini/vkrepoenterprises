using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Syncfusion.Pdf.Security;

namespace CRMRSDesktopApp.Billing;

internal sealed class TokenSigner : IPdfExternalSigner
{
    private readonly X509Certificate2 _cert;

    public TokenSigner(X509Certificate2 cert) => _cert = cert;

    public string HashAlgorithm => "SHA256";

    public byte[] Sign(byte[] message, out byte[]? timeStampResponse)
    {
        timeStampResponse = null;

        using var rsa = _cert.GetRSAPrivateKey();
        if (rsa != null)
            return rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using var ecdsa = _cert.GetECDsaPrivateKey();
        if (ecdsa != null)
            return ecdsa.SignData(message, HashAlgorithmName.SHA256);

        throw new InvalidOperationException(
            "The selected certificate has no usable private key. Plug in the DSC token and make sure its driver is installed.");
    }
}
