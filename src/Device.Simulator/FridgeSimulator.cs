using System.Text;
using System.Text.Json;
using Microsoft.Azure.Devices.Client;
using Mqtt.Common;

namespace Device.Simulator;

internal static class FridgeSimulator
{
    private static readonly double[] Temperatures = [4.5, 4.8, 9.2, 9.8, 5.0];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Preview()
    {
        Console.WriteLine("Payload preview (offline):");
        foreach (var reading in CreateReadings("fridge-01"))
        {
            Console.WriteLine(JsonSerializer.Serialize(reading, JsonOptions));
        }
    }

    public static async Task RunAsync(string connectionString, CancellationToken cancellationToken)
    {
        var deviceSettings = IotHubConnectionStringBuilder.Create(connectionString);
        if (string.IsNullOrWhiteSpace(deviceSettings.DeviceId))
        {
            throw new ArgumentException("A device connection string is required.");
        }

        using var client = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt_Tcp_Only);
        client.OperationTimeoutInMilliseconds = 30_000;

        Console.WriteLine($"Connecting {deviceSettings.DeviceId} to {deviceSettings.HostName} (MQTT/TLS, port 8883)...");
        await client.OpenAsync(cancellationToken);
        Console.WriteLine($"Connected. Sending {Temperatures.Length} readings...");

        try
        {
            var sentCount = 0;
            foreach (var reading in CreateReadings(deviceSettings.DeviceId))
            {
                var payload = JsonSerializer.Serialize(reading, JsonOptions);
                using var message = new Message(Encoding.UTF8.GetBytes(payload))
                {
                    ContentType = "application/json",
                    ContentEncoding = "utf-8",
                    MessageId = Guid.NewGuid().ToString("N")
                };

                await client.SendEventAsync(message, cancellationToken);
                sentCount++;
                Console.WriteLine($"Sent {sentCount}/{Temperatures.Length}: {payload}");

                if (sentCount < Temperatures.Length)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }

            Console.WriteLine($"{sentCount} messages sent to IoT Hub.");
        }
        finally
        {
            await client.CloseAsync(CancellationToken.None);
        }
    }

    private static IEnumerable<FridgeTelemetry> CreateReadings(string deviceId)
    {
        for (var index = 0; index < Temperatures.Length; index++)
        {
            yield return new FridgeTelemetry
            {
                DeviceId = deviceId,
                Temperature = Temperatures[index],
                Humidity = 65 + index,
                RecordedAt = DateTimeOffset.UtcNow,
                // Demo convention: simulated pin 17 reports 1 = door open, 0 = closed.
                GpioPin = 17,
                GpioValue = index is 2 or 3 ? 1 : 0
            };
        }
    }
}
