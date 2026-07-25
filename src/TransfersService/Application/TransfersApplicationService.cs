using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransfersService.Application.Contracts;
using TransfersService.Domain;
using TransfersService.Infrastructure;
using TransfersService.Messaging;

namespace TransfersService.Application;

public sealed class TransfersApplicationService(
    TransfersDbContext db,
    IMonolithBankingClient bankingClient,
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

        db.Transfers.Add(transfer);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await bankingClient.ApplyTransferAsync(
                new AccountsTransferCommand(
                    transfer.Id,
                    transfer.UserId,
                    transfer.SourceAccountId,
                    transfer.TargetAccountId,
                    transfer.Amount,
                    transfer.Reference),
                cancellationToken);

            var occurredAt = DateTimeOffset.UtcNow;
            var integrationEvent = new TransferCompletedIntegrationEvent(
                EventId: Guid.NewGuid(),
                TransferId: transfer.Id,
                UserId: transfer.UserId,
                SourceAccountId: transfer.SourceAccountId,
                TargetAccountId: transfer.TargetAccountId,
                Amount: transfer.Amount,
                Currency: "BOB",
                Reference: transfer.Reference,
                OccurredAtUtc: occurredAt);

            var payload = JsonSerializer.Serialize(
                integrationEvent,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            transfer.Complete(occurredAt);
            db.OutboxMessages.Add(
                OutboxMessage.Create(
                    integrationEvent.EventId,
                    nameof(TransferCompletedIntegrationEvent),
                    _rabbit.RoutingKey,
                    payload,
                    occurredAt));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Transferencia {TransferId} completada; evento {EventId} almacenado en Outbox.",
                transfer.Id,
                integrationEvent.EventId);

            return Map(transfer);
        }
        catch (MonolithBankingException ex)
        {
            transfer.Fail(ex.Message);
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
        catch (HttpRequestException ex)
        {
            transfer.Fail("No fue posible contactar al módulo Accounts.");
            await db.SaveChangesAsync(cancellationToken);

            logger.LogError(
                ex,
                "No fue posible contactar al monolito para la transferencia {TransferId}.",
                transfer.Id);

            throw new MonolithBankingException(
                StatusCodes.Status503ServiceUnavailable,
                "El módulo Accounts no está disponible temporalmente.");
        }
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
