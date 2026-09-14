using System.Security.Cryptography;
using System.Text;
using CompanyHarness.Desktop.Core.Updates;

namespace CompanyHarness.Core.Tests;

public sealed class UpdatePackageVerifierTests
{
    [Fact]
    public async Task VerifyHashAsync_AcceptsExpectedPackageAndRejectsTampering()
    {
        using var rsa = RSA.Create(2048);
        var verifier = new UpdatePackageVerifier(rsa);
        var packageBytes = Encoding.UTF8.GetBytes("approved package");
        var expectedHash = Convert.ToHexString(SHA256.HashData(packageBytes));

        await verifier.VerifyHashAsync(new MemoryStream(packageBytes), expectedHash, CancellationToken.None);

        await Assert.ThrowsAsync<CryptographicException>(() => verifier.VerifyHashAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("tampered package")),
            expectedHash,
            CancellationToken.None));
    }

    [Fact]
    public void VerifyManifestSignature_RejectsChangedManifest()
    {
        using var rsa = RSA.Create(2048);
        var verifier = new UpdatePackageVerifier(rsa);
        var approved = Encoding.UTF8.GetBytes("{\"version\":\"1.0.0\"}");
        var changed = Encoding.UTF8.GetBytes("{\"version\":\"9.9.9\"}");
        var signature = Convert.ToBase64String(rsa.SignData(
            approved,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss));

        verifier.VerifyManifestSignature(approved, signature);
        Assert.Throws<CryptographicException>(() => verifier.VerifyManifestSignature(changed, signature));
    }
}
