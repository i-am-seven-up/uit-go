using StackExchange.Redis;

namespace E2E.PerformanceTests.Infrastructure;

public class RedisHelper : IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public RedisHelper()
    {
        _redis = ConnectionMultiplexer.Connect(TestConfig.RedisConnectionString);
        _db = _redis.GetDatabase();
    }

    public async Task SeedDriversAsync(int count)
    {
        Console.WriteLine($"Seeding {count} drivers in Redis...");
        var batch = _db.CreateBatch();
        var tasks = new List<Task>();

        for (int i = 0; i < count; i++)
        {
            var driverId = Guid.NewGuid();
            var (lat, lng) = TestConfig.HCMCCoordinates.GetRandomLocation();

            // Add to geo index
            tasks.Add(batch.GeoAddAsync("drivers:online", lng, lat, driverId.ToString()));

            // Set driver as available
            tasks.Add(batch.HashSetAsync($"driver:{driverId}", new[]
            {
                new HashEntry("available", "1"),
                new HashEntry("current_trip_id", ""),
                new HashEntry("lat", lat.ToString()),
                new HashEntry("lng", lng.ToString())
            }));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
        Console.WriteLine($"✓ Seeded {count} drivers");
    }

    public async Task<int> GetOnlineDriverCount()
    {
        var members = await _db.GeoRadiusAsync(
            "drivers:online",
            TestConfig.HCMCCoordinates.District1.lng,
            TestConfig.HCMCCoordinates.District1.lat,
            100, // 100km radius
            GeoUnit.Kilometers
        );
        return members.Length;
    }

    public async Task<int> GetAvailableDriverCount()
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: "driver:*").ToList();

        int available = 0;
        foreach (var key in keys)
        {
            var isAvailable = await _db.HashGetAsync(key, "available");
            if (isAvailable == "1")
            {
                available++;
            }
        }

        return available;
    }

    public async Task CleanupAsync()
    {
        Console.WriteLine("Cleaning up Redis...");
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());

            // Delete all driver keys
            var driverKeys = server.Keys(pattern: "driver:*").ToArray();
            if (driverKeys.Length > 0)
            {
                await _db.KeyDeleteAsync(driverKeys);
            }

            // Delete geo index
            await _db.KeyDeleteAsync("drivers:online");

            Console.WriteLine($"✓ Redis cleaned ({driverKeys.Length} driver keys deleted)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Redis cleanup failed (non-critical): {ex.Message}");
        }
    }

    public async Task<Dictionary<string, string>> GetDriverInfo(Guid driverId)
    {
        var entries = await _db.HashGetAllAsync($"driver:{driverId}");
        return entries.ToDictionary(
            e => e.Name.ToString(),
            e => e.Value.ToString()
        );
    }

    public async Task SetDriverOnline(Guid driverId, double lat, double lng, bool available = true)
    {
        await _db.GeoAddAsync("drivers:online", lng, lat, driverId.ToString());
        await _db.HashSetAsync($"driver:{driverId}", new[]
        {
            new HashEntry("available", available ? "1" : "0"),
            new HashEntry("current_trip_id", ""),
            new HashEntry("lat", lat.ToString()),
            new HashEntry("lng", lng.ToString())
        });
    }

    public async Task<long> GetMemoryUsageBytes()
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var info = await server.InfoAsync("memory");

        foreach (var section in info)
        {
            foreach (var item in section)
            {
                if (item.Key == "used_memory")
                {
                    return long.Parse(item.Value);
                }
            }
        }

        return 0;
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
