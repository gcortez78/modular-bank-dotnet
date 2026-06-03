using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularBank.Modules.Notifications.Application;
using Npgsql;

namespace ModularBank.Modules.Notifications.Infrastructure;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString)
    {
        var dataSource = new NpgsqlDataSourceBuilder(connectionString)
            .EnableDynamicJson()
            .Build();
        services.AddDbContext<NotificationsDbContext>(opt =>
            opt.UseNpgsql(dataSource));
        services.AddScoped<INotificationsService, NotificationsService>();
        return services;
    }
}
