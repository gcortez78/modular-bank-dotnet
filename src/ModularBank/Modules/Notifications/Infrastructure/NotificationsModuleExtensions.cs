using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularBank.Modules.Notifications.Application;

namespace ModularBank.Modules.Notifications.Infrastructure;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<NotificationsDbContext>(opt =>
            opt.UseNpgsql(connectionString));
        services.AddScoped<INotificationsService, NotificationsService>();
        return services;
    }
}
