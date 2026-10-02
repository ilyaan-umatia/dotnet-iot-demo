using Device.Simulator;
using Microsoft.Extensions.Configuration;

if (args.Length == 1 && args[0] == "--preview")
{
    FridgeSimulator.Preview();
    return 0;
}

if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project src/Device.Simulator [-- --preview]");
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Local.json", optional: true)
        .Build();

    var connectionString = configuration["IoTHub:DeviceConnectionString"];
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("Device credentials are missing. Paste your device's Primary connection string into IoTHub:DeviceConnectionString in src/Device.Simulator/appsettings.Local.json.");
        return 1;
    }

    await FridgeSimulator.RunAsync(connectionString, cancellation.Token);
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Simulator stopped.");
    return 0;
}
catch (Exception exception)
{
    // Avoid printing SDK exception details that may contain credentials.
    Console.Error.WriteLine($"Simulator failed ({exception.GetType().Name}). Check your local JSON settings, device connection string, enabled device status and network access to port 8883.");
    return 1;
}
