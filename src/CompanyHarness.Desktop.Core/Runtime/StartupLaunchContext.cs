namespace CompanyHarness.Desktop.Core.Runtime;

public sealed record StartupLaunchContext(
    bool IsPostInstall,
    int? InstallerProcessId,
    bool IsRecoveryLaunch,
    int? RestartAfterProcessId)
{
    public static StartupLaunchContext Default { get; } = new(false, null, false, null);

    public static StartupLaunchContext Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var isPostInstall = arguments.Any(argument =>
            string.Equals(argument, "--post-install", StringComparison.OrdinalIgnoreCase));
        var isRecoveryLaunch = arguments.Any(argument =>
            string.Equals(argument, "--startup-recovery", StringComparison.OrdinalIgnoreCase));
        int? installerProcessId = null;
        int? restartAfterProcessId = null;
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], "--wait-for-process", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(arguments[index + 1], out var parsedProcessId)
                && parsedProcessId > 0)
            {
                installerProcessId = parsedProcessId;
            }

            if (string.Equals(arguments[index], "--restart-after-process", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(arguments[index + 1], out parsedProcessId)
                && parsedProcessId > 0)
            {
                restartAfterProcessId = parsedProcessId;
            }
        }

        return new StartupLaunchContext(
            isPostInstall,
            installerProcessId,
            isRecoveryLaunch,
            restartAfterProcessId);
    }
}
