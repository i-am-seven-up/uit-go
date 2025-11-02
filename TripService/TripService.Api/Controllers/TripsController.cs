using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TripService.Application.Abstractions;
using TripService.Domain.Entities;

namespace TripService.Api.Controllers
{
    [Route("api/trips")]
    [ApiController]
    public class TripsController : ControllerBase
    {
        private readonly ITripService _tripService;
        public TripsController(ITripService svc) => _tripService = svc;

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateTripRequest req, CancellationToken ct)
        {
            // TODO: sau này lấy từ JWT (user id). Hiện tại tạm hardcode.
            var passengerId = Guid.NewGuid();

            var trip = new Trip
            {
                Id = Guid.NewGuid(),
                PassengerId = passengerId,
                StartLat = req.PickupLat,
                StartLng = req.PickupLng,
                EndLat = req.DropoffLat,
                EndLng = req.DropoffLng,
                // Status sẽ set ở CreateAsync
            };

            var created = await _tripService.CreateAsync(trip, ct);

            return Ok(created);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var result = await _tripService.GetAsync(id, ct);
            if (result is null) return NotFound();
            return Ok(result);
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        {
            await _tripService.CancelAsync(id, ct);
            return NoContent();
        }

        [HttpGet("health")]
        public IActionResult Health() => Ok("trip ok");

    }
}
