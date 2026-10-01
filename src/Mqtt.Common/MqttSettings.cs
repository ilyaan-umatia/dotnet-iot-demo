namespace Mqtt.Common;

public sealed class MqttSettings
{
    public const string SectionName = "Mqtt";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string Topic { get; set; } = "devices/messages";
    public bool UseTls { get; set; } = false;
    public string BrokerCertificatePath { get; set; } = "";
}
