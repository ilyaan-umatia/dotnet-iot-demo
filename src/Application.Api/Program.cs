using Application.Api.Configuration;
using Application.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration.SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables().AddCommandLine(args);
builder.Services.Configure<ServiceBusSettings>(builder.Configuration.GetSection("ServiceBus"));
var databasePath = builder.Configuration["Storage:DatabasePath"]
    ?? Path.Combine(AppContext.BaseDirectory, "state", "backend.db");
builder.Services.AddSingleton(_ => new TelemetryStore(databasePath));
builder.Services.AddSingleton<GpioMessageService>();
builder.Services.AddHostedService<ServiceBusConsumer>();
builder.Services.AddControllers();

var app = builder.Build();
app.MapGet("/", () => "IoT application is running.");
app.MapControllers();
app.Run();
