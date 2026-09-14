using CompanyHarness.Contracts;

namespace CompanyHarness.Desktop.Core.Storage;

public sealed record ActivationProfile(
    EmployeeProfile Employee,
    Uri GatewayBaseUrl,
    QuotaSummary Quota,
    IReadOnlyList<string> AllowedModels,
    string? DefaultModel,
    IReadOnlyList<ModelCatalogEntry>? Models,
    DateTimeOffset ActivatedAt);
