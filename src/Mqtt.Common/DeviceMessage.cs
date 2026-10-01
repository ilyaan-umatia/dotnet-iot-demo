namespace Mqtt.Common;

public sealed class DeviceMessage
{
    public string DeviceId { get; set; } = "";
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}