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

        [HttpPost("{driverId}/location")]
        public async Task<IActionResult> UpdateLocation(Guid driverId, [FromBody] UpdateLocationDto dto)
        {
            await _driverSvc.UpdateLocationAsync(driverId, dto.Lat, dto.Lng);
            return Ok();
        }

        [HttpPost("{driverId}/online")]
        public async Task<IActionResult> SetOnline(Guid driverId, [FromBody] SetOnlineDto dto)
        {
            await _driverSvc.SetOnlineAsync(driverId, dto.Online);
            return Ok();
        }

        [HttpPost("{driverId}/trip-finished")]
        public async Task<IActionResult> TripFinished(string driverId)
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

}
