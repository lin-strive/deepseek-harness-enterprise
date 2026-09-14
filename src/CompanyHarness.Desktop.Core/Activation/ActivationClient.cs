using System.Net.Http.Json;
using CompanyHarness.Contracts;

namespace CompanyHarness.Desktop.Core.Activation;

public sealed class ActivationClient(HttpClient httpClient)
{
    public async Task<ActivationPreviewResponse> PreviewAsync(
        string activationCode,
        string clientVersion,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/activation/preview",
            new ActivationPreviewRequest(activationCode.Trim(), clientVersion),
            cancellationToken);

        return await ReadResponseAsync<ActivationPreviewResponse>(response, cancellationToken);
    }

    public async Task<ActivationConfirmResponse> ConfirmAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/v1/activation/confirm",
            new ActivationConfirmRequest(sessionId),
            cancellationToken);

        return await ReadResponseAsync<ActivationConfirmResponse>(response, cancellationToken);
    }

    public async Task<ModelCatalogResponse> GetModelCatalogAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("api/v1/models", cancellationToken);
        return await ReadResponseAsync<ModelCatalogResponse>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new ActivationClientException("client.empty_response", "服务器返回了空响应。", null);
        }

        var error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken);
        throw new ActivationClientException(
            error?.Code ?? "client.http_error",
            error?.Message ?? $"请求失败（HTTP {(int)response.StatusCode}）。",
            error?.SupportId);
    }
}

public sealed class ActivationClientException(string code, string message, string? supportId)
    : Exception(message)
{
    public string Code { get; } = code;

    public string? SupportId { get; } = supportId;
}
