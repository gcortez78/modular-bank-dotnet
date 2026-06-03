using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Accounts.Application;
using ModularBank.Modules.Accounts.Application.Dto;
using ModularBank.Modules.Accounts.Domain;
using ModularBank.Shared.Domain;

namespace ModularBank.Modules.Accounts.Infrastructure;

public class AccountsService(AccountsDbContext db) : IAccountsService
{
    public async Task<AccountSummary> CreateAccountAsync(Guid userId)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountNumber = "ACC" + Guid.NewGuid().ToString("N")[..12].ToUpper(),
            Balance = 0
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return ToSummary(account);
    }

    public async Task<Money> GetBalanceAsync(Guid accountId)
    {
        var account = await db.Accounts.FindAsync(accountId)
            ?? throw new KeyNotFoundException("Account not found");
        return Money.Of(account.Balance);
    }

    public async Task DebitAsync(Guid accountId, Money amount, string? reference)
    {
        var rows = await db.Accounts
            .Where(a => a.Id == accountId && a.Balance >= amount.Amount)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance - amount.Amount));

        if (rows == 0)
        {
            if (!await db.Accounts.AnyAsync(a => a.Id == accountId))
                throw new KeyNotFoundException("Account not found");
            throw new InvalidOperationException("Insufficient funds");
        }
    }

    public async Task CreditAsync(Guid accountId, Money amount, string? reference)
    {
        var rows = await db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance + amount.Amount));

        if (rows == 0)
            throw new KeyNotFoundException("Account not found");
    }

    public async Task<List<AccountSummary>> FindByOwnerAsync(Guid userId)
    {
        return await db.Accounts
            .Where(a => a.UserId == userId)
            .Select(a => ToSummary(a))
            .ToListAsync();
    }

    private static AccountSummary ToSummary(Account a) =>
        new(a.Id, a.AccountNumber, a.Balance);
}
