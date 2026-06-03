using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularBank.Modules.Accounts.Application;

namespace ModularBank.Modules.Accounts.Infrastructure;

public static class AccountsModuleExtensions
{
    public static IServiceCollection AddAccountsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AccountsDbContext>(opt =>
            opt.UseNpgsql(connectionString));
        services.AddScoped<IAccountsService, AccountsService>();
        return services;
    }
}
