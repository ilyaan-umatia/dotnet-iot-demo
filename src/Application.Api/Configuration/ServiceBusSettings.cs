namespace Application.Api.Configuration;

public sealed class ServiceBusSettings
{
    public string ConnectionString { get; set; } = "";
    public string QueueName { get; set; } = "device-messages";
}
