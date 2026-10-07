namespace Application.Api.Models;

public sealed record GatewayStatus(string GroupId, string GatewayId, bool Online,
    ulong? BdSeq, double? UptimeSeconds, DateTimeOffset UpdatedAt);
