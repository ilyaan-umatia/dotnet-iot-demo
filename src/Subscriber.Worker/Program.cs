using Mqtt.Common;
using Subscriber.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection(MqttSettings.SectionName));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
