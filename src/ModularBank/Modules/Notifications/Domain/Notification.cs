namespace ModularBank.Modules.Notifications.Domain;

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public Dictionary<string, string> Payload { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
