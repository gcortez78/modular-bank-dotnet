using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularBank.Modules.Audit.Application;

namespace ModularBank.Modules.Audit.Infrastructure;

public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AuditDbContext>(opt =>
            opt.UseNpgsql(connectionString));
        services.AddScoped<IAuditService, AuditService>();
        return services;
    }
}
