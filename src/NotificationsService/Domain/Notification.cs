namespace FinBank.NotificationsService.Domain;

public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }

    // JSON serializado por la aplicación y persistido en PostgreSQL como jsonb.
    public string PayloadJson { get; set; } = "{}";

    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
