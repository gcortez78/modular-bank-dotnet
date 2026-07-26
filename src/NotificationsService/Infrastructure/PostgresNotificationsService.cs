using System.Text.Json;
using FinBank.NotificationsService.Application;
using FinBank.NotificationsService.Application.Contracts;
using FinBank.NotificationsService.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinBank.NotificationsService.Infrastructure;

public sealed class PostgresNotificationsService(
    NotificationsDbContext db,
    ILogger<PostgresNotificationsService> logger) : INotificationsService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<CreateNotificationResult> CreateAsync(
        CreateNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = NormalizeKey(request.IdempotencyKey);

        if (idempotencyKey is not null)
        {
            var existing = await FindByIdempotencyKeyAsync(
                idempotencyKey,
                cancellationToken);

            if (existing is not null)
            {
                return new CreateNotificationResult(
                    ToResponse(existing),
                    Created: false);
            }
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Type = request.Type,
            PayloadJson = JsonSerializer.Serialize(request.Payload, JsonOptions),
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow
        };

        db.Notifications.Add(notification);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (idempotencyKey is not null)
        {
            db.Entry(notification).State = EntityState.Detached;

            var existing = await FindByIdempotencyKeyAsync(
                idempotencyKey,
                cancellationToken);

            if (existing is null)
            {
                throw;
            }

            logger.LogInformation(
                exception,
                "La notificación con clave de idempotencia {IdempotencyKey} ya existía.",
                idempotencyKey);

            return new CreateNotificationResult(
                ToResponse(existing),
                Created: false);
        }

        return new CreateNotificationResult(
            ToResponse(notification),
            Created: true);
    }

    public async Task<IReadOnlyList<NotificationResponse>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var notifications = await db.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .ToListAsync(cancellationToken);

        return notifications
            .Select(ToResponse)
            .ToList();
    }

    private Task<Notification?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return db.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                notification => notification.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    private static string? NormalizeKey(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static NotificationResponse ToResponse(Notification notification)
    {
        Dictionary<string, string> payload;

        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, string>>(
                notification.PayloadJson,
                JsonOptions) ?? new Dictionary<string, string>();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"El payload JSON de la notificación {notification.Id} no es válido.",
                exception);
        }

        return new NotificationResponse(
            notification.Id,
            notification.UserId,
            notification.Type,
            payload,
            notification.CreatedAt);
    }
}
