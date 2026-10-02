namespace Mqtt.Common;

public sealed class FridgeTelemetry
{
    public string DeviceId { get; set; } = "";
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public int GpioPin { get; set; }
    public int GpioValue { get; set; }
}
