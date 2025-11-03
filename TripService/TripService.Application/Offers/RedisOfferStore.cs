using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripService.Application.Abstractions;

namespace TripService.Application.Offers
{
    public sealed class RedisOfferStore : IOfferStore
    {
        private readonly IDatabase _db;
        public RedisOfferStore(IConnectionMultiplexer mux) => _db = mux.GetDatabase();

        private static string OfferKey(Guid tripId, Guid driverId) => $"offer:trip:{tripId}:driver:{driverId}";
        private static string DeclKey(Guid tripId, Guid driverId) => $"offer:trip:{tripId}:driver:{driverId}:declined";

        public Task SetPendingAsync(Guid tripId, Guid driverId, TimeSpan ttl, CancellationToken ct = default)
            => _db.StringSetAsync(OfferKey(tripId, driverId), "1", ttl);

        public Task<bool> ExistsAsync(Guid tripId, Guid driverId, CancellationToken ct = default)
            => _db.KeyExistsAsync(OfferKey(tripId, driverId));

        public Task MarkDeclinedAsync(Guid tripId, Guid driverId, CancellationToken ct = default)
            => _db.StringSetAsync(DeclKey(tripId, driverId), "1", TimeSpan.FromMinutes(5));

        public async Task<bool> IsDeclinedAsync(Guid tripId, Guid driverId, CancellationToken ct = default)
            => await _db.StringGetAsync(DeclKey(tripId, driverId)) == "1";
    }
}
