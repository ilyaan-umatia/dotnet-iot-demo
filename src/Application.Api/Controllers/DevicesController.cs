using Application.Api.Models;
using Application.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

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

    [HttpGet("{deviceId}/history")]
    public HistoryEntry[] GetHistory(string deviceId, [FromQuery, Range(1, 500)] int limit = 100,
        [FromQuery, Range(1, long.MaxValue)] long? before = null) => service.GetHistory(deviceId, limit, before);
}
