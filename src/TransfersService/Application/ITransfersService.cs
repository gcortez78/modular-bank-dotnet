using TransfersService.Application.Contracts;

namespace TransfersService.Application;

public interface ITransfersService
{
    Task<TransferResponse> CreateAsync(
        Guid userId,
        CreateTransferRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TransferResponse>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
