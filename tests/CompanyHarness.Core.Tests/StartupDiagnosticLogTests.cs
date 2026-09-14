using CompanyHarness.Desktop.Core.Runtime;

namespace CompanyHarness.Core.Tests;

public sealed class StartupDiagnosticLogTests
{
    [Fact]
    public async Task WriteFailure_CreatesSupportRecordWithoutApplicationContent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"company-harness-log-{Guid.NewGuid():N}");
        var log = new StartupDiagnosticLog(root);
        var exception = new HarnessStartupException(7, ["safe startup line"]);

        try
        {
            var result = await log.WriteFailureAsync(
                "0.2.3",
                exception,
                exception.RecentOutput,
                CancellationToken.None);
            var contents = await File.ReadAllTextAsync(result.LogPath);

            Assert.Equal(10, result.SupportId.Length);
            Assert.Contains($"supportId: {result.SupportId}", contents);
            Assert.Contains("harnessExitCode: 7", contents);
            Assert.Contains("safe startup line", contents);
            Assert.DoesNotContain("prompt", contents, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
