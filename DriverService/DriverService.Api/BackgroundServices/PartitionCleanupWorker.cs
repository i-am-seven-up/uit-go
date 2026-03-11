using StackExchange.Redis;

namespace DriverService.Api.BackgroundServices;

/// <summary>
/// Background service that periodically cleans up empty GEO partitions and expired driver mappings.
/// Runs every hour to maintain Redis memory efficiency.
/// </summary>
public class PartitionCleanupWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PartitionCleanupWorker> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

    public PartitionCleanupWorker(
        IConnectionMultiplexer redis,
        ILogger<PartitionCleanupWorker> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PartitionCleanupWorker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);

                _logger.LogInformation("Starting partition cleanup...");
                await CleanupPartitionsAsync(stoppingToken);
                _logger.LogInformation("Partition cleanup completed.");
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during partition cleanup");
                // Continue running even if cleanup fails
            }
        }

        _logger.LogInformation("PartitionCleanupWorker stopped.");
    }

    private async Task CleanupPartitionsAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());

        // Find all partition keys matching the pattern "drivers:online:*"
        var partitionKeys = server.Keys(pattern: "drivers:online:*").ToList();
        _logger.LogInformation("Found {Count} partition keys", partitionKeys.Count);

        int emptyPartitions = 0;
        int activePartitions = 0;

        foreach (var key in partitionKeys)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                // Check if partition exists and has any members
                // Use SortedSetLength since GEO is implemented as a sorted set in Redis
                var count = await db.SortedSetLengthAsync(key);

                if (count == 0)
                {
                    // Delete empty partition
                    await db.KeyDeleteAsync(key);
                    emptyPartitions++;
                }
                else
                {
                    activePartitions++;
                    // Refresh TTL on active partitions
                    await db.KeyExpireAsync(key, TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup partition {Key}", key.ToString());
            }
        }

        _logger.LogInformation(
            "Cleanup summary: {EmptyPartitions} empty partitions removed, {ActivePartitions} active partitions kept",
            emptyPartitions,
            activePartitions);

        // Cleanup expired driver partition mappings
        await CleanupExpiredDriverMappingsAsync(server, db, ct);
    }

    private async Task CleanupExpiredDriverMappingsAsync(IServer server, IDatabase db, CancellationToken ct)
    {
        try
        {
            // Find all driver partition mapping keys
            var mappingKeys = server.Keys(pattern: "driver:*:partition").ToList();
            _logger.LogInformation("Checking {Count} driver partition mappings", mappingKeys.Count);

            int expiredMappings = 0;

            foreach (var key in mappingKeys)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    // Check if the mapping has expired (TTL < 0 means no TTL, which shouldn't happen)
                    var ttl = await db.KeyTimeToLiveAsync(key);

                    if (!ttl.HasValue || ttl.Value <= TimeSpan.Zero)
                    {
                        // Mapping has expired or has no TTL, remove it
                        await db.KeyDeleteAsync(key);
                        expiredMappings++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check driver mapping {Key}", key.ToString());
                }
            }

            if (expiredMappings > 0)
            {
                _logger.LogInformation("Removed {Count} expired driver partition mappings", expiredMappings);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired driver mappings");
        }
    }
}
