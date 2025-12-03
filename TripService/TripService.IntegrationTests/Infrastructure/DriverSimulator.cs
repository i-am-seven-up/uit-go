using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Microsoft.Extensions.DependencyInjection;
using TripService.Api.Messaging;

namespace TripService.IntegrationTests.Infrastructure;

/// <summary>
/// Simulates DriverService actions by directly invoking TripService message consumers
/// This follows the actual architecture where DriverService publishes events that TripService consumes
/// </summary>
public class DriverSimulator
{
    private readonly TripServiceWebApplicationFactory _factory;

    public DriverSimulator(TripServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Simulates a driver accepting a trip by publishing the event
    /// In production: DriverService publishes DriverAcceptedTrip → TripService.DriverAcceptedTripConsumer handles it
    /// </summary>
    public async Task AcceptTripAsync(Guid tripId, Guid driverId, CancellationToken ct = default)
    {
        // Directly use the service layer to simulate the event effect
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<TripService.Application.Abstractions.ITripRepository>();

        var trip = await repo.GetAsync(tripId, ct);
        if (trip != null && trip.AssignedDriverId == driverId)
        {
            // Only accept if not already accepted (idempotent behavior)
            if (trip.Status != TripService.Domain.Entities.TripStatus.DriverAccepted)
            {
                trip.DriverAccept();
                await repo.UpdateAsync(trip, ct);
            }
        }
    }

    /// <summary>
    /// Simulates a driver declining a trip by directly executing the decline logic
    /// In production: DriverService publishes both TripOfferDeclined AND DriverDeclinedTrip
    /// </summary>
    public async Task DeclineTripAsync(Guid tripId, Guid driverId, CancellationToken ct = default)
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<TripService.Application.Abstractions.ITripRepository>();
        var offerStore = scope.ServiceProvider.GetRequiredService<TripService.Application.Abstractions.IOfferStore>();
        var matchService = scope.ServiceProvider.GetRequiredService<TripService.Application.Services.TripMatchService>();
        var bus = scope.ServiceProvider.GetRequiredService<Messaging.RabbitMQ.Abstractions.IEventPublisher>();
        var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();

        // Mark offer as declined
        await offerStore.MarkDeclinedAsync(tripId, driverId, ct);

        var trip = await repo.GetAsync(tripId, ct);
        if (trip != null && trip.AssignedDriverId == driverId)
        {
            trip.DriverDecline();
            await repo.UpdateAsync(trip, ct);

            // Release driver in Redis
            var db = redis.GetDatabase();
            await db.HashSetAsync($"driver:{driverId}", new StackExchange.Redis.HashEntry[]
            {
                new("available", "1"),
                new("current_trip_id", "")
            });

            // Retry with next driver if haven't exceeded retry limit
            if (trip.DriverRetryCount < 3)
            {
                trip.StartFindingDriver();
                await repo.UpdateAsync(trip, ct);

                // Get list of drivers already tried for this trip
                var triedDrivers = await matchService.GetTriedDriversAsync(trip.Id);

                var candidate = await matchService.FindBestDriverAsync(
                    lat: trip.StartLat,
                    lng: trip.StartLng,
                    radiusKm: 20.0,
                    take: 10,
                    excludeDriverIds: triedDrivers);

                if (candidate != null)
                {
                    const int offerWindowSeconds = 15;
                    const int safetySeconds = 5;

                    trip.AssignDriver(candidate.DriverId);
                    await repo.UpdateAsync(trip, ct);

                    // Track that we've tried this driver
                    await matchService.AddTriedDriverAsync(trip.Id, candidate.DriverId);

                    // Mark driver as assigned (simulates gRPC call)
                    await matchService.MarkDriverAssignedAsync(candidate.DriverId.ToString(), trip.Id);

                    await offerStore.SetPendingAsync(
                        trip.Id,
                        candidate.DriverId,
                        TimeSpan.FromSeconds(offerWindowSeconds + safetySeconds),
                        ct);

                    await bus.PublishAsync(
                        Routing.Keys.TripOffered,
                        new TripOffered(trip.Id, candidate.DriverId, offerWindowSeconds),
                        ct);
                }
                else
                {
                    trip.MarkNoDriverAvailable();
                    await repo.UpdateAsync(trip, ct);
                }
            }
            else
            {
                trip.MarkNoDriverAvailable();
                await repo.UpdateAsync(trip, ct);
            }
        }
    }
}
