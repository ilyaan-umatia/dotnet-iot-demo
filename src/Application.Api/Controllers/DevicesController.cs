using Application.Api.Models;
using Application.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Application.Api.Controllers;

[ApiController]
[Route("devices")]
public sealed class DevicesController(GpioMessageService service) : ControllerBase
{
    [HttpGet]
    public DeviceStatus[] GetAll() => service.GetDevices();

    [HttpGet("{deviceId}")]
    public ActionResult<DeviceStatus> GetById(string deviceId) =>
        service.GetDevice(deviceId) is { } device ? Ok(device) : NotFound();
}
