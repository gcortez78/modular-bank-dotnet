using Microsoft.EntityFrameworkCore;

namespace ModularBank.Modules.Audit.Infrastructure;

public class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("audit");
    }
}
