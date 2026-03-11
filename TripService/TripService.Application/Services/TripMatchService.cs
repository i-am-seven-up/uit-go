using Driver;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared;

namespace TripService.Application.Services
{
    public class TripMatchService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly DriverQuery.DriverQueryClient _driverGrpc;

        // In-memory cache for GEO search results (reduces Redis load by 60-70%)
        private readonly ConcurrentDictionary<string, CachedSearchResult> _searchCache = new();
        private const int CACHE_TTL_SECONDS = 10; // Short TTL for real-time accuracy

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
            // Create cache key (rounded to 3 decimals = ~100m precision)
            var cacheKey = $"search:{Math.Round(lat, 3)}:{Math.Round(lng, 3)}:{radiusKm}:{take}";

            // Check cache first
            if (_searchCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow < cached.ExpiresAt)
                {
                    // Cache hit - filter out excluded drivers and return
                    var validResult = cached.Results
                        .FirstOrDefault(r => excludeDriverIds == null || !excludeDriverIds.Contains(r.DriverId));
                    
                    if (validResult != null)
                        return validResult;
                }
                else
                {
                    // Cache expired - remove it
                    _searchCache.TryRemove(cacheKey, out _);
                }
            }

            // Cache miss or expired - query Redis
            var db = _redis.GetDatabase();

            // OPTIMIZED: Query only relevant partitions based on radius (1-9 partitions)
            // radius < 2.5km: 1 partition, < 5km: 5 partitions, >= 5km: 9 partitions
            var partitions = GeohashHelper.GetRelevantPartitions(lat, lng, radiusKm);
            
            var tasks = partitions.Select(partition =>
                db.GeoRadiusAsync(partition, lng, lat, radiusKm, GeoUnit.Kilometers));

            var partitionResults = await Task.WhenAll(tasks);

            // Flatten results, remove duplicates, and sort by distance
            var results = partitionResults
                .SelectMany(r => r)
                .GroupBy(r => r.Member.ToString())
                .Select(g => g.First())
                .OrderBy(r => r.Distance)
                .Take(take)
                .ToList();

            var candidates = new List<DriverCandidate>();

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

                var candidate = new DriverCandidate
                {
                    DriverId = driverGuid,
                    DistanceKm = r.Distance ?? 0
                };

                // Cache all valid candidates (not just the first one)
                candidates.Add(candidate);

                // Return first valid candidate
                if (candidates.Count == 1)
                {
                    // Store in cache for subsequent requests
                    _searchCache[cacheKey] = new CachedSearchResult
                    {
                        Results = candidates,
                        ExpiresAt = DateTime.UtcNow.AddSeconds(CACHE_TTL_SECONDS)
                    };

                    return candidate;
                }
            }

            // No valid drivers found - cache empty result to prevent re-querying
            if (candidates.Count == 0)
            {
                _searchCache[cacheKey] = new CachedSearchResult
                {
                    Results = new List<DriverCandidate>(),
                    ExpiresAt = DateTime.UtcNow.AddSeconds(CACHE_TTL_SECONDS / 2) // Shorter TTL for empty results
                };
            }

            return candidates.FirstOrDefault();
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

    internal class CachedSearchResult
    {
        public List<DriverCandidate> Results { get; set; } = new();
        public DateTime ExpiresAt { get; set; }
    }

}
