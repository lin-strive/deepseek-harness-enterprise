using CompanyHarness.Desktop.Core.Storage;

namespace CompanyHarness.Core.Tests;

public sealed class ShellPreferencesStoreTests
{
    [Fact]
    public async Task RoundTrip_PersistsThemeModeAsReadableJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"company-harness-shell-preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "shell-preferences.json");
        var store = new JsonShellPreferencesStore(path);

        try
        {
            await store.WriteAsync(new ShellPreferences(ShellThemeMode.Dark), CancellationToken.None);

            var restored = await store.ReadAsync(CancellationToken.None);
            var json = await File.ReadAllTextAsync(path);

            Assert.Equal(ShellThemeMode.Dark, restored.ThemeMode);
            Assert.Contains("Dark", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReadAsync_InvalidJsonFallsBackToFollowHarness()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"company-harness-shell-preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "shell-preferences.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "{not-json");
        var store = new JsonShellPreferencesStore(path);

        try
        {
            var restored = await store.ReadAsync(CancellationToken.None);

            Assert.Equal(ShellThemeMode.FollowHarness, restored.ThemeMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
