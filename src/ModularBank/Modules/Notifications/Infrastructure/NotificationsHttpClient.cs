using System.Net.Http.Json;
using ModularBank.Modules.Notifications.Application;
using ModularBank.Modules.Notifications.Domain;

namespace ModularBank.Modules.Notifications.Infrastructure;

public sealed class NotificationsHttpClient(HttpClient httpClient)
    : INotificationsService
{
    public async Task SendAsync(
        Guid userId,
        NotificationType type,
        Dictionary<string, string> payload,
        string? idempotencyKey = null)
    {
        var request = new CreateNotificationRequest(
            userId,
            type,
            payload,
            idempotencyKey);

        using var response = await httpClient.PostAsJsonAsync(
            "internal/notifications",
            request);

        await EnsureSuccessAsync(response);
    }

    public async Task<List<Notification>> GetForUserAsync(Guid userId)
    {
        using var response = await httpClient.GetAsync(
            $"internal/notifications/{userId}");

        await EnsureSuccessAsync(response);

        return await response.Content
            .ReadFromJsonAsync<List<Notification>>()
            ?? [];
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();

        throw new HttpRequestException(
            $"Notifications Service respondió {(int)response.StatusCode} " +
            $"{response.ReasonPhrase}. Cuerpo: {body}",
            inner: null,
            response.StatusCode);
    }

    private sealed record CreateNotificationRequest(
        Guid UserId,
        NotificationType Type,
        Dictionary<string, string> Payload,
        string? IdempotencyKey);
}
