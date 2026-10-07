using Application.Api.Models;
using Application.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Application.Api.Controllers;

[ApiController]
[Route("gateways")]
public sealed class GatewaysController(GpioMessageService service) : ControllerBase
{
    [HttpGet]
    public GatewayStatus[] GetAll() => service.GetGateways();
}
