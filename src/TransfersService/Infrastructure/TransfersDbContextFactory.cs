using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TransfersService.Infrastructure;

public sealed class TransfersDbContextFactory
    : IDesignTimeDbContextFactory<TransfersDbContext>
{
    public TransfersDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__Default")
            ?? "Host=localhost;Port=5435;Database=transfers_db;Username=transfers;Password=transfers-local;Search Path=transfers";

        var options = new DbContextOptionsBuilder<TransfersDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "transfers"))
            .Options;

        return new TransfersDbContext(options);
    }
}
