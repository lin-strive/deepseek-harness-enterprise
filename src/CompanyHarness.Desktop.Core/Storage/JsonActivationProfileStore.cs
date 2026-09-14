using System.Text.Json;

namespace CompanyHarness.Desktop.Core.Storage;

public sealed class JsonActivationProfileStore(string filePath) : IActivationProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<ActivationProfile?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<ActivationProfile>(stream, JsonOptions, cancellationToken);
    }

    public async Task WriteAsync(ActivationProfile profile, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Activation profile path has no parent directory.");
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
            await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, filePath, overwrite: true);
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }
}
