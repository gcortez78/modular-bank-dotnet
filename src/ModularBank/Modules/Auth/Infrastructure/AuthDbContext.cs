using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Auth.Domain;

namespace ModularBank.Modules.Auth.Infrastructure;

public class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("auth");

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired().HasMaxLength(255);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).IsRequired().HasColumnName("password_hash");
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.Token).IsRequired().HasMaxLength(255);
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        });
    }
}
