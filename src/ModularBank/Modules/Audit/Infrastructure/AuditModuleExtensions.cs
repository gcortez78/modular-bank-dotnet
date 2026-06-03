using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ModularBank.Modules.Audit.Infrastructure;

public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AuditDbContext>(opt => opt.UseNpgsql(connectionString));
        return services;
    }
}
