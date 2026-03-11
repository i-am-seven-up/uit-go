using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace TripService.Application.Services;

/// <summary>
/// Manages trip offer timeouts using Redis Sorted Set.
/// Replaces the blocking Task.Delay approach with a non-blocking scheduler.
/// </summary>
public class TripOfferTimeoutScheduler
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<TripOfferTimeoutScheduler> _logger;
    private const string TIMEOUT_KEY = "trip:offers:timeouts";

    public TripOfferTimeoutScheduler(
        IConnectionMultiplexer redis,
        ILogger<TripOfferTimeoutScheduler> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Schedule a trip offer to timeout after the specified TTL.
    /// Uses Redis Sorted Set where score = Unix timestamp of expiry time.
    /// </summary>
    public async Task ScheduleTimeoutAsync(Guid tripId, Guid driverId, int ttlSeconds)
    {
        try
        {
            var db = _redis.GetDatabase();
            var expireAtUnix = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds).ToUnixTimeSeconds();
            var member = $"{tripId}:{driverId}";

            var added = await db.SortedSetAddAsync(TIMEOUT_KEY, member, expireAtUnix);

            if (added)
            {
                _logger.LogDebug(
                    "Scheduled timeout for Trip={TripId}, Driver={DriverId}, ExpiresAt={ExpiresAt}",
                    tripId, driverId, DateTimeOffset.FromUnixTimeSeconds(expireAtUnix));
            }
            else
            {
                _logger.LogWarning(
                    "Timeout already scheduled for Trip={TripId}, Driver={DriverId}",
                    tripId, driverId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to schedule timeout for Trip={TripId}, Driver={DriverId}",
                tripId, driverId);
            throw;
        }
    }

    /// <summary>
    /// Get all expired trip offers (score <= current Unix timestamp).
    /// Automatically removes retrieved offers from the sorted set.
    /// </summary>
    public async Task<List<(Guid TripId, Guid DriverId)>> GetExpiredOffersAsync(int batchSize = 100)
    {
        try
        {
            var db = _redis.GetDatabase();
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Get expired offers (score <= now)
            var expired = await db.SortedSetRangeByScoreAsync(
                TIMEOUT_KEY,
                start: 0,
                stop: nowUnix,
                order: Order.Ascending,
                take: batchSize);

            if (expired.Length == 0)
            {
                return new List<(Guid, Guid)>();
            }

            // Remove from sorted set (atomic operation)
            await db.SortedSetRemoveAsync(TIMEOUT_KEY, expired);

            // Parse results
            var result = new List<(Guid TripId, Guid DriverId)>();
            foreach (var member in expired)
            {
                if (TryParseMember(member.ToString(), out var tripId, out var driverId))
                {
                    result.Add((tripId, driverId));
                }
                else
                {
                    _logger.LogWarning("Failed to parse timeout member: {Member}", member);
                }
            }

            if (result.Count > 0)
            {
                _logger.LogInformation(
                    "Retrieved {Count} expired trip offers for processing",
                    result.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get expired offers from Redis");
            return new List<(Guid, Guid)>();
        }
    }

    /// <summary>
    /// Cancel a scheduled timeout (e.g., when driver accepts/declines before timeout).
    /// </summary>
    public async Task CancelTimeoutAsync(Guid tripId, Guid driverId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var member = $"{tripId}:{driverId}";

            var removed = await db.SortedSetRemoveAsync(TIMEOUT_KEY, member);

            if (removed)
            {
                _logger.LogDebug(
                    "Cancelled timeout for Trip={TripId}, Driver={DriverId}",
                    tripId, driverId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to cancel timeout for Trip={TripId}, Driver={DriverId}",
                tripId, driverId);
        }
    }

    /// <summary>
    /// Get the count of pending timeouts (for monitoring).
    /// </summary>
    public async Task<long> GetPendingCountAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.SortedSetLengthAsync(TIMEOUT_KEY);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending timeout count");
            return 0;
        }
    }

    private bool TryParseMember(string member, out Guid tripId, out Guid driverId)
    {
        tripId = Guid.Empty;
        driverId = Guid.Empty;

        try
        {
            var parts = member.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            return Guid.TryParse(parts[0], out tripId) && Guid.TryParse(parts[1], out driverId);
        }
        catch
        {
            return false;
        }
    }
}
