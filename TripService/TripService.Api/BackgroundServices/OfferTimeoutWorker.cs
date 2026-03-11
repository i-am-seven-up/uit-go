using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Abstractions;
using TripService.Application.Abstractions;
using TripService.Application.Services;

namespace TripService.Api.BackgroundServices;

/// <summary>
/// Background worker that continuously polls for expired trip offers
/// and publishes appropriate events (TripAutoAssigned or TripOfferTimeout).
/// Replaces the blocking Task.Delay approach in TripOfferedConsumer.
/// </summary>
public class OfferTimeoutWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OfferTimeoutWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _errorRetryInterval = TimeSpan.FromSeconds(5);

    public OfferTimeoutWorker(
        IServiceProvider serviceProvider,
        ILogger<OfferTimeoutWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OfferTimeoutWorker starting...");

        // Wait a bit for the application to fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredOffersAsync(stoppingToken);
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("OfferTimeoutWorker stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OfferTimeoutWorker, retrying in {Delay}s", _errorRetryInterval.TotalSeconds);
                await Task.Delay(_errorRetryInterval, stoppingToken);
            }
        }

        _logger.LogInformation("OfferTimeoutWorker stopped");
    }

    private async Task ProcessExpiredOffersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();

        var scheduler = scope.ServiceProvider.GetRequiredService<TripOfferTimeoutScheduler>();
        var offerStore = scope.ServiceProvider.GetRequiredService<IOfferStore>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        // Get expired offers from Redis Sorted Set
        var expiredOffers = await scheduler.GetExpiredOffersAsync(batchSize: 100);

        if (expiredOffers.Count == 0)
        {
            return; // No work to do
        }

        _logger.LogInformation("Processing {Count} expired trip offers", expiredOffers.Count);

        foreach (var (tripId, driverId) in expiredOffers)
        {
            try
            {
                await ProcessSingleOfferTimeoutAsync(
                    tripId,
                    driverId,
                    offerStore,
                    eventPublisher,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to process timeout for Trip={TripId}, Driver={DriverId}",
                    tripId, driverId);
                // Continue with next offer
            }
        }
    }

    private async Task ProcessSingleOfferTimeoutAsync(
        Guid tripId,
        Guid driverId,
        IOfferStore offerStore,
        IEventPublisher eventPublisher,
        CancellationToken ct)
    {
        // Check if driver explicitly declined
        var isDeclined = await offerStore.IsDeclinedAsync(tripId, driverId, ct);

        // Check if offer still exists (not already processed)
        var stillExists = await offerStore.ExistsAsync(tripId, driverId, ct);

        if (!stillExists)
        {
            // Offer already processed (e.g., driver accepted explicitly)
            _logger.LogDebug(
                "Offer no longer exists for Trip={TripId}, Driver={DriverId} (likely already accepted)",
                tripId, driverId);
            return;
        }

        if (isDeclined)
        {
            // Driver explicitly declined → trigger retry to find another driver
            _logger.LogInformation(
                "Driver {DriverId} declined Trip {TripId}, publishing TripOfferTimeout for retry",
                driverId, tripId);

            var timeoutEvent = new TripOfferTimeout(
                TripId: tripId,
                DriverId: driverId,
                TimeoutAtUtc: DateTime.UtcNow,
                RetryCount: 0 // TODO: Track actual retry count
            );

            await eventPublisher.PublishAsync(
                Routing.Keys.TripOfferTimeout,
                timeoutEvent,
                ct);
        }
        else
        {
            // Driver did NOT respond (neither accept nor decline) → auto-assign
            _logger.LogInformation(
                "Driver {DriverId} did not respond to Trip {TripId}, publishing TripAutoAssigned",
                driverId, tripId);

            var autoAssignEvent = new TripAutoAssigned(
                TripId: tripId,
                DriverId: driverId,
                TimeoutAtUtc: DateTime.UtcNow
            );

            await eventPublisher.PublishAsync(
                Routing.Keys.TripAutoAssigned,
                autoAssignEvent,
                ct);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OfferTimeoutWorker is stopping gracefully...");
        await base.StopAsync(cancellationToken);
    }
}
