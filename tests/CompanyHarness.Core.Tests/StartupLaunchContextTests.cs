using CompanyHarness.Desktop.Core.Runtime;

namespace CompanyHarness.Core.Tests;

public sealed class StartupLaunchContextTests
{
    [Fact]
    public void Parse_RecognizesPostInstallProcess()
    {
        var result = StartupLaunchContext.Parse(
            ["--post-install", "--wait-for-process", "2468"]);

        Assert.True(result.IsPostInstall);
        Assert.Equal(2468, result.InstallerProcessId);
        Assert.False(result.IsRecoveryLaunch);
        Assert.Null(result.RestartAfterProcessId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Parse_IgnoresInvalidProcessId(string value)
    {
        var result = StartupLaunchContext.Parse(["--wait-for-process", value]);

        Assert.False(result.IsPostInstall);
        Assert.Null(result.InstallerProcessId);
    }

    [Fact]
    public void Parse_RecognizesControlledRecoveryRestart()
    {
        var result = StartupLaunchContext.Parse(
            ["--startup-recovery", "--restart-after-process", "1357"]);

        Assert.True(result.IsRecoveryLaunch);
        Assert.Equal(1357, result.RestartAfterProcessId);
        Assert.False(result.IsPostInstall);
        Assert.Null(result.InstallerProcessId);
    }
}
