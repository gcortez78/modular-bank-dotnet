using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TransfersService.Infrastructure;

public sealed class MonolithBankingClient(
    HttpClient httpClient,
    IOptions<MonolithOptions> options,
    ILogger<MonolithBankingClient> logger) : IMonolithBankingClient
{
    private readonly MonolithOptions _options = options.Value;

    public async Task ApplyTransferAsync(
        AccountsTransferCommand command,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "internal/transfers/accounts/apply")
        {
            Content = JsonContent.Create(command)
        };

        request.Headers.Add("X-Internal-Api-Key", _options.InternalApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = ExtractDetail(body) ??
                     $"El monolito respondió HTTP {(int)response.StatusCode}.";

        logger.LogWarning(
            "Falló la aplicación contable de la transferencia {TransferId}. HTTP {StatusCode}: {Detail}",
            command.TransferId,
            (int)response.StatusCode,
            detail);

        throw new MonolithBankingException((int)response.StatusCode, detail);
    }

    private static string? ExtractDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("detail", out var detail))
                return detail.GetString();

            if (document.RootElement.TryGetProperty("error", out var error))
                return error.GetString();
        }
        catch (JsonException)
        {
            // El cuerpo puede no ser JSON.
        }

        return body.Length <= 500 ? body : body[..500];
    }
}
