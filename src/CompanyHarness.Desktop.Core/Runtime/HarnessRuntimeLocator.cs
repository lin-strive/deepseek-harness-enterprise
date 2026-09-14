namespace CompanyHarness.Desktop.Core.Runtime;

public sealed record HarnessRuntimeLayout(
    string RuntimeRoot,
    string NodeExecutablePath,
    string HarnessEntryPointPath,
    string CompanyPatchPath,
    string ModuleResolverPath);

public static class HarnessRuntimeLocator
{
    public static HarnessRuntimeLayout Locate(
        string applicationBaseDirectory,
        string? applicationVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);

        var baseDirectory = Path.GetFullPath(applicationBaseDirectory);
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(applicationVersion))
        {
            candidates.Add(Path.Combine(baseDirectory, $"runtime-{applicationVersion}"));
        }

        candidates.Add(Path.Combine(baseDirectory, "runtime"));

        var cursor = new DirectoryInfo(baseDirectory);
        while (cursor is not null)
        {
            candidates.Add(Path.Combine(cursor.FullName, "artifacts", "windows-harness-runtime"));
            cursor = cursor.Parent;
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var layout = CreateLayout(candidate);
            if (File.Exists(layout.NodeExecutablePath)
                && File.Exists(layout.HarnessEntryPointPath)
                && File.Exists(layout.CompanyPatchPath)
                && File.Exists(layout.ModuleResolverPath))
            {
                return layout;
            }
        }

        throw new DirectoryNotFoundException(
            "未找到 Harness Runtime。请重新安装超智能 Harness，或先运行 scripts/prepare-harness-runtime.ps1。");
    }

    private static HarnessRuntimeLayout CreateLayout(string runtimeRoot)
    {
        var fullRoot = Path.GetFullPath(runtimeRoot);
        return new HarnessRuntimeLayout(
            fullRoot,
            Path.Combine(fullRoot, "node", "node.exe"),
            Path.Combine(fullRoot, "harness", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"),
            Path.Combine(fullRoot, "company.cordis.yml"),
            Path.Combine(fullRoot, "company-module-resolver.mjs"));
    }
}
