namespace TransfersService.Infrastructure;

public interface IMonolithBankingClient
{
    Task ApplyTransferAsync(
        AccountsTransferCommand command,
        CancellationToken cancellationToken);
}
