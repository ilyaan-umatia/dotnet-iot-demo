using Microsoft.Extensions.Options;
using Mqtt.Common;
using MQTTnet;
using System.Buffers;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Subscriber.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly MqttSettings _mqtt;
    private readonly IHostEnvironment _environment;

    public Worker(ILogger<Worker> logger, IOptions<MqttSettings> mqttOptions, IHostEnvironment environment)
    {
        _logger = logger;
        _mqtt = mqttOptions.Value;
        _environment = environment;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

       client.ApplicationMessageReceivedAsync += args =>
{
    var payload = Encoding.UTF8.GetString(
        args.ApplicationMessage.Payload.ToArray());

    try
    {
        var message = JsonSerializer.Deserialize<DeviceMessage>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (message is null ||
            string.IsNullOrWhiteSpace(message.DeviceId))
        {
            _logger.LogWarning(
                "Received a message without a valid DeviceId on {Topic}",
                args.ApplicationMessage.Topic);

            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Device {DeviceId}: temperature={Temperature}C, humidity={Humidity}%, recordedAt={RecordedAt}, topic={Topic}",
            message.DeviceId,
            message.Temperature,
            message.Humidity,
            message.RecordedAt,
            args.ApplicationMessage.Topic);

        if (message.Temperature > 8)
        {
            _logger.LogWarning(
                "High temperature for {DeviceId}: {Temperature}C",
                message.DeviceId,
                message.Temperature);
        }
    }
    catch (JsonException)
    {
        _logger.LogWarning(
            "Received invalid device JSON on {Topic}",
            args.ApplicationMessage.Topic);
    }

    return Task.CompletedTask;
};
        var connectionBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_mqtt.Host, _mqtt.Port);

        if (_mqtt.UseTls)
        {
            var brokerCertificatePath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _mqtt.BrokerCertificatePath));
            var trustedCertificate = new X509Certificate2Collection();
            trustedCertificate.ImportFromPem(File.ReadAllText(brokerCertificatePath));
            connectionBuilder.WithTlsOptions(new MqttClientTlsOptionsBuilder().WithTrustChain(trustedCertificate).Build());
        }

        var connectionOptions = connectionBuilder.Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                {
                    await client.ConnectAsync(connectionOptions, stoppingToken);

                    var subscriptionTopic = _mqtt.Topic.Replace("{deviceId}", "+");

                    var subscribeOptions = factory.CreateSubscribeOptionsBuilder()
                        .WithTopicFilter(subscriptionTopic)
                        .Build();

                    await client.SubscribeAsync(subscribeOptions, stoppingToken);
                    _logger.LogInformation("Subscribed to {Topic} via {Host}:{Port}", subscriptionTopic, _mqtt.Host, _mqtt.Port);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "MQTT connection lost. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
