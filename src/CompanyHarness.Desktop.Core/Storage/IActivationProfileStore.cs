namespace CompanyHarness.Desktop.Core.Storage;

public interface IActivationProfileStore
{
    Task<ActivationProfile?> ReadAsync(CancellationToken cancellationToken);

    Task WriteAsync(ActivationProfile profile, CancellationToken cancellationToken);

    Task DeleteAsync(CancellationToken cancellationToken);
}
