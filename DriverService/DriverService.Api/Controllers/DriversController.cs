using DriverService.Application.Abstractions;
using DriverService.Application.Services;
using DriverService.Infrastructure.Data;
using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace DriverService.Api.Controllers
{
    [ApiController]
    [Route("api/drivers")]
    public class DriversController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;

        private readonly IDriverService _driverSvc;

        private readonly IEventPublisher _bus;

        public DriversController(IConnectionMultiplexer redis, IDriverService driverSvc, IEventPublisher bus)
        {
            _driverSvc = driverSvc;
            _redis = redis;
            _bus = bus;
        }

        // Bật online kèm tọa độ ban đầu
        [Authorize]
        [HttpPost("online")]
        public async Task<IActionResult> SetOnline([FromBody] SetOnlineRequest req, CancellationToken ct)
        {
            // Extract driver ID from JWT sub claim
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var driverId))
            {
                return Unauthorized(new { error = "Invalid or missing driver ID in token" });
            }

            // lưu lat/lng vào DB và Redis
            await _driverSvc.UpdateLocationAsync(driverId, req.Lat, req.Lng, ct);
            await _driverSvc.SetOnlineAsync(driverId, true, ct);
            return Ok(new { id = driverId, online = true });
        }

        // Cập nhật vị trí (tài xế đang online)
        [Authorize]
        [HttpPost("location")]
        public async Task<IActionResult> UpdateLocation([FromBody] UpdateLocationRequest req, CancellationToken ct)
        {
            // Extract driver ID from JWT sub claim
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var driverId))
            {
                return Unauthorized(new { error = "Invalid or missing driver ID in token" });
            }

            await _driverSvc.UpdateLocationAsync(driverId, req.Lat, req.Lng, ct);
            return Ok(new { id = driverId, updated = true });
        }

        [Authorize]
        [HttpPost("trip-finished")]
        public async Task<IActionResult> TripFinished()
        {
            // Extract driver ID from JWT sub claim
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var driverId))
            {
                return Unauthorized(new { error = "Invalid or missing driver ID in token" });
            }

            var dbRedis = _redis.GetDatabase();

            // đánh dấu driver available lại
            var key = $"driver:{driverId}";
            await dbRedis.HashSetAsync(key, new HashEntry[]
            {
            new HashEntry("available", "1"),
            new HashEntry("current_trip_id", "")
            });

            // (optional): update DB nếu bạn muốn log lịch sử
            // var driver = await _db.Drivers.FirstOrDefaultAsync(x => x.Id == driverId);
            // if (driver != null) { ... }
            // await _db.SaveChangesAsync();

            return Ok(new { driverId, status = "available" });
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] double lat, [FromQuery] double lng, [FromQuery] double radiusKm = 5, [FromQuery] int take = 10)
        {
            var db = _redis.GetDatabase();
            var results = await db.GeoRadiusAsync("drivers:geo", lng, lat, radiusKm, GeoUnit.Kilometers, count: take, order: Order.Ascending);

            var list = new List<object>();
            foreach (var r in results)
            {
                var id = r.Member.ToString();
                var hash = await db.HashGetAllAsync($"driver:{id}");
                var obj = hash.ToStringDictionary();
                list.Add(new
                {
                    driverId = id,
                    distanceKm = r.Distance ?? 0,
                    name = obj.GetValueOrDefault("name"),
                    lat = double.TryParse(obj.GetValueOrDefault("lat"), out var a) ? a : 0,
                    lng = double.TryParse(obj.GetValueOrDefault("lng"), out var b) ? b : 0,
                    available = obj.GetValueOrDefault("available") == "1"
                });
            }
            return Ok(list);
        }

        [Authorize]
        [HttpPost("trips/{tripId:guid}/accept")]
        public async Task<IActionResult> AcceptTrip(Guid tripId, CancellationToken ct)
        {
            // Extract driver ID from JWT sub claim
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var driverId))
            {
                return Unauthorized(new { error = "Invalid or missing driver ID in token" });
            }

            await _bus.PublishAsync(
                Routing.Keys.DriverAcceptedTrip,
                new DriverAcceptedTrip(tripId, driverId),
                ct);

            return Accepted(new { tripId, driverId, status = "accepted" });
        }

        [Authorize]
        [HttpPost("trips/{tripId:guid}/decline")]
        public async Task<IActionResult> Decline(Guid tripId, CancellationToken ct)
        {
            // Extract driver ID from JWT sub claim
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var driverId))
            {
                return Unauthorized(new { error = "Invalid or missing driver ID in token" });
            }

            // Publish both events: one for offer tracking, one for business logic
            await _bus.PublishAsync(Routing.Keys.TripOfferDeclined,
                new TripOfferDeclined(tripId, driverId), ct);

            await _bus.PublishAsync(Routing.Keys.DriverDeclinedTrip,
                new DriverDeclinedTrip(tripId, driverId), ct);

            return Accepted(new { tripId, driverId, status = "declined" });
        }

        [HttpGet("health")]
        public IActionResult Health() => Ok("driver ok");
    }

    public record UpdateLocationDto(double Lat, double Lng);
    public record SetOnlineDto(bool Online);

    public sealed class SetOnlineRequest
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
    }
    public sealed class UpdateLocationRequest
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
    }
}
