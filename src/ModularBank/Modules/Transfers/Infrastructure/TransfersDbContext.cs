using Microsoft.EntityFrameworkCore;

namespace ModularBank.Modules.Transfers.Infrastructure;

public class TransfersDbContext(DbContextOptions<TransfersDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("transfers");
    }
}
