using System.Data;
using Microsoft.EntityFrameworkCore;
using ModularBank.Modules.Accounts.Domain;
using ModularBank.Modules.Accounts.Infrastructure;
using ModularBank.Modules.Audit.Application;

namespace ModularBank.Modules.Accounts.Api;

public static class InternalTransfersEndpoints
{
    public static IEndpointRouteBuilder MapInternalTransfersEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/transfers/accounts")
            .WithTags("Internal Transfers");

        group.MapPost("/apply", ApplyTransferAsync);
        group.MapGet("/ownership", CheckOwnershipAsync);

        return app;
    }

    private static async Task<IResult> ApplyTransferAsync(
        HttpContext httpContext,
        ApplyAccountsTransferRequest request,
        AccountsDbContext db,
        IAuditService auditService,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!HasValidApiKey(httpContext, configuration))
            return Results.Unauthorized();

        if (request.TransferId == Guid.Empty ||
            request.UserId == Guid.Empty ||
            request.SourceAccountId == Guid.Empty ||
            request.TargetAccountId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                detail = "Los identificadores de la transferencia son obligatorios."
            });
        }

        if (request.SourceAccountId == request.TargetAccountId)
            return Results.BadRequest(new { detail = "Las cuentas deben ser diferentes." });

        if (request.Amount <= 0)
            return Results.BadRequest(new { detail = "El monto debe ser mayor que cero." });

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var alreadyProcessed = await db.ProcessedTransferCommands
            .AnyAsync(x => x.TransferId == request.TransferId, cancellationToken);

        if (alreadyProcessed)
        {
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new
            {
                transferId = request.TransferId,
                alreadyProcessed = true
            });
        }

        var source = await db.Accounts
            .SingleOrDefaultAsync(
                x => x.Id == request.SourceAccountId,
                cancellationToken);

        if (source is null)
            return Results.NotFound(new { detail = "Cuenta origen no encontrada." });

        if (source.UserId != request.UserId)
            return Results.Json(
                new { detail = "La cuenta origen no pertenece al usuario." },
                statusCode: StatusCodes.Status403Forbidden);

        var target = await db.Accounts
            .SingleOrDefaultAsync(
                x => x.Id == request.TargetAccountId,
                cancellationToken);

        if (target is null)
            return Results.NotFound(new { detail = "Cuenta destino no encontrada." });

        if (source.Balance < request.Amount)
            return Results.Conflict(new { detail = "Saldo insuficiente." });

        source.Balance -= request.Amount;
        target.Balance += request.Amount;

        db.ProcessedTransferCommands.Add(
            ProcessedTransferCommand.Create(
                request.TransferId,
                request.UserId,
                request.SourceAccountId,
                request.TargetAccountId,
                request.Amount,
                request.Reference));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        try
        {
            await auditService.RecordAsync(
                request.UserId,
                "TRANSFER_EXECUTED",
                new Dictionary<string, string>
                {
                    ["transferId"] = request.TransferId.ToString(),
                    ["amount"] = request.Amount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["sourceAccountId"] = request.SourceAccountId.ToString(),
                    ["targetAccountId"] = request.TargetAccountId.ToString()
                });
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("InternalTransfers")
                .LogWarning(
                    ex,
                    "No se pudo registrar auditoría para la transferencia {TransferId}.",
                    request.TransferId);
        }

        return Results.Ok(new
        {
            transferId = request.TransferId,
            alreadyProcessed = false
        });
    }

    private static async Task<IResult> CheckOwnershipAsync(
        HttpContext httpContext,
        Guid userId,
        Guid accountId,
        AccountsDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!HasValidApiKey(httpContext, configuration))
            return Results.Unauthorized();

        var owns = await db.Accounts
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == accountId && x.UserId == userId,
                cancellationToken);

        return Results.Ok(new { owns });
    }

    private static bool HasValidApiKey(
        HttpContext context,
        IConfiguration configuration)
    {
        var expected = configuration["Transfers:InternalApiKey"];

        return !string.IsNullOrWhiteSpace(expected) &&
               context.Request.Headers.TryGetValue(
                   "X-Internal-Api-Key",
                   out var provided) &&
               string.Equals(
                   provided.ToString(),
                   expected,
                   StringComparison.Ordinal);
    }
}

public sealed record ApplyAccountsTransferRequest(
    Guid TransferId,
    Guid UserId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    decimal Amount,
    string? Reference);
