using System.Text.Json;
using Device.Gateway.Configuration;
using Device.Gateway.Connection;
using Device.Gateway.Sparkplug;
using Google.Protobuf;

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.Cancel();
};
try
{
    var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    var settings = JsonSerializer.Deserialize<GatewaySettings>(File.ReadAllText(settingsPath))
        ?? throw new InvalidOperationException("Could not read appsettings.json.");
    var birthTopic = SparkplugTopics.NodeBirth(settings.Sparkplug);
    if (args.Contains("--preview"))
    {
        Console.WriteLine(birthTopic);
        Console.WriteLine(JsonFormatter.Default.Format(SparkplugPayloads.NodeBirth(0)));
        return;
    }
    using var connection = new MqttConnection(settings.Mqtt, settings.Sparkplug);
    var runner = new GatewayRunner(connection, settings);
    var input = Task.Run(() => Console.ReadLine());
    var running = runner.RunAsync(stop.Token);
    try
    {
        if (await Task.WhenAny(input, running) == input)
            stop.Cancel();
        await running;
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested)
    {
        Console.WriteLine("Stopping...");
    }
    finally
    {
        stop.Cancel();
        using var disconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.DisconnectAsync(disconnectTimeout.Token);
    }
    Console.WriteLine("Disconnected.");
}
catch (Exception error)
{
    Console.Error.WriteLine($"Gateway error: {error.Message}");
    Environment.ExitCode = 1;
}