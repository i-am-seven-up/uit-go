using Microsoft.AspNetCore.Authorization;
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

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateTripRequest req, CancellationToken ct)
        {
            // Extract PassengerId from JWT sub claim
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var passengerId))
            {
                return Unauthorized(new { error = "Invalid or missing user ID in token" });
            }

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

        [Authorize]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var result = await _tripService.GetAsync(id, ct);
            if (result is null) return NotFound();
            return Ok(result);
        }

        [Authorize]
        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelTripRequest? request, CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var passengerId))
            {
                return Unauthorized(new { error = "Invalid or missing user ID in token" });
            }

            var trip = await _tripService.GetAsync(id, ct);
            if (trip == null)
                return NotFound();

            // Verify ownership
            if (trip.PassengerId != passengerId)
                return Forbid();

            var reason = request?.Reason ?? "Passenger cancelled";

            try
            {
                await _tripService.CancelAsync(id, reason, ct);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("health")]
        public IActionResult Health() => Ok("trip ok");
    }

    public record CreateTripRequest(double PickupLat, double PickupLng, double DropoffLat, double DropoffLng);
    public record CancelTripRequest(string? Reason);
}
