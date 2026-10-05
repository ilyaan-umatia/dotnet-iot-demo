using Device.Gateway.Configuration;
using Device.Gateway.Connection;
using Device.Gateway.Sparkplug;
using Google.Protobuf;

namespace Device.Gateway.Simulation;

public sealed class FridgeSimulator(MqttConnection connection, GatewaySettings settings)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var device = settings.Device;
        // Fixed readings show normal temperature, an open door, and recovery.
        (double Temperature, double Humidity, bool DoorOpen)[] readings =
        [
            (4.5, 65, false), (4.8, 66, false), (9.2, 67, true),
            (9.8, 68, true), (5.0, 69, false)
        ];
        ulong sequence = 1; // NBIRTH already used seq 0 in this MQTT session.
        var initial = readings[0];
        var birthTopic = SparkplugTopics.DeviceBirth(settings.Sparkplug, device.DeviceId);
        await connection.PublishAsync(birthTopic,
            SparkplugPayloads.DeviceReading(sequence, initial.Temperature, initial.Humidity, initial.DoorOpen).ToByteArray(),
            cancellationToken);
        Console.WriteLine($"Published DBIRTH: {birthTopic} (seq {sequence})");
        foreach (var reading in readings.Skip(1))
        {
            await Task.Delay(TimeSpan.FromSeconds(device.ReadingIntervalSeconds), cancellationToken);
            sequence = (sequence + 1) % 256;
            await connection.PublishAsync(SparkplugTopics.DeviceData(settings.Sparkplug, device.DeviceId),
                SparkplugPayloads.DeviceReading(sequence, reading.Temperature, reading.Humidity, reading.DoorOpen).ToByteArray(),
                cancellationToken);
            Console.WriteLine($"DDATA {device.DeviceId}: temperature={reading.Temperature}C, humidity={reading.Humidity}%, doorOpen={reading.DoorOpen} (seq {sequence})");
        }
        await Task.Delay(TimeSpan.FromSeconds(device.ReadingIntervalSeconds), cancellationToken);
        sequence = (sequence + 1) % 256;
        var deathTopic = SparkplugTopics.DeviceDeath(settings.Sparkplug, device.DeviceId);
        await connection.PublishAsync(deathTopic, SparkplugPayloads.DeviceDeath(sequence).ToByteArray(), cancellationToken);
        Console.WriteLine($"Published DDEATH: {deathTopic} (seq {sequence})");
    }
}