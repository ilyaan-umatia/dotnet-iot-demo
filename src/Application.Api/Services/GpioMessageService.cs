using IoT.Contracts;
using Application.Api.Models;

namespace Application.Api.Services;

public sealed class GpioMessageService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GatewayStatus> _gateways = [];
    private readonly Dictionary<string, DeviceStatus> _devices = [];
    private readonly HashSet<string> _processed = [];
    private readonly Queue<string> _recent = [];

    public void Process(LifecycleEvent item)
    {
        Validate(item);
        lock (_gate)
        {
            if (_processed.Contains(item.EventId)) return;
            var gatewayKey = item.GroupId + "/" + item.GatewayId;
            _gateways.TryGetValue(gatewayKey, out var gateway);
            if (item.MessageType == "NBIRTH")
            {
                _gateways[gatewayKey] = new(item.GroupId, item.GatewayId, true, item.BdSeq,
                    item.Metrics.GetValueOrDefault("UptimeSeconds")?.Number, item.ObservedAt);
                MarkDevicesOffline(item.GroupId, item.GatewayId);
            }
            else if (item.MessageType == "NDEATH")
            {
                if (gateway is not null && gateway.BdSeq == item.BdSeq)
                {
                    _gateways[gatewayKey] = gateway with { Online = false, UpdatedAt = item.ObservedAt };
                    MarkDevicesOffline(item.GroupId, item.GatewayId);
                }
            }
            else
            {
                if (gateway is null || !gateway.Online)
                    throw new ArgumentException("Gateway birth required before updates.");
                if (item.MessageType == "NDATA")
                    _gateways[gatewayKey] = gateway with
                    {
                        UptimeSeconds = item.Metrics.GetValueOrDefault("UptimeSeconds")?.Number,
                        UpdatedAt = item.ObservedAt
                    };
                else
                {
                    var key = gatewayKey + "/" + item.DeviceId;
                    _devices.TryGetValue(key, out var device);
                    if (item.MessageType == "DDEATH")
                    {
                        if (device is null) throw new ArgumentException("Device birth required.");
                        _devices[key] = device with { Online = false, UpdatedAt = item.ObservedAt };
                    }
                    else
                    {
                        if (item.MessageType == "DDATA" && (device is null || !device.Online))
                            throw new ArgumentException("Device birth required before data.");
                        var temperature = item.Metrics.GetValueOrDefault("Temperature")?.Number ?? device?.Temperature;
                        var humidity = item.Metrics.GetValueOrDefault("Humidity")?.Number ?? device?.Humidity;
                        var gpioPin = item.Metrics.GetValueOrDefault("GpioPin")?.Number ?? device?.GpioPin;
                        var gpioValue = item.Metrics.GetValueOrDefault("GpioValue")?.Number ?? device?.GpioValue;
                        if (temperature is null || humidity is null || gpioPin is null || gpioValue is null)
                            throw new ArgumentException("Missing fridge metrics.");
                        // This simulated door sensor is active-high: 1 means open, 0 means closed.
                        var doorOpen = gpioValue == 1;
                        _devices[key] = new(item.GroupId, item.GatewayId, item.DeviceId!, true,
                            temperature.Value, humidity.Value, (int)gpioPin.Value, (int)gpioValue.Value, doorOpen, temperature > 8,
                            item.RecordedAt, item.ObservedAt);
                    }
                }
            }
            _processed.Add(item.EventId);
            _recent.Enqueue(item.EventId);
            if (_recent.Count > 1000) _processed.Remove(_recent.Dequeue());
        }
    }

    public static void Validate(LifecycleEvent item)
    {
        if (string.IsNullOrWhiteSpace(item.EventId) || string.IsNullOrWhiteSpace(item.GroupId) ||
            string.IsNullOrWhiteSpace(item.GatewayId) || item.Metrics is null || item.ObservedAt == default ||
            item.MessageType is not ("NBIRTH" or "NDATA" or "NDEATH" or "DBIRTH" or "DDATA" or "DDEATH"))
            throw new ArgumentException("Invalid lifecycle event.");
        if (item.MessageType.StartsWith('D') && string.IsNullOrWhiteSpace(item.DeviceId))
            throw new ArgumentException("Device ID required.");
        if (item.MessageType != "NDEATH" && (item.Seq is null or > 255 || item.RecordedAt is null))
            throw new ArgumentException("Timestamp and sequence required.");
        if (item.MessageType == "NDEATH" && item.Seq is not null)
            throw new ArgumentException("NDEATH must not contain seq.");
        if ((item.MessageType is "NBIRTH" or "NDEATH") && item.BdSeq is null or > 255)
            throw new ArgumentException("bdSeq required.");
        var expectedTopic = $"spBv1.0/{item.GroupId}/{item.MessageType}/{item.GatewayId}" +
            (item.MessageType.StartsWith('D') ? $"/{item.DeviceId}" : "");
        if (item.SourceTopic != expectedTopic || item.GroupId.Contains('/') || item.GatewayId.Contains('/') ||
            item.DeviceId?.Contains('/') == true)
            throw new ArgumentException("Topic identity mismatch.");
        if (item.MessageType == "NBIRTH" && item.Seq != 0)
            throw new ArgumentException("NBIRTH must start at seq 0.");
        foreach (var (name, value) in item.Metrics)
        {
            if (value is null || (value.Number is { } number && !double.IsFinite(number)))
                throw new ArgumentException("Invalid metric.");
            if (name == "Humidity" && value.Number is < 0 or > 100)
                throw new ArgumentException("Humidity must be between 0 and 100.");
            if (name == "GpioPin" && (value.Number is null || value.Number < 0 ||
                value.Number > int.MaxValue || value.Number != Math.Truncate(value.Number.Value)))
                throw new ArgumentException("GPIO pin must be a non-negative integer.");
            if (name == "GpioValue" && value.Number is not (0 or 1))
                throw new ArgumentException("GPIO value must be 0 or 1.");
        }
    }

    private void MarkDevicesOffline(string groupId, string gatewayId)
    {
        foreach (var (key, device) in _devices.ToArray())
            if (device.GroupId == groupId && device.GatewayId == gatewayId)
                _devices[key] = device with { Online = false };
    }

    public GatewayStatus[] GetGateways() { lock (_gate) return _gateways.Values.ToArray(); }
    public DeviceStatus[] GetDevices() { lock (_gate) return _devices.Values.ToArray(); }
    public DeviceStatus? GetDevice(string deviceId)
    {
        lock (_gate) return _devices.Values.FirstOrDefault(device => device.DeviceId == deviceId);
    }
}
