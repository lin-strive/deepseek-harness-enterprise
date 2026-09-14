using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace CompanyHarness.Desktop.Core.Runtime;

public sealed class HarnessProcessManager(HttpClient healthClient) : IAsyncDisposable
{
    private const int MaximumCapturedLines = 24;
    private const int MaximumCapturedLineLength = 600;
    private static readonly Regex KeyPattern = new(
        "(?i)(?:sk|key)-[a-z0-9_-]{8,}",
        RegexOptions.CultureInvariant);
    private static readonly string[] PersonalProviderEnvironmentVariables =
    [
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_BASE_URL",
        "AWS_ACCESS_KEY_ID",
        "AWS_SECRET_ACCESS_KEY",
        "AWS_SESSION_TOKEN",
        "AZURE_OPENAI_API_KEY",
        "AZURE_OPENAI_ENDPOINT",
        "GEMINI_API_KEY",
        "GOOGLE_API_KEY",
        "OPENAI_API_KEY",
        "OPENAI_BASE_URL",
    ];

    private Process? _process;
    private readonly object _outputLock = new();
    private readonly Queue<string> _recentStartupOutput = new();
    private volatile bool _captureStartupOutput;

    public HarnessRuntimeState State { get; private set; } = HarnessRuntimeState.Stopped;

    public Uri? LocalUri { get; private set; }

    public async Task<Uri> StartAsync(HarnessLaunchOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        if (_process is { HasExited: false })
        {
            return LocalUri ?? throw new InvalidOperationException("Harness process is running without a local URI.");
        }

        State = HarnessRuntimeState.Starting;
        ClearStartupOutput();
        _captureStartupOutput = true;
        Directory.CreateDirectory(options.DataDirectory);
        if (options.ScriptPath is not null)
        {
            EnsureRuntimeModuleIsReadable(options.ScriptPath);
        }
        var port = ReserveLoopbackPort();
        LocalUri = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);

        var startInfo = CreateStartInfo(options, port);
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Exited += (_, _) => State = HarnessRuntimeState.Stopped;
        _process.OutputDataReceived += (_, args) => CaptureStartupOutput(args.Data, options.VirtualKey);
        _process.ErrorDataReceived += (_, args) => CaptureStartupOutput(args.Data, options.VirtualKey);

        if (!_process.Start())
        {
            State = HarnessRuntimeState.Failed;
            throw new InvalidOperationException("Harness process could not be started.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        try
        {
            await WaitUntilReadyAsync(_process, LocalUri, options.StartupTimeout, cancellationToken);
            _captureStartupOutput = false;
            State = HarnessRuntimeState.Running;
            return LocalUri;
        }
        catch
        {
            await StopAsync(CancellationToken.None);
            _captureStartupOutput = false;
            State = HarnessRuntimeState.Failed;
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _captureStartupOutput = false;
        var process = _process;
        _process = null;
        LocalUri = null;

        if (process is null)
        {
            State = HarnessRuntimeState.Stopped;
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                }
            }
        }
        finally
        {
            process.Dispose();
            State = HarnessRuntimeState.Stopped;
        }
    }

    public void StopImmediately()
    {
        _captureStartupOutput = false;
        var process = _process;
        _process = null;
        LocalUri = null;
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(milliseconds: 2_000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and termination.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Windows is already tearing down the process tree; window shutdown must continue.
        }
        finally
        {
            process?.Dispose();
            State = HarnessRuntimeState.Stopped;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    public IReadOnlyList<string> GetRecentStartupOutput()
    {
        lock (_outputLock)
        {
            return _recentStartupOutput.ToArray();
        }
    }

    internal static ProcessStartInfo CreateStartInfo(HarnessLaunchOptions options, int port)
    {
        var startInfo = CreateBaseStartInfo(options);

        if (options.ScriptPath is not null)
        {
            var runtimeModules = FindRuntimeNodeModules(options.ScriptPath);
            var resolverPath = FindRuntimeModuleResolver(runtimeModules);
            startInfo.Environment["COMPANY_HARNESS_RUNTIME_MODULES"] = runtimeModules;
            startInfo.ArgumentList.Add("--import");
            startInfo.ArgumentList.Add(new Uri(resolverPath).AbsoluteUri);
            startInfo.ArgumentList.Add(options.ScriptPath);
        }

        startInfo.ArgumentList.Add("web");
        startInfo.ArgumentList.Add("--patch");
        startInfo.ArgumentList.Add(options.CompanyPatchPath);
        startInfo.ArgumentList.Add("--no-open");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return startInfo;
    }

    private static ProcessStartInfo CreateBaseStartInfo(HarnessLaunchOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = options.DataDirectory,
        };

        foreach (var variable in PersonalProviderEnvironmentVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        var gatewayBaseUrl = options.GatewayBaseUrl.ToString().TrimEnd('/');
        startInfo.Environment["DSH_HOME"] = options.DataDirectory;
        startInfo.Environment["DSH_MODEL"] = options.DefaultModel;
        startInfo.Environment["DEEPSEEK_BASE_URL"] = gatewayBaseUrl;
        startInfo.Environment["DEEPSEEK_SEARCH_BASE_URL"] = gatewayBaseUrl;
        startInfo.Environment["DEEPSEEK_API_KEY"] = options.VirtualKey;
        startInfo.Environment["DSH_TELEMETRY_DISABLED"] = "1";
        startInfo.Environment["DSH_TELEMETRY_MODE"] = "DISABLED";
        return startInfo;
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void EnsureRuntimeModuleIsReadable(string scriptPath)
    {
        var runtimeModules = FindRuntimeNodeModules(scriptPath);
        var requiredFiles = new[]
        {
            Path.Combine(
                runtimeModules,
                "@deepseek-ai",
                "dsh-client-ui-permission-presets",
                "package.json"),
            Path.Combine(
                runtimeModules,
                "@deepseek-ai",
                "dsh-client-ui-permission-presets",
                "lib",
                "index.js"),
        };

        foreach (var requiredFile in requiredFiles)
        {
            if (!File.Exists(requiredFile))
            {
                throw new FileNotFoundException(
                    "固定 Harness Runtime 缺少必要模块，请重新安装超智能 Harness。",
                    requiredFile);
            }

            using var stream = new FileStream(
                requiredFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            if (stream.Length == 0)
            {
                throw new InvalidDataException(
                    $"固定 Harness Runtime 模块为空：{requiredFile}");
            }
        }
    }

    private static string FindRuntimeNodeModules(string scriptPath)
    {
        for (var directory = new FileInfo(scriptPath).Directory;
             directory is not null;
             directory = directory.Parent)
        {
            if (string.Equals(directory.Name, "node_modules", StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Harness entry script is not located beneath a node_modules directory.");
    }

    private static string FindRuntimeModuleResolver(string runtimeModules)
    {
        var harnessDirectory = Directory.GetParent(runtimeModules)
            ?? throw new DirectoryNotFoundException("Harness Runtime directory is incomplete.");
        var runtimeRoot = harnessDirectory.Parent
            ?? throw new DirectoryNotFoundException("Harness Runtime root is unavailable.");
        var resolverPath = Path.Combine(runtimeRoot.FullName, "company-module-resolver.mjs");
        if (!File.Exists(resolverPath))
        {
            throw new FileNotFoundException(
                "固定 Harness Runtime 缺少公司模块解析器，请重新安装超智能 Harness。",
                resolverPath);
        }

        return resolverPath;
    }

    internal async Task WaitUntilReadyAsync(
        Process process,
        Uri localUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(timeout);

        try
        {
            while (true)
            {
                startupTimeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    await process.WaitForExitAsync(CancellationToken.None);
                    throw new HarnessStartupException(process.ExitCode, GetRecentStartupOutput());
                }

                try
                {
                    using var response = await healthClient.GetAsync(localUri, startupTimeout.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // The loopback listener is not ready yet.
                }
                catch (OperationCanceledException) when (
                    !startupTimeout.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
                {
                    // A single health request can time out while Harness is warming up.
                    // Keep retrying until the overall startup deadline is reached.
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), startupTimeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Harness did not become ready within {timeout.TotalSeconds:0} seconds.");
        }
    }

    private void ClearStartupOutput()
    {
        lock (_outputLock)
        {
            _recentStartupOutput.Clear();
        }
    }

    private void CaptureStartupOutput(string? line, string virtualKey)
    {
        if (!_captureStartupOutput || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var safeLine = line.Replace(virtualKey, "[REDACTED]", StringComparison.Ordinal);
        safeLine = KeyPattern.Replace(safeLine, "[REDACTED]");
        safeLine = new string(safeLine
            .Where(character => !char.IsControl(character) || character == '\t')
            .Take(MaximumCapturedLineLength)
            .ToArray());

        lock (_outputLock)
        {
            _recentStartupOutput.Enqueue(safeLine);
            while (_recentStartupOutput.Count > MaximumCapturedLines)
            {
                _recentStartupOutput.Dequeue();
            }
        }
    }
}

public sealed class HarnessStartupException(
    int exitCode,
    IReadOnlyList<string> recentOutput)
    : Exception($"Harness exited during startup with code {exitCode}.")
{
    public int ExitCode { get; } = exitCode;

    public IReadOnlyList<string> RecentOutput { get; } = recentOutput;
}

public enum HarnessRuntimeState
{
    Stopped,
    Starting,
    Running,
    Failed,
}
