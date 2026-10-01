using Microsoft.Extensions.Options;
using Mqtt.Common;
using MQTTnet;
using MQTTnet.Protocol;
using System.Security.Cryptography.X509Certificates;

namespace Publisher.Api;

public sealed class MqttPublisher
{
    private readonly MqttSettings _settings;
    private readonly IHostEnvironment _environment;

    public MqttPublisher(IOptions<MqttSettings> options, IHostEnvironment environment)
    {
        _settings = options.Value;
        _environment = environment;
    }

    public async Task PublishAsync(string payload, string deviceId, CancellationToken cancellationToken)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();

        var connectionBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.Host, _settings.Port);

        if (_settings.UseTls)
        {
            var brokerCertificatePath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _settings.BrokerCertificatePath));
            var trustedCertificate = new X509Certificate2Collection();
            trustedCertificate.ImportFromPem(File.ReadAllText(brokerCertificatePath));
            connectionBuilder.WithTlsOptions(new MqttClientTlsOptionsBuilder().WithTrustChain(trustedCertificate).Build());
        }

        var connectionOptions = connectionBuilder.Build();

        await client.ConnectAsync(connectionOptions, cancellationToken);

        var topic = _settings.Topic.Replace("{deviceId}", deviceId);
            
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await client.PublishAsync(message, cancellationToken);
        await client.DisconnectAsync();
    }
}
