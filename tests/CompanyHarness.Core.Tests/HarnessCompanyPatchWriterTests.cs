using CompanyHarness.Contracts;
using CompanyHarness.Desktop.Core.Runtime;

namespace CompanyHarness.Core.Tests;

public sealed class HarnessCompanyPatchWriterTests
{
    [Fact]
    public async Task WriteAsync_RendersOfficialModelsAndVisionCapability()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"company-harness-patch-{Guid.NewGuid():N}");
        var catalog = new ModelCatalogResponse("deepseek-v4-flash", [
            new("deepseek-v4-flash", "DeepSeek V4 Flash", "Fast", false, ["text"]),
            new("deepseek-v4-flash-vision-exp", "DeepSeek V4 Flash Vision Exp", "Vision", true, ["text", "image"]),
        ]);
        try
        {
            var path = await HarnessCompanyPatchWriter.WriteAsync(directory, catalog, CancellationToken.None);
            var yaml = await File.ReadAllTextAsync(path);
            Assert.Contains("model: 'deepseek-v4-flash'", yaml);
            Assert.Contains($"models:{Environment.NewLine}      - id: 'deepseek-v4-flash'", yaml);
            Assert.DoesNotContain("models:      - id:", yaml);
            Assert.Contains("id: 'deepseek-v4-flash-vision-exp'", yaml);
            Assert.Contains("inputModalities: [text, image]", yaml);
            Assert.DoesNotContain("company-coder", yaml);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
