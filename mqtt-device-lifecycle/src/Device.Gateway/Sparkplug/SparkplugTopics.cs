using Device.Gateway.Configuration;

namespace Device.Gateway.Sparkplug;

public static class SparkplugTopics
{
    public static string NodeBirth(SparkplugSettings settings)
    {
        ValidateIdentifier(settings.GroupId, nameof(settings.GroupId));
        ValidateIdentifier(settings.EdgeNodeId, nameof(settings.EdgeNodeId));
        return $"spBv1.0/{settings.GroupId}/NBIRTH/{settings.EdgeNodeId}";
    }

    public static string NodeDeath(SparkplugSettings settings)
    {
        ValidateIdentifier(settings.GroupId, nameof(settings.GroupId));
        ValidateIdentifier(settings.EdgeNodeId, nameof(settings.EdgeNodeId));
        return $"spBv1.0/{settings.GroupId}/NDEATH/{settings.EdgeNodeId}";
    }

    public static string DeviceBirth(SparkplugSettings settings, string deviceId) =>
        DeviceTopic(settings, deviceId, "DBIRTH");

    public static string DeviceData(SparkplugSettings settings, string deviceId) =>
        DeviceTopic(settings, deviceId, "DDATA");

    public static string DeviceDeath(SparkplugSettings settings, string deviceId) =>
        DeviceTopic(settings, deviceId, "DDEATH");

    private static string DeviceTopic(SparkplugSettings settings, string deviceId, string messageType)
    {
        ValidateIdentifier(settings.GroupId, nameof(settings.GroupId));
        ValidateIdentifier(settings.EdgeNodeId, nameof(settings.EdgeNodeId));
        ValidateIdentifier(deviceId, nameof(deviceId));
        return $"spBv1.0/{settings.GroupId}/{messageType}/{settings.EdgeNodeId}/{deviceId}";
    }

    private static void ValidateIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['/', '+', '#']) >= 0)
        {
            throw new ArgumentException($"{name} must be nonempty and must not contain /, + or #.");
        }
    }
}
