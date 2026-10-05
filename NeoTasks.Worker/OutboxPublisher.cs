using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeoTasks;
using RabbitMQ.Client;

namespace NeoTasks.Worker;

public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = RabbitSettings.Create(configuration);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    stoppingToken);
                await channel.QueueDeclareAsync(RabbitSettings.TaskAssignedQueue, durable: true, exclusive: false,
                    autoDelete: false, arguments: null, cancellationToken: stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<TasksDb>();
                    var batch = await db.Outbox.Where(message => message.PublishedAtUtc == null)
                        .OrderBy(message => message.OccurredAtUtc).Take(25).ToListAsync(stoppingToken);

                    foreach (var message in batch)
                    {
                        var properties = new BasicProperties
                        {
                            ContentType = "application/json",
                            DeliveryMode = DeliveryModes.Persistent,
                            MessageId = message.Id.ToString("N")
                        };
                        await channel.BasicPublishAsync("", RabbitSettings.TaskAssignedQueue, mandatory: true,
                            basicProperties: properties, body: Encoding.UTF8.GetBytes(message.Payload), cancellationToken: stoppingToken);
                        message.PublishedAtUtc = DateTime.UtcNow;
                    }

                    if (batch.Count > 0) await db.SaveChangesAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(batch.Count == 0 ? 3 : 1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Outbox dispatch failed; pending events will be retried.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            if (!stoppingToken.IsCancellationRequested) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
