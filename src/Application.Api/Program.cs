using Application.Api.Configuration;
using Application.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration.SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables().AddCommandLine(args);
builder.Services.Configure<ServiceBusSettings>(builder.Configuration.GetSection("ServiceBus"));
builder.Services.AddSingleton<GpioMessageService>();
builder.Services.AddHostedService<ServiceBusConsumer>();
builder.Services.AddControllers();

var app = builder.Build();
app.MapGet("/", () => "IoT application is running.");
app.MapControllers();
app.Run();
