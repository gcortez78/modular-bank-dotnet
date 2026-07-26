using System.Security.Cryptography;
using System.Text;

namespace FinBank.NotificationsService.Api;

public sealed class InternalApiKeyFilter(IConfiguration configuration) : IEndpointFilter
{
    private const string HeaderName = "X-Internal-Api-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var expected = configuration["InternalApiKey"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            return Results.Problem(
                title: "Configuración inválida",
                detail: "InternalApiKey no está configurada.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var supplied))
        {
            return Results.Unauthorized();
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());

        var valid = expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);

        if (!valid)
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
