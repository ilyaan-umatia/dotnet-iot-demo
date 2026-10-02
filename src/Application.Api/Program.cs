using Application.Api.Configuration;
using Application.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"), optional: true);
builder.Services.Configure<ServiceBusSettings>(builder.Configuration.GetSection("ServiceBus"));
builder.Services.AddSingleton<GpioMessageService>();
builder.Services.AddHostedService<ServiceBusConsumer>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    message = "Device application API is running. View /devices or /devices/fridge-01.",
    consumerConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["ServiceBus:ConnectionString"])
}));

app.MapGet("/devices", (GpioMessageService service) => Results.Ok(service.GetDevices()));

app.MapGet("/devices/{deviceId}", (string deviceId, GpioMessageService service) =>
    service.GetDevice(deviceId) is { } status
        ? Results.Ok(status)
        : Results.NotFound(new { error = "No readings have been processed for this device yet." }));

app.Run();
