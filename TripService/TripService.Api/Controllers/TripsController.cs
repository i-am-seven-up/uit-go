using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TripService.Application.Abstractions;
using TripService.Domain.Entities;

namespace TripService.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TripsController : ControllerBase
    {
        private readonly ITripService _tripService;
        public TripsController(ITripService svc) => _tripService = svc;

        [HttpPost]
        public Task<Trip> Create([FromBody] Trip trip, CancellationToken ct) => _tripService.CreateAsync(trip, ct);

        [HttpGet("{id:guid}")]
        public Task<Trip?> Get(Guid id, CancellationToken ct) => _tripService.GetAsync(id, ct);

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        {
            await _tripService.CancelAsync(id, ct);
            return NoContent();
        }

    }
}
