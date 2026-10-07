using System.Buffers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Device.Gateway.Configuration;
using IoT.Contracts;
using Microsoft.Azure.Devices.Client;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace Device.Gateway.Cloud;

public sealed class CloudForwarder : IDisposable
{
    private readonly IMqttClient _mqtt = new MqttClientFactory().CreateMqttClient();
    private readonly MqttClientOptions _options;
    private readonly X509Certificate2? _certificate;
    private readonly DeviceClient? _azure;
    private readonly string _filter;
    private readonly SparkplugDecoder _decoder = new();
    private readonly Channel<LifecycleEvent> _pending = Channel.CreateBounded<LifecycleEvent>(200);
    private readonly object _gate = new();
    private TaskCompletionSource _subscribed = NewSignal();
    private int _forwardedDeath = -1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CloudForwarder(GatewaySettings settings)
    {
        var connectionString = settings.IoTHub.DeviceConnectionString;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var identity = IotHubConnectionStringBuilder.Create(connectionString).DeviceId;
            if (identity != settings.Sparkplug.EdgeNodeId)
                throw new ArgumentException("IoT Hub credentials must belong to the gateway's EdgeNodeId.");
            _azure = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt_Tcp_Only);
            _azure.OperationTimeoutInMilliseconds = 15_000;
        }

        var mqtt = settings.Mqtt;
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(mqtt.Host, mqtt.Port)
            .WithClientId(mqtt.ClientId + "-cloud-forwarder")
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(mqtt.ConnectTimeoutSeconds))
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(mqtt.KeepAliveSeconds));
        if (mqtt.UseTls)
        {
            _certificate = X509Certificate2.CreateFromPem(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, mqtt.BrokerCertificatePath)));
            builder.WithTlsOptions(new MqttClientTlsOptionsBuilder()
                .WithTrustChain(new X509Certificate2Collection(_certificate)).Build());
        }
        _options = builder.Build();
        _filter = $"spBv1.0/{settings.Sparkplug.GroupId}/+/{settings.Sparkplug.EdgeNodeId}/#";
        _mqtt.DisconnectedAsync += _ =>
        {
            lock (_gate)
                if (_subscribed.Task.IsCompleted) _subscribed = NewSignal();
            return Task.CompletedTask;
        };
        _mqtt.ApplicationMessageReceivedAsync += args =>
        {
            try
            {
                var item = _decoder.Decode(args.ApplicationMessage.Topic, args.ApplicationMessage.Payload.ToArray());
                if (item.MessageType == "NBIRTH") Volatile.Write(ref _forwardedDeath, -1);
                if (!_pending.Writer.TryWrite(item))
                    Console.WriteLine($"Cloud buffer full. Dropped {item.MessageType}: {item.EventId}");
            }
            catch (Exception error) when (error is ArgumentException or Google.Protobuf.InvalidProtocolBufferException)
            {
                Console.WriteLine($"Rejected MQTT message: {error.Message}");
            }
            return Task.CompletedTask;
        };
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WaitForSubscriptionAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task ready;
            lock (_gate)
            {
                if (_mqtt.IsConnected && _subscribed.Task.IsCompletedSuccessfully) return;
                if (_subscribed.Task.IsCompleted) _subscribed = NewSignal();
                ready = _subscribed.Task;
            }
            await ready.WaitAsync(cancellationToken);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var forwarding = ForwardAsync(session.Token);
        var factory = new MqttClientFactory();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (forwarding.IsCompleted) await forwarding;
                try
                {
                    if (!_mqtt.IsConnected)
                    {
                        _decoder.Reset();
                        await _mqtt.ConnectAsync(_options, cancellationToken);
                        await _mqtt.SubscribeAsync(factory.CreateSubscribeOptionsBuilder()
                            .WithTopicFilter(_filter, MqttQualityOfServiceLevel.AtLeastOnce).Build(), cancellationToken);
                        lock (_gate)
                            if (_mqtt.IsConnected) _subscribed.TrySetResult();
                        Console.WriteLine($"Cloud forwarder subscribed: {_filter}");
                    }
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    Console.WriteLine($"Forwarder broker connection failed ({error.GetType().Name}). Retry in 2s.");
                    // A failed subscription must reconnect and subscribe before announcing readiness.
                    if (_mqtt.IsConnected)
                        await _mqtt.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }
        finally
        {
            session.Cancel();
            try { await forwarding; }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            if (_mqtt.IsConnected)
            {
                using var disconnect = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await _mqtt.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), disconnect.Token); }
                catch (Exception error) { Console.WriteLine($"Forwarder disconnect failed ({error.GetType().Name})."); }
            }
        }
    }

    private async Task ForwardAsync(CancellationToken cancellationToken)
    {
        if (_azure is null) Console.WriteLine("Azure forwarding disabled. IoT Hub credentials are blank.");
        await foreach (var item in _pending.Reader.ReadAllAsync(cancellationToken))
        {
            if (_azure is null) continue;
            while (true)
            {
                try
                {
                    using var message = new Message(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item, JsonOptions)))
                    {
                        ContentType = "application/json", ContentEncoding = "utf-8", MessageId = item.EventId
                    };
                    await _azure.SendEventAsync(message, cancellationToken).WaitAsync(cancellationToken);
                    Console.WriteLine($"Forwarded {item.MessageType}: {item.EventId}");
                    if (item.MessageType == "NDEATH") Volatile.Write(ref _forwardedDeath, (int)item.BdSeq!.Value);
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    Console.WriteLine($"Azure send failed ({error.GetType().Name}). Retry in 2s.");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }
    }

    public async Task WaitForDeathAsync(ulong bdSeq, CancellationToken cancellationToken)
    {
        if (_azure is null) return;
        // This also waits for earlier events, which are forwarded by the same queue reader.
        while (Volatile.Read(ref _forwardedDeath) != (int)bdSeq)
            await Task.Delay(100, cancellationToken);
    }

    public void Dispose()
    {
        _mqtt.Dispose();
        _azure?.Dispose();
        _certificate?.Dispose();
    }
}
