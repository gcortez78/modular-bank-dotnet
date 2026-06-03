using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularBank.Modules.Transfers.Application;

namespace ModularBank.Modules.Transfers.Infrastructure;

public static class TransfersModuleExtensions
{
    public static IServiceCollection AddTransfersModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TransfersDbContext>(opt =>
            opt.UseNpgsql(connectionString));
        services.AddScoped<TransferUseCase>();
        return services;
    }
}
