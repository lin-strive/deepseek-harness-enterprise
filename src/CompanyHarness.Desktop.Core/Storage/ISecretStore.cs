namespace CompanyHarness.Desktop.Core.Storage;

public interface ISecretStore
{
    string? Read(string targetName);

    void Write(string targetName, string secret, string userName);

    void Delete(string targetName);
}
