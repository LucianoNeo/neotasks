using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NeoTasks.Worker;

public sealed class TaskAssignmentConsumer(IConfiguration configuration, ILogger<TaskAssignmentConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = RabbitSettings.Create(configuration);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(RabbitSettings.TaskAssignedQueue, durable: true, exclusive: false,
                    autoDelete: false, arguments: null, cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, 5, false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    try
                    {
                        var payload = JsonSerializer.Deserialize<TaskAssignment>(delivery.Body.Span, JsonSerializerOptions.Web)
                            ?? throw new InvalidDataException("Task notification payload is empty.");
                        await SendEmailAsync(payload, stoppingToken);
                        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Task assignment notification failed; RabbitMQ will redeliver it.");
                        await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
                    }
                };

                await channel.BasicConsumeAsync(RabbitSettings.TaskAssignedQueue, autoAck: false, consumer, stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RabbitMQ consumer disconnected; reconnecting.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task SendEmailAsync(TaskAssignment assignment, CancellationToken cancellationToken)
    {
        var host = configuration["Mail:Host"] ?? "mail";
        var port = configuration.GetValue("Mail:Port", 1025);
        var from = configuration["Mail:From"] ?? "neotasks@localhost";
        using var message = new MailMessage(from, assignment.Recipient)
        {
            Subject = $"Nova tarefa em {assignment.ProjectName}",
            Body = $"Olá {assignment.RecipientName},\n\nUma tarefa foi atribuída a você no projeto {assignment.ProjectName}: {assignment.TaskTitle}.\n\nNeoTasks"
        };
        using var client = new SmtpClient(host, port) { EnableSsl = false, DeliveryMethod = SmtpDeliveryMethod.Network };
        await client.SendMailAsync(message, cancellationToken);
    }

    private sealed record TaskAssignment(string Recipient, string RecipientName, string TaskTitle, string ProjectName);
}
