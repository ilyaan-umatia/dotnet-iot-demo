using Device.Gateway.Configuration;
using Device.Gateway.Connection;
using Device.Gateway.Sparkplug;
using Google.Protobuf;

namespace Device.Gateway.Simulation;

public sealed class FridgeSimulator(MqttConnection connection, GatewaySettings settings, SparkplugSequence sequence)
{
    private static readonly (double Temperature, double Humidity, int GpioValue)[] Readings =
    [
        (4.5, 65, 0), (4.8, 66, 0), (9.2, 67, 1),
        (9.8, 68, 1), (5.0, 69, 0)
    ];
    private int _nextReading;
    private bool _offline;

    public async Task InitializeAsync(bool online, CancellationToken cancellationToken)
    {
        _offline = !online;
        if (online) await PublishBirthAsync(cancellationToken);
    }

    private async Task PublishBirthAsync(CancellationToken cancellationToken)
    {
        var reading = Readings[0];
        var current = sequence.Next();
        var topic = SparkplugTopics.DeviceBirth(settings.Sparkplug, settings.Device.DeviceId);
        await connection.PublishAsync(topic,
            SparkplugPayloads.DeviceReading(current, reading.Temperature, reading.Humidity, settings.Device.GpioPin, reading.GpioValue).ToByteArray(),
            cancellationToken);
        _nextReading = 1;
        Console.WriteLine($"Published DBIRTH: {topic} (seq {current})");
    }

    public async Task UpdateAsync(bool online, CancellationToken cancellationToken)
    {
        if (online && _offline)
        {
            _offline = false;
            await PublishBirthAsync(cancellationToken);
            return;
        }
        if (_offline) return;

        var current = sequence.Next();
        var deviceId = settings.Device.DeviceId;
        if (online)
        {
            var reading = Readings[_nextReading % Readings.Length];
            await connection.PublishAsync(SparkplugTopics.DeviceData(settings.Sparkplug, deviceId),
                SparkplugPayloads.DeviceReading(current, reading.Temperature, reading.Humidity, settings.Device.GpioPin, reading.GpioValue).ToByteArray(),
                cancellationToken);
            _nextReading++;
            Console.WriteLine($"DDATA {deviceId}: temperature={reading.Temperature}C, humidity={reading.Humidity}%, GPIO {settings.Device.GpioPin}={reading.GpioValue} (seq {current})");
            return;
        }

        var deathTopic = SparkplugTopics.DeviceDeath(settings.Sparkplug, deviceId);
        await connection.PublishAsync(deathTopic, SparkplugPayloads.DeviceDeath(current).ToByteArray(), cancellationToken);
        _offline = true;
        Console.WriteLine($"Published DDEATH: {deathTopic} (seq {current})");
    }
}
