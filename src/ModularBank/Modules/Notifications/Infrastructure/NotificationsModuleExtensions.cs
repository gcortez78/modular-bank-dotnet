using ModularBank.Modules.Notifications.Application;

namespace ModularBank.Modules.Notifications.Infrastructure;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = configuration["Notifications:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var serviceUri))
        {
            throw new InvalidOperationException(
                "Notifications:BaseUrl debe ser una URL absoluta válida.");
        }

        var internalApiKey = configuration["Notifications:InternalApiKey"];
        if (string.IsNullOrWhiteSpace(internalApiKey)
            || internalApiKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Notifications:InternalApiKey debe tener al menos 32 caracteres.");
        }

        services.AddHttpClient<INotificationsService, NotificationsHttpClient>(
            client =>
            {
                client.BaseAddress = serviceUri;
                client.Timeout = TimeSpan.FromSeconds(3);
                client.DefaultRequestHeaders.Add(
                    "X-Internal-Api-Key",
                    internalApiKey);
            });

        return services;
    }
}
