using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularBank.Modules.Audit.Application;
using Npgsql;

namespace ModularBank.Modules.Audit.Infrastructure;

public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, string connectionString)
    {
        var dataSource = new NpgsqlDataSourceBuilder(connectionString)
            .EnableDynamicJson()
            .Build();
        services.AddDbContext<AuditDbContext>(opt =>
            opt.UseNpgsql(dataSource));
        services.AddScoped<IAuditService, AuditService>();
        return services;
    }
}
