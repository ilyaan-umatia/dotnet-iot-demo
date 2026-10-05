using Device.Gateway.Configuration;
using Device.Gateway.Sparkplug;
using Google.Protobuf;
using Device.Gateway.Simulation;

namespace Device.Gateway.Connection;

public sealed class GatewayRunner(MqttConnection connection, GatewaySettings settings)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var mqtt = settings.Mqtt;
        if (mqtt.RetryDelaySeconds <= 0 || mqtt.MaxRetryDelaySeconds < mqtt.RetryDelaySeconds)
            throw new ArgumentException("Retry delays must be positive and the maximum must be at least the initial delay.");
        if (settings.Device.ReadingIntervalSeconds <= 0)
            throw new ArgumentException("Reading interval must be positive.");
        _ = SparkplugTopics.DeviceBirth(settings.Sparkplug, settings.Device.DeviceId);
        var topic = SparkplugTopics.NodeBirth(settings.Sparkplug);
        var delaySeconds = mqtt.RetryDelaySeconds;
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"Connecting {mqtt.ClientId} to {mqtt.Host}:{mqtt.Port}...");
                await connection.ConnectAsync(cancellationToken);
                Console.WriteLine("Connected.");
                // Birth and Will messages share the counter reserved for this connection.
                await connection.PublishAsync(topic, SparkplugPayloads.NodeBirth(connection.BirthDeathSequence).ToByteArray(), cancellationToken);
                Console.WriteLine($"Published NBIRTH: {topic}");
                if (!connection.MarkOnline())
                    throw new InvalidOperationException("Connection closed during the birth announcement.");
                attempt = 0;
                delaySeconds = mqtt.RetryDelaySeconds;
                await RunDeviceSessionAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                Console.WriteLine($"Connection failed: {error.Message}");
            }
            cancellationToken.ThrowIfCancellationRequested();
            await connection.PrepareRetryAsync(cancellationToken);
            attempt++;
            Console.WriteLine($"Retry {attempt} in {delaySeconds}s.");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            delaySeconds = (int)Math.Min((long)delaySeconds * 2, mqtt.MaxRetryDelaySeconds);
        }
    }
    private async Task RunDeviceSessionAsync(CancellationToken cancellationToken)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var disconnected = connection.WaitForDisconnectAsync(session.Token);
        var simulation = new FridgeSimulator(connection, settings).RunAsync(session.Token);
        try
        {
            if (await Task.WhenAny(disconnected, simulation) == simulation)
                await simulation;
            // The gateway stays online after the simulated device goes offline.
            await disconnected;
        }
        finally
        {
            session.Cancel();
            try { await simulation; }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            try { await disconnected; }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        }
    }
}