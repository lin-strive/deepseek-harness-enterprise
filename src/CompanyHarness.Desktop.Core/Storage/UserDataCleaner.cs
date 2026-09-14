namespace CompanyHarness.Desktop.Core.Storage;

public sealed class UserDataCleaner(
    ISecretStore secretStore,
    string credentialTarget,
    string dataRoot,
    string expectedParentDirectory)
{
    public void Clear()
    {
        var resolvedParent = Path.GetFullPath(expectedParentDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var resolvedDataRoot = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(
                Path.GetDirectoryName(resolvedDataRoot),
                resolvedParent,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("User data path is outside the expected application directory.");
        }

        secretStore.Delete(credentialTarget);
        if (Directory.Exists(resolvedDataRoot))
        {
            Directory.Delete(resolvedDataRoot, recursive: true);
        }
    }
}
