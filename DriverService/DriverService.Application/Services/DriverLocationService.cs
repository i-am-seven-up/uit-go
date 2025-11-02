using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriverService.Application.Services
{
    public class DriverLocationService
    {
        private readonly IConnectionMultiplexer _redis;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const string GEO_KEY = "drivers:online";

        public DriverLocationService(IConnectionMultiplexer redis) => _redis = redis;

        public async Task SetOnlineAsync(Guid driverId, string name, double lat, double lng)
        {
            var db = _redis.GetDatabase();
            await db.GeoAddAsync(GEO_KEY, longitude: lng, latitude: lat, member: driverId.ToString());
            await db.HashSetAsync($"driver:{driverId}", new HashEntry[] {
            new("name", name ?? ""),
            new("online", "1"),
            new("available", "1"),
            new("lat", lat.ToString(C)),
            new("lng", lng.ToString(C)),
            new("current_trip_id", "")
        });
        }

        public async Task UpdateLocationAsync(Guid driverId, double lat, double lng)
        {
            var db = _redis.GetDatabase();
            await db.GeoAddAsync(GEO_KEY, longitude: lng, latitude: lat, member: driverId.ToString());
            await db.HashSetAsync($"driver:{driverId}", new HashEntry[] {
            new("lat", lat.ToString(C)),
            new("lng", lng.ToString(C))
        });
        }

        public async Task SetOfflineAsync(Guid driverId)
        {
            var db = _redis.GetDatabase();
            await db.HashSetAsync($"driver:{driverId}", new HashEntry[] {
            new("online", "0"),
            new("available", "0"),
            new("current_trip_id", "")
        });
            // tuỳ chọn: await db.GeoRemoveAsync(GEO_KEY, driverId.ToString());
        }

        public async Task<bool> AssignTripAsync(Guid driverId, Guid tripId)
        {
            var db = _redis.GetDatabase();
            if (await db.HashGetAsync($"driver:{driverId}", "available") != "1") return false;
            await db.HashSetAsync($"driver:{driverId}", new HashEntry[] {
            new("available", "0"),
            new("current_trip_id", tripId.ToString())
        });
            return true;
        }
    }
}
