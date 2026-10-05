namespace NeoTasks.Worker;

internal static class RabbitSettings
{
    public const string TaskAssignedQueue = "neotasks.task-assigned.v1";

    public static RabbitMQ.Client.ConnectionFactory Create(IConfiguration configuration)
    {
        var factory = new RabbitMQ.Client.ConnectionFactory
        {
            HostName = configuration["RabbitMq:Host"] ?? "rabbitmq",
            UserName = configuration["RabbitMq:Username"] ?? "neotasks",
            Password = configuration["RabbitMq:Password"] ?? "neotasks-local-only",
            ClientProvidedName = "neotasks:assignment-notifications"
        };
        return factory;
    }
}
