using System.Collections.Concurrent;
using Application.Api.Models;
using Mqtt.Common;

namespace Application.Api.Services;

public sealed class GpioMessageService(ILogger<GpioMessageService> logger)
{
    private readonly ConcurrentDictionary<string, DeviceStatus> _devices = new(StringComparer.Ordinal);

    public void Process(FridgeTelemetry reading)
    {
        if (string.IsNullOrWhiteSpace(reading.DeviceId) ||
            reading.DeviceId.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_') ||
            !double.IsFinite(reading.Temperature) ||
            !double.IsFinite(reading.Humidity) || reading.Humidity is < 0 or > 100 ||
            reading.GpioPin < 0 || reading.GpioValue is not (0 or 1) ||
            reading.RecordedAt == default)
        {
            throw new ArgumentException("Invalid device telemetry.");
        }

        var status = new DeviceStatus(
            reading.DeviceId, reading.Temperature, reading.Humidity,
            reading.GpioPin, reading.GpioValue,
            DoorOpen: reading.GpioValue == 1,
            HighTemperature: reading.Temperature > 8,
            reading.RecordedAt, DateTimeOffset.UtcNow);

        // A delayed or redelivered old message should not replace a newer reading.
        _devices.AddOrUpdate(reading.DeviceId, status,
            (_, current) => status.RecordedAt >= current.RecordedAt ? status : current);

        logger.LogInformation(
            "Processed {DeviceId}: temperature={Temperature}C, humidity={Humidity}%, GPIO {GpioPin}={GpioValue}, doorOpen={DoorOpen}",
            status.DeviceId, status.Temperature, status.Humidity,
            status.GpioPin, status.GpioValue, status.DoorOpen);

        if (status.HighTemperature)
        {
            logger.LogWarning("High temperature for {DeviceId}: {Temperature}C", status.DeviceId, status.Temperature);
        }
    }

    public DeviceStatus? GetDevice(string deviceId) => _devices.GetValueOrDefault(deviceId);

    public DeviceStatus[] GetDevices() => _devices.Values.OrderBy(device => device.DeviceId).ToArray();
}
