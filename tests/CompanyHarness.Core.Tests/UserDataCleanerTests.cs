using CompanyHarness.Desktop.Core.Storage;

namespace CompanyHarness.Core.Tests;

public sealed class UserDataCleanerTests
{
    [Fact]
    public void ClearDeletesOnlyValidatedDataRootAndCredential()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"company-harness-cleaner-{Guid.NewGuid():N}");
        var dataRoot = Path.Combine(parent, "CompanyHarness");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "profile.json"), "test");
        var secrets = new RecordingSecretStore();

        new UserDataCleaner(secrets, "test-target", dataRoot, parent).Clear();

        Assert.False(Directory.Exists(dataRoot));
        Assert.Equal("test-target", secrets.DeletedTarget);
        Directory.Delete(parent);
    }

    [Fact]
    public void ClearRejectsDirectoryOutsideExpectedParent()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"company-harness-cleaner-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"company-harness-outside-{Guid.NewGuid():N}");
        var secrets = new RecordingSecretStore();

        Assert.Throws<InvalidOperationException>(() =>
            new UserDataCleaner(secrets, "test-target", outside, parent).Clear());
        Assert.Null(secrets.DeletedTarget);
    }

    private sealed class RecordingSecretStore : ISecretStore
    {
        public string? DeletedTarget { get; private set; }

        public void Write(string target, string secret, string userName)
        {
        }

        public string? Read(string target) => null;

        public void Delete(string target)
        {
            DeletedTarget = target;
        }
    }
}
