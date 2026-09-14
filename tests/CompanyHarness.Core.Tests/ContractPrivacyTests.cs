using CompanyHarness.Contracts;

namespace CompanyHarness.Core.Tests;

public sealed class ContractPrivacyTests
{
    [Fact]
    public void ActivationRequests_DoNotContainDeviceIdentity()
    {
        var propertyNames = typeof(ActivationPreviewRequest).GetProperties()
            .Concat(typeof(ActivationConfirmRequest).GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name => name.Contains("Device", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Hardware", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Fingerprint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConfirmPolicy_DisablesPersonalProvidersAndTelemetry()
    {
        var models = new[] { new ModelCatalogEntry("deepseek-v4-flash", "DeepSeek V4 Flash", "Fast", false, ["text"]) };
        var policy = new ClientPolicy(["deepseek-v4-flash"], "deepseek-v4-flash", models, false, false);

        Assert.False(policy.PersonalProvidersAllowed);
        Assert.False(policy.TelemetryEnabled);
    }
}
