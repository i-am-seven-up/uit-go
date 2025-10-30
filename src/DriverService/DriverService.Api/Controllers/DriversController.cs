using DriverService.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace DriverService.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DriversController : ControllerBase
    {
        private readonly IDriverService _svc;
        public DriversController(IDriverService svc) => _svc = svc;

        [HttpPut("{id:guid}/status")]
        public Task SetStatus(Guid id, [FromQuery] bool online, CancellationToken ct) => _svc.SetOnlineAsync(id, online, ct);

        [HttpPut("{id:guid}/location")]
        public Task UpdateLocation(Guid id, [FromQuery] double lat, [FromQuery] double lng, CancellationToken ct) =>
            _svc.UpdateLocationAsync(id, lat, lng, ct);
    }
}
