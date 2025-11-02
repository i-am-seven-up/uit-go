using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriverService.Application.Services
{
    public class DriverLocationService
    {
        private readonly IConnectionMultiplexer _redis;
        public DriverLocationService(IConnectionMultiplexer redis) => _redis = redis;

        public async Task UpdateLocationAsync(Guid driverId, double lat, double lng)
        {
            var db = _redis.GetDatabase();
            // Lưu vào GEO key drivers:online
            await db.GeoAddAsync("drivers:online", longitude: lng, latitude: lat, member: driverId.ToString());
            // Optional: trạng thái online
            await db.HashSetAsync($"driver:{driverId}", new HashEntry[] { new("online", "1") });
        }
    }

}
