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
            int take)
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
                    DriverId = Guid.Parse(driverId),
                    DistanceKm = r.Distance ?? 0
                };
            }

            return null;
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
