using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ModularBank.Modules.Notifications.Infrastructure;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<NotificationsDbContext>(opt => opt.UseNpgsql(connectionString));
        return services;
    }
}
