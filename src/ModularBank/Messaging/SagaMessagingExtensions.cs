namespace ModularBank.Messaging;

public static class SagaMessagingExtensions
{
    public static IServiceCollection AddSagaMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RabbitMqSagaOptions>(
            configuration.GetSection(RabbitMqSagaOptions.SectionName));

        services.AddHostedService<AccountsTransferRequestedConsumer>();
        services.AddHostedService<AccountsOutboxPublisher>();
        services.AddHostedService<AuditTransferResultConsumer>();

        return services;
    }
}
