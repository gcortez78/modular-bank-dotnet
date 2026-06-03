using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Audit.Application;
using ModularBank.Modules.Audit.Domain;

namespace ModularBank.Modules.Audit.Infrastructure;

public class AuditService(AuditDbContext db) : IAuditService
{
    public async Task RecordAsync(Guid userId, string action, Dictionary<string, string> metadata)
    {
        if (string.IsNullOrWhiteSpace(action) || action.Length > 100)
            throw new ArgumentException("Action must be 1-100 characters.", nameof(action));

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action,
            Metadata = metadata
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<AuditEntry>> GetForUserAsync(Guid userId)
    {
        return await db.AuditEntries
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
    }
}
