using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Abstractions;
using Messaging.RabbitMQ.Config;
using Messaging.RabbitMQ.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using TripService.Application.Abstractions;
using TripService.Application.Services;

namespace TripService.Api.Messaging
{
    public sealed class DriverDeclinedTripConsumer : BaseRabbitConsumer<DriverDeclinedTrip>
    {
        private readonly ITripRepository _repo;
        private readonly TripMatchService _matchService;
        private readonly IOfferStore _offerStore;
        private readonly IEventPublisher _bus;
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<DriverDeclinedTripConsumer> _logger;

        public DriverDeclinedTripConsumer(
            ILogger<DriverDeclinedTripConsumer> logger,
            IOptions<RabbitMqOptions> options,
            ITripRepository repo,
            TripMatchService matchService,
            IOfferStore offerStore,
            IEventPublisher bus,
            IConnectionMultiplexer redis)
            : base(logger, options, Routing.Exchange)
        {
            _repo = repo;
            _matchService = matchService;
            _offerStore = offerStore;
            _bus = bus;
            _redis = redis;
            _logger = logger;
        }

        protected override string RoutingKey => Routing.Keys.DriverDeclinedTrip;
        protected override string QueueName => "trip.driver.declined";

        protected override async Task HandleAsync(
            DriverDeclinedTrip message,
            BasicDeliverEventArgs ea,
            IModel channel,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "Driver {DriverId} declined trip {TripId}",
                message.DriverId, message.TripId);

            var trip = await _repo.GetAsync(message.TripId, ct);
            if (trip != null && trip.AssignedDriverId == message.DriverId)
            {
                trip.DriverDecline();
                await _repo.UpdateAsync(trip, ct);

                // Release driver in Redis
                await ReleaseDriverAsync(message.DriverId, ct);

                // Retry with next driver if haven't exceeded retry limit (max 3 attempts)
                if (trip.DriverRetryCount < 3)
                {
                    _logger.LogInformation(
                        "Retrying trip {TripId} with next driver (attempt {Attempt})",
                        message.TripId, trip.DriverRetryCount + 1);

                    trip.StartFindingDriver();
                    await _repo.UpdateAsync(trip, ct);

                    // Get list of drivers already tried for this trip to exclude them
                    var triedDrivers = await _matchService.GetTriedDriversAsync(trip.Id);

                    var candidate = await _matchService.FindBestDriverAsync(
                        lat: trip.StartLat,
                        lng: trip.StartLng,
                        radiusKm: 20.0,
                        take: 10,
                        excludeDriverIds: triedDrivers);

                    if (candidate != null)
                    {
                        const int offerWindowSeconds = 15;
                        const int safetySeconds = 5;

                        // Lock the driver to prevent concurrent assignment
                        var driverLocked = await _matchService.TryLockDriverAsync(
                            candidate.DriverId,
                            trip.Id,
                            TimeSpan.FromSeconds(offerWindowSeconds + safetySeconds)
                        );

                        if (driverLocked)
                        {
                            trip.AssignDriver(candidate.DriverId);
                            await _repo.UpdateAsync(trip, ct);

                            // Track that we've tried this driver
                            await _matchService.AddTriedDriverAsync(trip.Id, candidate.DriverId);

                            // Mark driver as assigned in DriverService (via gRPC)
                            await _matchService.MarkDriverAssignedAsync(candidate.DriverId.ToString(), trip.Id);

                            await _offerStore.SetPendingAsync(
                                trip.Id,
                                candidate.DriverId,
                                TimeSpan.FromSeconds(offerWindowSeconds + safetySeconds),
                                ct);

                            await _bus.PublishAsync(
                                Routing.Keys.TripOffered,
                                new TripOffered(trip.Id, candidate.DriverId, offerWindowSeconds),
                                ct);
                        }
                        else
                        {
                            // Driver already locked, try finding another one recursively
                            trip.MarkNoDriverAvailable();
                            await _repo.UpdateAsync(trip, ct);
                        }
                    }
                    else
                    {
                        trip.MarkNoDriverAvailable();
                        await _repo.UpdateAsync(trip, ct);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Trip {TripId} exceeded retry limit, marking as NoDriverAvailable",
                        message.TripId);

                    trip.MarkNoDriverAvailable();
                    await _repo.UpdateAsync(trip, ct);
                }
            }
        }

        private async Task ReleaseDriverAsync(Guid driverId, CancellationToken ct)
        {
            var db = _redis.GetDatabase();
            await db.HashSetAsync($"driver:{driverId}", new HashEntry[]
            {
                new("available", "1"),
                new("current_trip_id", "")
            });
        }
    }
}
