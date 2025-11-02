using DriverService.Application.Abstractions;
using DriverService.Application.Services;
using DriverService.Infrastructure.Data;
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

        public DriversController(IConnectionMultiplexer redis, IDriverService driverSvc)
        {
            _driverSvc = driverSvc;
            _redis = redis;

        }

        // Bật online kèm tọa độ ban đầu
        [HttpPost("{id:guid}/online")]
        public async Task<IActionResult> SetOnline(Guid id, [FromBody] SetOnlineRequest req, CancellationToken ct)
        {
            // lưu lat/lng vào DB và Redis
            await _driverSvc.UpdateLocationAsync(id, req.Lat, req.Lng, ct);
            await _driverSvc.SetOnlineAsync(id, true, ct);
            return Ok(new { id, online = true });
        }

        // Cập nhật vị trí (tài xế đang online)
        [HttpPost("{id:guid}/location")]
        public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] UpdateLocationRequest req, CancellationToken ct)
        {
            await _driverSvc.UpdateLocationAsync(id, req.Lat, req.Lng, ct);
            return Ok(new { id, updated = true });
        }

        [HttpPost("{driverId}/trip-finished")]
        public async Task<IActionResult> TripFinished(Guid driverId)
        {
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
