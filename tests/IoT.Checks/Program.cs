using Application.Api.Services;
using Device.Gateway.Cloud;
using Device.Gateway.Sparkplug;
using Google.Protobuf;
using IoT.Contracts;
using Org.Eclipse.Tahu.Protobuf;
using System.Text.Json;

var decoder = new SparkplugDecoder();
var state = new GpioMessageService();
var prefix = "spBv1.0/warehouse-01/";
LifecycleEvent Send(string type, Payload payload)
{
    var topic = prefix + type + "/gateway-01" + (type.StartsWith('D') ? "/fridge-01" : "");
    var item = decoder.Decode(topic, payload.ToByteArray());
    var json = JsonSerializer.Serialize(item);
    state.Process(JsonSerializer.Deserialize<LifecycleEvent>(json)!);
    return item;
}
void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
void Reject(Action action)
{
    try { action(); } catch (ArgumentException) { return; }
    throw new Exception("Invalid event was accepted.");
}
Reject(() => Send("DDATA", SparkplugPayloads.DeviceReading(1, 5, 60, 17, 0)));
Send("NBIRTH", SparkplugPayloads.NodeBirth(7));
var birth = Send("DBIRTH", SparkplugPayloads.DeviceReading(1, 4.5, 65, 17, 0));
Check(!birth.Metrics.ContainsKey("DoorOpen"), "Gateway should send raw GPIO input.");
Check(state.GetDevices().Single() is { GpioPin: 17, GpioValue: 0, DoorOpen: false }, "GPIO low must mean door closed.");
Send("NDATA", SparkplugPayloads.NodeData(2, 5));
Send("DDATA", SparkplugPayloads.DeviceReading(3, 9.2, 67, 17, 1));
Check(state.GetDevices().Single() is { Online: true, Temperature: 9.2, HighTemperature: true, GpioPin: 17, GpioValue: 1, DoorOpen: true }, "Reading mismatch.");
state.Process(birth); // queue redelivery must not restore the older reading
Check(state.GetDevices().Single().Temperature == 9.2, "Duplicate overwrote latest reading.");
Send("DDEATH", SparkplugPayloads.DeviceDeath(4));
Check(!state.GetDevices().Single().Online && state.GetGateways().Single().Online, "Device outage affected gateway.");
Send("NDATA", SparkplugPayloads.NodeData(5, 10));
Send("DBIRTH", SparkplugPayloads.DeviceReading(6, 5, 69, 17, 0));
Check(state.GetDevices().Single().Online, "Device failed to recover.");
Check(!state.GetDevices().Single().DoorOpen, "GPIO low did not close the door on recovery.");
Reject(() => decoder.Decode(prefix + "NDEATH/gateway-01", SparkplugPayloads.NodeDeath(6).ToByteArray()));
Check(state.GetGateways().Single().Online, "Old death affected current session.");
Send("NDEATH", SparkplugPayloads.NodeDeath(7));
Check(!state.GetGateways().Single().Online && !state.GetDevices().Single().Online, "Node loss did not invalidate devices.");
Send("NBIRTH", SparkplugPayloads.NodeBirth(8, 12));
Send("DBIRTH", SparkplugPayloads.DeviceReading(1, 5, 69, 17, 0));
Reject(() => Send("NDATA", SparkplugPayloads.NodeData(3, 15)));
Reject(() => Send("NDATA", SparkplugPayloads.NodeData(4, 20)));
Send("NBIRTH", SparkplugPayloads.NodeBirth(9));
Send("DBIRTH", SparkplugPayloads.DeviceReading(1, 5, 69, 17, 0));
for (ulong seq = 2; seq <= 255; seq++) Send("NDATA", SparkplugPayloads.NodeData(seq, seq));
Send("NDATA", SparkplugPayloads.NodeData(0, 256));
Check(state.GetGateways().Single().UptimeSeconds == 256, "Sequence wrap failed.");
Reject(() => state.Process(birth with { EventId = "bad", GatewayId = "forged" }));
Reject(() => state.Process(birth with { EventId = "bad-humidity", Metrics = new() { ["Humidity"] = new(Number: 101) } }));
Reject(() => state.Process(birth with { EventId = "bad-gpio", Metrics = new() { ["GpioValue"] = new(Number: 2) } }));
Reject(() => state.Process(birth with { EventId = "bad-pin", Metrics = new() { ["GpioPin"] = new(Number: -1) } }));
Reject(() => state.Process(birth with { EventId = "fractional-pin", Metrics = new() { ["GpioPin"] = new(Number: 17.5) } }));
Console.WriteLine("PASS: GPIO door mapping, binary decoding, JSON roundtrip, lifecycle/recovery, duplicates, stale death, sequence gaps/wrap and validation.");
