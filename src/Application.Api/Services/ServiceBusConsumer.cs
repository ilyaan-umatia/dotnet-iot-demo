using System.Text.Json;
using Application.Api.Configuration;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Mqtt.Common;

namespace Application.Api.Services;

public sealed class ServiceBusConsumer(
    IOptions<ServiceBusSettings> options,
    GpioMessageService messageService,
    ILogger<ServiceBusConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            logger.LogWarning("Queue consumer is disabled. Set ServiceBus:ConnectionString in Application.Api/appsettings.Local.json and restart the API.");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.QueueName))
        {
            throw new InvalidOperationException("ServiceBus:QueueName is required.");
        }

        await using var client = new ServiceBusClient(settings.ConnectionString,
            new ServiceBusClientOptions { TransportType = ServiceBusTransportType.AmqpTcp });
        await using var processor = client.CreateProcessor(settings.QueueName,
            new ServiceBusProcessorOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1
            });

        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += args =>
        {
            // Log error category without exposing credentials from SDK exception details.
            logger.LogError("Service Bus error ({ErrorType}) during {ErrorSource}. Check the Listen policy, queue name and network access to port 5671.",
                args.Exception.GetType().Name, args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("Started queue consumer for {QueueName} via AMQP + TLS. Waiting for messages...", settings.QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        finally
        {
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var reading = JsonSerializer.Deserialize<FridgeTelemetry>(args.Message.Body.ToString(), JsonOptions);
            if (reading is null)
            {
                throw new ArgumentException("Missing device telemetry.");
            }

            // IoT Hub supplies the authenticated device identity as a message property.
            if (args.Message.ApplicationProperties.TryGetValue("iothub-connection-device-id", out var sourceDeviceId) &&
                !string.Equals(sourceDeviceId?.ToString(), reading.DeviceId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Payload device identity does not match IoT Hub.");
            }

            messageService.Process(reading);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            await args.DeadLetterMessageAsync(args.Message, "InvalidTelemetry",
                "Expected valid fridge readings and a GPIO value of 0 or 1.", args.CancellationToken);
            logger.LogWarning("Moved invalid message {MessageId} to the dead-letter queue.", args.Message.MessageId);
            return;
        }

        // Only remove a message from the active queue after successful processing.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        logger.LogInformation("Completed queue message {MessageId}", args.Message.MessageId);
    }
}
