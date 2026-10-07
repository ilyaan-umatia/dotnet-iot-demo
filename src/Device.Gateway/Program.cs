using Device.Gateway.Configuration;
using Device.Gateway.Cloud;
using Device.Gateway.Connection;
using Microsoft.Extensions.Configuration;

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.Cancel();
};
try
{
    var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json").Build();
    var settings = configuration.Get<GatewaySettings>()
        ?? throw new InvalidOperationException("Could not read appsettings.json.");
    using var connection = new MqttConnection(settings.Mqtt, settings.Sparkplug);
    using var forwarder = new CloudForwarder(settings);
    using var forwardingStop = new CancellationTokenSource();
    var forwarding = forwarder.RunAsync(forwardingStop.Token);
    var runner = new GatewayRunner(connection, settings, forwarder);
    var input = Task.Run(() =>
    {
        while (!stop.IsCancellationRequested)
        {
            var command = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (command is null or "" or "stop") break;
            if (command == "offline") runner.SetDeviceOnline(false);
            else if (command == "online") runner.SetDeviceOnline(true);
            else Console.WriteLine("Commands: offline, online, stop.");
        }
    });
    var running = runner.RunAsync(stop.Token);
    try
    {
        if (await Task.WhenAny(input, running, forwarding) != running)
            stop.Cancel();
        await running;
        if (forwarding.IsCompleted) await forwarding;
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested)
    {
        Console.WriteLine("Stopping...");
    }
    finally
    {
        stop.Cancel();
        var connected = connection.IsConnected;
        using var disconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.DisconnectAsync(disconnectTimeout.Token);
        try
        {
            if (connected && !forwarding.IsCompleted)
            {
                using var flushTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await forwarder.WaitForDeathAsync(connection.BirthDeathSequence, flushTimeout.Token);
            }
        }
        catch (OperationCanceledException) { Console.WriteLine("Final NDEATH was not confirmed by Azure."); }
        finally
        {
            forwardingStop.Cancel();
            try { await forwarding; }
            catch (OperationCanceledException) when (forwardingStop.IsCancellationRequested) { }
        }
    }
    Console.WriteLine("Disconnected.");
}
catch (Exception error)
{
    Console.Error.WriteLine($"Gateway error: {error.Message}");
    Environment.ExitCode = 1;
}
