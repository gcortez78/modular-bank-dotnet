using ModularBank.Modules.Audit.Domain;

namespace ModularBank.Modules.Audit.Application;

public interface IAuditService
{
    Task RecordAsync(Guid userId, string action, Dictionary<string, string> metadata);
    Task<List<AuditEntry>> GetForUserAsync(Guid userId);
}
