namespace Application.Api.Models;

public sealed record DeviceStatus(string GroupId, string GatewayId, string DeviceId, bool Online,
    double Temperature, double Humidity, int GpioPin, int GpioValue, bool DoorOpen, bool HighTemperature,
    DateTimeOffset? RecordedAt, DateTimeOffset UpdatedAt);
