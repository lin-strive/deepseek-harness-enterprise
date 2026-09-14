using System.Text;

namespace CompanyHarness.Desktop.Core.Runtime;

public sealed record StartupDiagnosticRecord(string SupportId, string LogPath);

public sealed class StartupDiagnosticLog(string applicationDataRoot)
{
    private const long MaximumLogSizeBytes = 512 * 1024;
    private readonly string _logDirectory = Path.Combine(
        Path.GetFullPath(applicationDataRoot),
        "logs");

    public async Task<StartupDiagnosticRecord> WriteFailureAsync(
        string applicationVersion,
        Exception exception,
        IReadOnlyList<string> harnessStartupOutput,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(harnessStartupOutput);

        Directory.CreateDirectory(_logDirectory);
        var logPath = Path.Combine(_logDirectory, "startup.log");
        RotateIfNeeded(logPath);

        var supportId = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var builder = new StringBuilder()
            .AppendLine("--- Company Harness startup failure ---")
            .Append("timeUtc: ").AppendLine(DateTimeOffset.UtcNow.ToString("O"))
            .Append("supportId: ").AppendLine(supportId)
            .Append("appVersion: ").AppendLine(applicationVersion)
            .Append("osVersion: ").AppendLine(Environment.OSVersion.VersionString)
            .Append("exceptionType: ").AppendLine(exception.GetType().FullName)
            .Append("hresult: 0x").AppendLine(exception.HResult.ToString("X8"))
            .Append("message: ").AppendLine(Sanitize(exception.Message));

        if (exception is HarnessStartupException harnessException)
        {
            builder.Append("harnessExitCode: ").AppendLine(harnessException.ExitCode.ToString());
        }

        if (harnessStartupOutput.Count > 0)
        {
            builder.AppendLine("harnessStartupOutput:");
            foreach (var line in harnessStartupOutput.Take(24))
            {
                builder.Append("  ").AppendLine(Sanitize(line));
            }
        }

        builder.AppendLine();
        await File.AppendAllTextAsync(logPath, builder.ToString(), Encoding.UTF8, cancellationToken);
        return new StartupDiagnosticRecord(supportId, logPath);
    }

    private static string Sanitize(string value) => new(value
        .Where(character => !char.IsControl(character) || character is '\t' or '\r' or '\n')
        .Take(2_000)
        .ToArray());

    private static void RotateIfNeeded(string logPath)
    {
        if (!File.Exists(logPath) || new FileInfo(logPath).Length < MaximumLogSizeBytes)
        {
            return;
        }

        File.Move(logPath, Path.ChangeExtension(logPath, ".previous.log"), overwrite: true);
    }
}
