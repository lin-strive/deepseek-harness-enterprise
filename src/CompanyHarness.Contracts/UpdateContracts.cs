namespace CompanyHarness.Contracts;

public sealed record UpdateManifest(
    string Channel,
    DateTimeOffset PublishedAt,
    UpdateComponent Desktop,
    UpdateComponent Harness,
    UpdateComponent Node,
    UpdateComponent Skills,
    string MinimumDesktopVersion,
    bool IsMandatory,
    DateTimeOffset? MandatoryAfter,
    string ReleaseNotes,
    string Signature);

public sealed record UpdateComponent(
    string Version,
    Uri DownloadUrl,
    long SizeBytes,
    string Sha256,
    string? RollbackVersion);
