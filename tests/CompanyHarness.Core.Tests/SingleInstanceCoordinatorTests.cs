using CompanyHarness.Desktop.Core.Runtime;

namespace CompanyHarness.Core.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondaryInstanceSignalsPrimaryListener()
    {
        var instanceName = $"Local\\SmartWheelchair.CompanyHarness.Tests.{Guid.NewGuid():N}";
        using var primary = new SingleInstanceCoordinator(instanceName);
        using var secondary = new SingleInstanceCoordinator(instanceName);
        var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(primary.IsPrimaryInstance);
        Assert.False(secondary.IsPrimaryInstance);

        primary.StartListening(() => activation.TrySetResult());
        secondary.SignalPrimaryInstance();

        await activation.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SecondaryInstanceCannotStartListener()
    {
        var instanceName = $"Local\\SmartWheelchair.CompanyHarness.Tests.{Guid.NewGuid():N}";
        using var primary = new SingleInstanceCoordinator(instanceName);
        using var secondary = new SingleInstanceCoordinator(instanceName);

        Assert.Throws<InvalidOperationException>(() => secondary.StartListening(() => { }));
    }
}
