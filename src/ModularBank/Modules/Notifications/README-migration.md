# notifications — Migration Guide

## Public interface
`INotificationsService.SendAsync(Guid userId, NotificationType type, Dictionary<string,string> payload)`

## Consumers
- `transfers` module (on transfer sent/received)
- `auth` module (on login)

## To extract as microservice
1. Create `notifications-service` with the same DB schema
2. Replace `NotificationsService` with `NotificationsHttpClient`:
   - POST /internal/notifications { userId, type, payload }
   - Make this call fire-and-forget
3. Transfer transactions no longer roll back if notification fails
   — consider an outbox pattern if delivery guarantee is required
