namespace CompanyHarness.Contracts;

public sealed record ActivationPreviewRequest(string ActivationCode, string ClientVersion);

public sealed record ActivationPreviewResponse(
    string ActivationSessionId,
    EmployeeProfile Employee,
    DateTimeOffset ExpiresAt);

public sealed record ActivationConfirmRequest(string ActivationSessionId);

public sealed record ActivationConfirmResponse(
    EmployeeProfile Employee,
    string VirtualKey,
    Uri GatewayBaseUrl,
    QuotaSummary Quota,
    ClientPolicy Policy);

public sealed record EmployeeProfile(
    Guid EmployeeId,
    string EmployeeNumber,
    string DisplayName,
    string Department);

public sealed record ClientPolicy(
    IReadOnlyList<string> AllowedModels,
    string DefaultModel,
    IReadOnlyList<ModelCatalogEntry> Models,
    bool PersonalProvidersAllowed,
    bool TelemetryEnabled);

public sealed record ModelCatalogEntry(
    string Id,
    string Name,
    string Description,
    bool Experimental,
    IReadOnlyList<string> InputModalities);

public sealed record ModelCatalogResponse(
    string DefaultModel,
    IReadOnlyList<ModelCatalogEntry> Models);

public sealed record ApiError(string Code, string Message, string SupportId);
