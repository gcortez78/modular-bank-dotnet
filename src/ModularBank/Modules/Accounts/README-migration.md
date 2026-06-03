# Accounts — Migration Guide

## Public interface
```csharp
IAccountsService.CreateAccountAsync(Guid userId) → AccountSummary
IAccountsService.GetBalanceAsync(Guid accountId) → Money
IAccountsService.DebitAsync(Guid accountId, Money amount, string? reference)
IAccountsService.CreditAsync(Guid accountId, Money amount, string? reference)
IAccountsService.FindByOwnerAsync(Guid userId) → List<AccountSummary>
```

## Consumers
- `Transfers` module

## To extract as microservice
1. Create `accounts-service` with the same DB schema
2. Replace the `IAccountsService` DI registration with `AccountsHttpClient : IAccountsService`:
   - GET  /internal/accounts/{id}/balance
   - POST /internal/accounts/{id}/debit  { amount, reference }
   - POST /internal/accounts/{id}/credit { amount, reference }
3. Debit + credit are no longer in the same transaction as transfer record.
   Implement a Saga with compensation in `Transfers` module.
