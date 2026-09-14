using CompanyHarness.Contracts;
using CompanyHarness.Desktop.Core.Storage;

namespace CompanyHarness.Core.Tests;

public sealed class ActivationProfileStoreTests
{
    [Fact]
    public async Task RoundTrip_PersistsOnlyNonSecretActivationProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"company-harness-profile-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "profile.json");
        var store = new JsonActivationProfileStore(path);
        var employee = new EmployeeProfile(Guid.NewGuid(), "SW0021", "张三", "研发部");
        var resetAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(8));
        var profile = new ActivationProfile(
            employee,
            new Uri("https://gateway.example.internal/v1"),
            new QuotaSummary(500m, 15m, 30, 120_000, 3, resetAt),
            ["deepseek-v4-flash", "deepseek-v4-pro"],
            "deepseek-v4-flash",
            [
                new ModelCatalogEntry("deepseek-v4-flash", "DeepSeek V4 Flash", "Fast", false, ["text"]),
                new ModelCatalogEntry("deepseek-v4-pro", "DeepSeek V4 Pro", "Pro", false, ["text"]),
            ],
            new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.FromHours(8)));

        try
        {
            await store.WriteAsync(profile, CancellationToken.None);
            var restored = await store.ReadAsync(CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);

            Assert.NotNull(restored);
            Assert.Equal(profile.Employee, restored.Employee);
            Assert.Equal(profile.GatewayBaseUrl, restored.GatewayBaseUrl);
            Assert.Equal(profile.Quota, restored.Quota);
            Assert.Equal(profile.AllowedModels, restored.AllowedModels);
            Assert.Equal(profile.DefaultModel, restored.DefaultModel);
            Assert.Equal(profile.Models!.Select(model => model.Id), restored.Models!.Select(model => model.Id));
            Assert.Equal(profile.ActivatedAt, restored.ActivatedAt);
            Assert.DoesNotContain("virtualKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
