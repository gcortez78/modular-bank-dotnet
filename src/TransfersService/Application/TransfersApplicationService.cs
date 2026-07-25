using FinBank.IntegrationEvents;
using FinBank.IntegrationEvents.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransfersService.Application.Contracts;
using TransfersService.Domain;
using TransfersService.Infrastructure;
using TransfersService.Messaging;

namespace TransfersService.Application;

public sealed class TransfersApplicationService(
    TransfersDbContext db,
    IOptions<RabbitMqOptions> rabbitOptions,
    ILogger<TransfersApplicationService> logger) : ITransfersService
{
    private readonly RabbitMqOptions _rabbit = rabbitOptions.Value;

    public async Task<TransferResponse> CreateAsync(
        Guid userId,
        CreateTransferRequest request,
        CancellationToken cancellationToken)
    {
        var transfer = Transfer.Create(
            userId,
            request.SourceAccountId,
            request.TargetAccountId,
            request.Amount,
            request.Reference);

        var occurredAt = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid();
        var correlationId = transfer.Id;

        var cloudEvent = CloudEventFactory.Create(
            EventCatalog.TransferRequestedV1,
            source: "finbank/transfers-service",
            subject: $"transfer/{transfer.Id:N}",
            correlationId: correlationId,
            causationId: null,
            data: new TransferRequestedV1(
                transfer.Id,
                transfer.UserId,
                transfer.SourceAccountId,
                transfer.TargetAccountId,
                transfer.Amount,
                "BOB",
                transfer.Reference),
            eventId: eventId,
            occurredAt: occurredAt);

        var payload = CloudEventJson.Serialize(cloudEvent);

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        db.Transfers.Add(transfer);
        db.OutboxMessages.Add(
            OutboxMessage.Create(
                cloudEvent.Id,
                cloudEvent.Type,
                _rabbit.TransferRequestedRoutingKey,
                payload,
                occurredAt));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Transferencia {TransferId} creada en Pending; CloudEvent {EventId} guardado en Outbox. CorrelationId {CorrelationId}.",
            transfer.Id,
            cloudEvent.Id,
            cloudEvent.CorrelationId);

        return Map(transfer);
    }

    public async Task<IReadOnlyList<TransferResponse>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await db.Transfers
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new TransferResponse(
                x.Id,
                x.SourceAccountId,
                x.TargetAccountId,
                x.Amount,
                x.Reference,
                x.Status,
                x.CreatedAt,
                x.CompletedAt,
                x.FailureReason))
            .ToListAsync(cancellationToken);
    }

    private static TransferResponse Map(Transfer transfer) =>
        new(
            transfer.Id,
            transfer.SourceAccountId,
            transfer.TargetAccountId,
            transfer.Amount,
            transfer.Reference,
            transfer.Status,
            transfer.CreatedAt,
            transfer.CompletedAt,
            transfer.FailureReason);
}
