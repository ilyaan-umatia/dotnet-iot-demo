using Mqtt.Common;
using Publisher.Api;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection(MqttSettings.SectionName));
builder.Services.AddSingleton<MqttPublisher>();
var app = builder.Build();

app.MapGet("/", () => "Publisher API is running. POST device readings to /publish.");

app.MapPost("/publish", async (
    DeviceMessage request,
    MqttPublisher publisher,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DeviceId))
    {
        return Results.BadRequest(new { error = "DeviceId is required." });
    }

    if (request.DeviceId.Any(c =>
    !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
{
    return Results.BadRequest(new
    {
        error = "DeviceId can contain only letters, numbers, hyphens and underscores."
    });
}

if (request.Humidity < 0 || request.Humidity > 100)
{
    return Results.BadRequest(new
    {
        error = "Humidity must be between 0 and 100."
    });
}

    try
    {
        var payload = JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await publisher.PublishAsync(payload, request.DeviceId, cancellationToken);

        return Results.Ok(new
        {
            status = "published",
            deviceId = request.DeviceId
        });
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        logger.LogError(exception, "Could not publish MQTT message");

        return Results.Problem(
            "Could not connect to or publish through the MQTT broker.",
            statusCode: 503);
    }
});

app.Run();
