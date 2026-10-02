namespace Application.Api.Models;

public sealed record DeviceStatus(
    string DeviceId,
    double Temperature,
    double Humidity,
    int GpioPin,
    int GpioValue,
    bool DoorOpen,
    bool HighTemperature,
    DateTimeOffset RecordedAt,
    DateTimeOffset ProcessedAt);
