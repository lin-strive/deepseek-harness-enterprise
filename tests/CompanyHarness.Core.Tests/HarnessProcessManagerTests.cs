using System.Diagnostics;
using System.Net;
using CompanyHarness.Desktop.Core.Runtime;

namespace CompanyHarness.Core.Tests;

public sealed class HarnessProcessManagerTests
{
    [Fact]
    public void CreateStartInfo_ForcesLoopbackCompanyGatewayAndTelemetryOff()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test process path unavailable.");
        var patch = Path.GetTempFileName();
        var options = new HarnessLaunchOptions(
            executable,
            null,
            patch,
            Path.GetTempPath(),
            new Uri("https://gateway.example.internal/v1"),
            "virtual-key",
            "company-coder",
            TimeSpan.FromSeconds(10));

        try
        {
            var result = HarnessProcessManager.CreateStartInfo(options, 32123);

            Assert.Contains("web", result.ArgumentList);
            Assert.Contains("--patch", result.ArgumentList);
            Assert.Contains(patch, result.ArgumentList);
            Assert.Contains("--no-open", result.ArgumentList);
            Assert.Contains("127.0.0.1", result.ArgumentList);
            Assert.Contains("32123", result.ArgumentList);
            Assert.Equal("https://gateway.example.internal/v1", result.Environment["DEEPSEEK_BASE_URL"]);
            Assert.Equal("https://gateway.example.internal/v1", result.Environment["DEEPSEEK_SEARCH_BASE_URL"]);
            Assert.Equal("virtual-key", result.Environment["DEEPSEEK_API_KEY"]);
            Assert.Equal("company-coder", result.Environment["DSH_MODEL"]);
            Assert.Equal("1", result.Environment["DSH_TELEMETRY_DISABLED"]);
            Assert.Equal("DISABLED", result.Environment["DSH_TELEMETRY_MODE"]);
            Assert.False(result.UseShellExecute);
            Assert.True(result.CreateNoWindow);
        }
        finally
        {
            File.Delete(patch);
        }
    }

    [Fact]
    public void Validate_AllowsTemporaryHttpTestGateway()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test process path unavailable.");
        var patch = Path.GetTempFileName();
        var options = new HarnessLaunchOptions(
            executable,
            null,
            patch,
            Path.GetTempPath(),
            new Uri("http://gateway.example.internal/v1"),
            "virtual-key",
            "company-coder",
            TimeSpan.FromSeconds(10));

        try
        {
            options.Validate();
        }
        finally
        {
            File.Delete(patch);
        }
    }

    [Fact]
    public void CreateStartInfo_RemovesInheritedPersonalProviderCredentials()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test process path unavailable.");
        var patch = Path.GetTempFileName();
        var previous = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "personal-key");

        try
        {
            var options = new HarnessLaunchOptions(
                executable,
                null,
                patch,
                Path.GetTempPath(),
                new Uri("https://gateway.example.internal/v1"),
                "virtual-key",
                "company-fast",
                TimeSpan.FromSeconds(10));

            var result = HarnessProcessManager.CreateStartInfo(options, 32123);

            Assert.False(result.Environment.ContainsKey("OPENAI_API_KEY"));
            Assert.Equal("virtual-key", result.Environment["DEEPSEEK_API_KEY"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previous);
            File.Delete(patch);
        }
    }

    [Fact]
    public void CreateStartInfo_RegistersFixedRuntimeModuleResolverBeforeHarnessScript()
    {
        var root = Path.Combine(Path.GetTempPath(), $"company-harness-bridge-{Guid.NewGuid():N}");
        var modules = Path.Combine(root, "runtime", "harness", "node_modules");
        var script = Path.Combine(modules, "@deepseek-ai", "dsh", "lib", "bin.js");
        var resolver = Path.Combine(root, "runtime", "company-module-resolver.mjs");
        var patch = Path.Combine(root, "company.cordis.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, string.Empty);
        File.WriteAllText(resolver, string.Empty);
        File.WriteAllText(patch, string.Empty);
        var options = new HarnessLaunchOptions(
            Environment.ProcessPath ?? throw new InvalidOperationException("Test process path unavailable."),
            script,
            patch,
            Path.Combine(root, "data"),
            new Uri("https://gateway.example.internal/v1"),
            "virtual-key",
            "company-coder",
            TimeSpan.FromSeconds(10));

        try
        {
            var result = HarnessProcessManager.CreateStartInfo(options, 32123);

            Assert.Equal("--import", result.ArgumentList[0]);
            Assert.Equal(new Uri(resolver).AbsoluteUri, result.ArgumentList[1]);
            Assert.Equal(script, result.ArgumentList[2]);
            Assert.Equal(modules, result.Environment["COMPANY_HARNESS_RUNTIME_MODULES"]);
            Assert.Equal("virtual-key", result.Environment["DEEPSEEK_API_KEY"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validate_RejectsInvalidModelId()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test process path unavailable.");
        var patch = Path.GetTempFileName();
        var options = new HarnessLaunchOptions(
            executable,
            null,
            patch,
            Path.GetTempPath(),
            new Uri("https://gateway.example.internal/v1"),
            "virtual-key",
            "DeepSeek invalid model!",
            TimeSpan.FromSeconds(10));

        try
        {
            Assert.Throws<ArgumentException>(options.Validate);
        }
        finally
        {
            File.Delete(patch);
        }
    }

    [Fact]
    public async Task WaitUntilReady_RetriesAfterSingleRequestTimeout()
    {
        var handler = new TimeoutThenSuccessHandler();
        using var httpClient = new HttpClient(handler);
        await using var manager = new HarnessProcessManager(httpClient);
        using var process = Process.GetCurrentProcess();

        await manager.WaitUntilReadyAsync(
            process,
            new Uri("http://127.0.0.1:32123/"),
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class TimeoutThenSuccessHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                throw new TaskCanceledException("Synthetic per-request timeout.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
