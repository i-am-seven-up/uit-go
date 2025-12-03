using Driver;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TripService.Application.Services
{
    public class TripMatchService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly DriverQuery.DriverQueryClient _driverGrpc;

        public TripMatchService(
            IConnectionMultiplexer redis,
            DriverQuery.DriverQueryClient driverGrpc)
        {
            _redis = redis;
            _driverGrpc = driverGrpc;
        }

        public async Task<DriverCandidate?> FindBestDriverAsync(
            double lat, double lng,
            double radiusKm,
            int take,
            HashSet<Guid>? excludeDriverIds = null)
        {
            var db = _redis.GetDatabase();

            var results = await db.GeoRadiusAsync(
                "drivers:online",
                longitude: lng,
                latitude: lat,
                radius: radiusKm,
                unit: GeoUnit.Kilometers,
                count: take,
                order: Order.Ascending);

            foreach (var r in results)
            {
                var driverId = r.Member.ToString();
                var driverGuid = Guid.Parse(driverId);

                // Skip drivers that have already been tried for this trip
                if (excludeDriverIds != null && excludeDriverIds.Contains(driverGuid))
                    continue;

                // confirm với DriverService qua gRPC
                var info = await _driverGrpc.GetDriverInfoAsync(
                    new GetDriverInfoRequest
                    {
                        DriverId = driverId
                    }
                );

                if (!info.Available)
                    continue;

                return new DriverCandidate
                {
                    DriverId = driverGuid,
                    DistanceKm = r.Distance ?? 0
                };
            }

            return null;
        }

        public async Task<HashSet<Guid>> GetTriedDriversAsync(Guid tripId)
        {
            var db = _redis.GetDatabase();
            var members = await db.SetMembersAsync($"trip:{tripId}:tried_drivers");
            return members.Select(m => Guid.Parse(m.ToString())).ToHashSet();
        }

        public async Task AddTriedDriverAsync(Guid tripId, Guid driverId)
        {
            var db = _redis.GetDatabase();
            await db.SetAddAsync($"trip:{tripId}:tried_drivers", driverId.ToString());
            await db.KeyExpireAsync($"trip:{tripId}:tried_drivers", TimeSpan.FromHours(1));
        }

        public async Task<bool> TryLockTripAsync(Guid tripId, TimeSpan ttl)
        {
            var db = _redis.GetDatabase();
            return await db.StringSetAsync(
                $"trip:{tripId}:lock",
                "1",
                ttl,
                When.NotExists
            );
        }

        public async Task<bool> TryLockDriverAsync(Guid driverId, Guid tripId, TimeSpan ttl)
        {
            var db = _redis.GetDatabase();
            return await db.StringSetAsync(
                $"driver:{driverId}:trip_lock",
                tripId.ToString(),
                ttl,
                When.NotExists
            );
        }

        public async Task<bool> MarkDriverAssignedAsync(string driverId, Guid tripId)
        {
            var resp = await _driverGrpc.MarkTripAssignedAsync(new MarkTripAssignedRequest
            {
                DriverId = driverId,
                TripId = tripId.ToString()
            });

            return resp.Success;
        }
    }

    public class DriverCandidate
    {
        public Guid DriverId { get; set; }
        public double DistanceKm { get; set; }
    }

}
