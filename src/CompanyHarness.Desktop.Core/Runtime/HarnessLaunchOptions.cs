namespace CompanyHarness.Desktop.Core.Runtime;

public sealed record HarnessLaunchOptions(
    string ExecutablePath,
    string? ScriptPath,
    string CompanyPatchPath,
    string DataDirectory,
    Uri GatewayBaseUrl,
    string VirtualKey,
    string DefaultModel,
    TimeSpan StartupTimeout)
{
    private static readonly System.Text.RegularExpressions.Regex ModelIdPattern =
        new("^[a-z0-9][a-z0-9._-]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public void Validate()
    {
        if (!File.Exists(ExecutablePath))
        {
            throw new FileNotFoundException("Harness executable was not found.", ExecutablePath);
        }

        if (ScriptPath is not null && !File.Exists(ScriptPath))
        {
            throw new FileNotFoundException("Harness entry script was not found.", ScriptPath);
        }

        if (!File.Exists(CompanyPatchPath))
        {
            throw new FileNotFoundException("Company Harness configuration was not found.", CompanyPatchPath);
        }

        if (!GatewayBaseUrl.IsAbsoluteUri
            || GatewayBaseUrl.Scheme != Uri.UriSchemeHttp
                && GatewayBaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Gateway URL must use HTTP or HTTPS.", nameof(GatewayBaseUrl));
        }

        if (!string.IsNullOrEmpty(GatewayBaseUrl.UserInfo)
            || !string.IsNullOrEmpty(GatewayBaseUrl.Query)
            || !string.IsNullOrEmpty(GatewayBaseUrl.Fragment)
            || !GatewayBaseUrl.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Gateway URL must be an API base ending in /v1 without credentials, query, or fragment.", nameof(GatewayBaseUrl));
        }

        if (string.IsNullOrWhiteSpace(VirtualKey))
        {
            throw new ArgumentException("A virtual key is required.", nameof(VirtualKey));
        }

        if (!ModelIdPattern.IsMatch(DefaultModel))
        {
            throw new ArgumentException("Default model ID is invalid.", nameof(DefaultModel));
        }

        if (StartupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StartupTimeout));
        }

    }
}
