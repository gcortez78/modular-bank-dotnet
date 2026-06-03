using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularBank.Modules.Auth.Application;

namespace ModularBank.Modules.Auth.Infrastructure;

public static class AuthModuleExtensions
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AuthDbContext>(opt =>
            opt.UseNpgsql(connectionString));
        services.AddScoped<AuthUseCase>();
        return services;
    }
}
