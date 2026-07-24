using FinBank.NotificationsService.Application.Contracts;

namespace FinBank.NotificationsService.Application;

public interface INotificationsService
{
    Task<CreateNotificationResult> CreateAsync(
        CreateNotificationRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationResponse>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
