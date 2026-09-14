using CompanyHarness.Desktop.Core.Runtime;

namespace CompanyHarness.Core.Tests;

public sealed class HarnessRuntimeLocatorTests
{
    [Fact]
    public void Locate_FindsBundledRuntimeBesideApplication()
    {
        var root = Path.Combine(Path.GetTempPath(), $"company-harness-runtime-{Guid.NewGuid():N}");
        var runtime = Path.Combine(root, "runtime");
        var node = Path.Combine(runtime, "node", "node.exe");
        var entry = Path.Combine(runtime, "harness", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        var patch = Path.Combine(runtime, "company.cordis.yml");
        var resolver = Path.Combine(runtime, "company-module-resolver.mjs");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(node)!);
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
            File.WriteAllText(node, string.Empty);
            File.WriteAllText(entry, string.Empty);
            File.WriteAllText(patch, string.Empty);
            File.WriteAllText(resolver, string.Empty);

            var result = HarnessRuntimeLocator.Locate(root);

            Assert.Equal(Path.GetFullPath(runtime), result.RuntimeRoot);
            Assert.Equal(node, result.NodeExecutablePath);
            Assert.Equal(entry, result.HarnessEntryPointPath);
            Assert.Equal(patch, result.CompanyPatchPath);
            Assert.Equal(resolver, result.ModuleResolverPath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Locate_PrefersRuntimeMatchingApplicationVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"company-harness-versioned-runtime-{Guid.NewGuid():N}");
        var legacyRuntime = Path.Combine(root, "runtime");
        var versionedRuntime = Path.Combine(root, "runtime-0.2.6");

        try
        {
            CreateRuntimeLayout(legacyRuntime);
            CreateRuntimeLayout(versionedRuntime);

            var result = HarnessRuntimeLocator.Locate(root, "0.2.6");

            Assert.Equal(Path.GetFullPath(versionedRuntime), result.RuntimeRoot);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void CreateRuntimeLayout(string runtime)
    {
        var node = Path.Combine(runtime, "node", "node.exe");
        var entry = Path.Combine(runtime, "harness", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        var patch = Path.Combine(runtime, "company.cordis.yml");
        var resolver = Path.Combine(runtime, "company-module-resolver.mjs");
        Directory.CreateDirectory(Path.GetDirectoryName(node)!);
        Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
        File.WriteAllText(node, string.Empty);
        File.WriteAllText(entry, string.Empty);
        File.WriteAllText(patch, string.Empty);
        File.WriteAllText(resolver, string.Empty);
    }
}
