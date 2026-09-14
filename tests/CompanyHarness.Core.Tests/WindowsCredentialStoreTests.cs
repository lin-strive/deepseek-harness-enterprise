using CompanyHarness.Desktop.Core.Storage;

namespace CompanyHarness.Core.Tests;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void RoundTrip_UsesWindowsCredentialManager()
    {
        var target = $"SmartWheelchair.CompanyHarness.Tests.{Guid.NewGuid():N}";
        var secret = $"sk-test-{Guid.NewGuid():N}";
        var store = new WindowsCredentialStore();

        try
        {
            Assert.Null(store.Read(target));

            store.Write(target, secret, "SW-TEST");

            Assert.Equal(secret, store.Read(target));
        }
        finally
        {
            store.Delete(target);
        }

        Assert.Null(store.Read(target));
    }
}
