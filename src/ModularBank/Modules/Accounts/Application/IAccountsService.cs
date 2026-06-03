using ModularBank.Modules.Accounts.Application.Dto;
using ModularBank.Shared.Domain;

namespace ModularBank.Modules.Accounts.Application;

public interface IAccountsService
{
    Task<AccountSummary> CreateAccountAsync(Guid userId);
    Task<Money> GetBalanceAsync(Guid accountId);
    Task DebitAsync(Guid accountId, Money amount, string? reference);
    Task CreditAsync(Guid accountId, Money amount, string? reference);
    Task<List<AccountSummary>> FindByOwnerAsync(Guid userId);
}
