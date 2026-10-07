using Org.Eclipse.Tahu.Protobuf;

namespace Device.Gateway.Sparkplug;

public static class SparkplugPayloads
{
    public static Payload NodeBirth(ulong birthDeathSequence, ulong uptimeSeconds = 0)
    {
        var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = new Payload { Timestamp = timestamp, Seq = 0 };

        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = "bdSeq",
            Datatype = (uint)DataType.Int64,
            LongValue = birthDeathSequence,
            Timestamp = timestamp
        });

        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = "UptimeSeconds",
            Datatype = (uint)DataType.Int64,
            LongValue = uptimeSeconds,
            Timestamp = timestamp
        });
        return payload;
    }
    public static Payload NodeData(ulong sequence, ulong uptimeSeconds)
    {
        var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = new Payload { Timestamp = timestamp, Seq = sequence };
        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = "UptimeSeconds",
            Timestamp = timestamp,
            LongValue = uptimeSeconds
        });
        return payload;
    }
    public static Payload NodeDeath(ulong birthDeathSequence)
    {
        // The Will is prepared before connecting, so its timestamp is not the death time.
        var payload = new Payload();
        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = "bdSeq",
            Datatype = (uint)DataType.Int64,
            LongValue = birthDeathSequence
        });
        return payload;
    }
    public static Payload DeviceReading(ulong sequence, double temperature, double humidity, int gpioPin, int gpioValue)
    {
        var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = new Payload { Timestamp = timestamp, Seq = sequence };
        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = "Temperature", Datatype = (uint)DataType.Double,
            DoubleValue = temperature, Timestamp = timestamp
        });
        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = "Humidity", Datatype = (uint)DataType.Double,
            DoubleValue = humidity, Timestamp = timestamp
        });
        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = "GpioPin", Datatype = (uint)DataType.Int32,
            IntValue = (uint)gpioPin, Timestamp = timestamp
        });
        payload.Metrics.Add(new Payload.Types.Metric
        {
            Name = "GpioValue", Datatype = (uint)DataType.Int32,
            IntValue = (uint)gpioValue, Timestamp = timestamp
        });
        return payload;
    }

    public static Payload DeviceDeath(ulong sequence) => new()
    {
        Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Seq = sequence
    };
}
