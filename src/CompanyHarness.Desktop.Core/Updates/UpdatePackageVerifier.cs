using System.Security.Cryptography;

namespace CompanyHarness.Desktop.Core.Updates;

public sealed class UpdatePackageVerifier(RSA publicKey)
{
    public async Task VerifyHashAsync(
        Stream package,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        var expected = Convert.FromHexString(expectedSha256);
        var actual = await SHA256.HashDataAsync(package, cancellationToken);

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new CryptographicException("Update package SHA-256 verification failed.");
        }
    }

    public void VerifyManifestSignature(ReadOnlySpan<byte> manifestBytes, string base64Signature)
    {
        var signature = Convert.FromBase64String(base64Signature);
        if (!publicKey.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
        {
            throw new CryptographicException("Update manifest signature verification failed.");
        }
    }
}
