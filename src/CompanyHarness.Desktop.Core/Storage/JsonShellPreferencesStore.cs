using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompanyHarness.Desktop.Core.Storage;

public sealed class JsonShellPreferencesStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<ShellPreferences> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return ShellPreferences.Default;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var preferences = await JsonSerializer.DeserializeAsync<ShellPreferences>(stream, JsonOptions, cancellationToken);
            return preferences is not null && Enum.IsDefined(preferences.ThemeMode)
                ? preferences
                : ShellPreferences.Default;
        }
        catch (JsonException)
        {
            return ShellPreferences.Default;
        }
    }

    public async Task WriteAsync(ShellPreferences preferences, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Shell preferences path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = filePath + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, filePath, overwrite: true);
    }
}
