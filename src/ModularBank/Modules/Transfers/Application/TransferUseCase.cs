using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Accounts.Application;
using ModularBank.Modules.Audit.Application;
using ModularBank.Modules.Notifications.Application;
using ModularBank.Modules.Notifications.Domain;
using ModularBank.Modules.Transfers.Application.Dto;
using ModularBank.Modules.Transfers.Domain;
using ModularBank.Modules.Transfers.Infrastructure;
using ModularBank.Shared.Domain;

namespace ModularBank.Modules.Transfers.Application;

public class TransferUseCase(
    TransfersDbContext db,
    IAccountsService accountsService,
    INotificationsService notificationsService,
    IAuditService auditService)
{
    // Note: debit/credit operate on AccountsDbContext, which is a separate EF connection.
    // They are not covered by this TransfersDbContext transaction. If db.SaveChangesAsync()
    // fails after debit+credit, the account balances are already modified.
    // Production systems should use a Saga or outbox pattern for full atomicity.
    public async Task<Transfer> ExecuteAsync(Guid userId, TransferRequest request)
    {
        var owned = await accountsService.FindByOwnerAsync(userId);
        if (!owned.Any(a => a.Id == request.SourceAccountId))
            throw new UnauthorizedAccessException("Source account does not belong to the authenticated user.");

        var amount = Money.Of(request.Amount);

        await accountsService.DebitAsync(request.SourceAccountId, amount, request.Reference);
        await accountsService.CreditAsync(request.TargetAccountId, amount, request.Reference);

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            SourceAccountId = request.SourceAccountId,
            TargetAccountId = request.TargetAccountId,
            Amount = request.Amount,
            Reference = request.Reference,
            CreatedAt = DateTime.UtcNow
        };
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync();

        await notificationsService.SendAsync(userId, NotificationType.TransferSent, new Dictionary<string, string>
        {
            ["amount"] = request.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["targetAccountId"] = request.TargetAccountId.ToString()
        });

        await auditService.RecordAsync(userId, "TRANSFER_EXECUTED", new Dictionary<string, string>
        {
            ["transferId"] = transfer.Id.ToString(),
            ["amount"] = request.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

        return transfer;
    }

    public async Task<List<Transfer>> GetHistoryAsync(Guid userId, Guid accountId)
    {
        var owned = await accountsService.FindByOwnerAsync(userId);
        if (!owned.Any(a => a.Id == accountId))
            throw new UnauthorizedAccessException("Account does not belong to the authenticated user.");

        return await db.Transfers
            .Where(t => t.SourceAccountId == accountId || t.TargetAccountId == accountId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}
