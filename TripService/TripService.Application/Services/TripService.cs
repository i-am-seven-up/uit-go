using Messaging.Contracts.Common;
using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Abstractions;
using TripService.Application.Abstractions;
using TripService.Domain.Entities;

namespace TripService.Application.Services
{
    public sealed class TripService : ITripService
    {
        private readonly ITripRepository _repo;
        private readonly TripMatchService _match;
        private readonly IOfferStore _offers;
        private readonly IEventPublisher _bus;

        public TripService(ITripRepository repo, TripMatchService match, IOfferStore offers, IEventPublisher bus)
        {
            _repo = repo; _match = match; _offers = offers; _bus = bus;
        }

        public async Task<Trip> CreateAsync(Trip trip, CancellationToken ct = default)
        {
            // Trip starts in Requested state (default)
            await _repo.AddAsync(trip, ct);

            var createdEvt = new TripCreated(
                TripId: trip.Id,
                PassengerId: trip.PassengerId,
                Start: new GeoPoint(trip.StartLat, trip.StartLng),
                End: new GeoPoint(trip.EndLat, trip.EndLng),
                CreatedAtUtc: DateTime.UtcNow
            );
            await _bus.PublishAsync(Routing.Keys.TripCreated, createdEvt, ct);

            // Transition to FindingDriver state
            trip.StartFindingDriver();
            await _repo.UpdateAsync(trip, ct);

            var candidate = await _match.FindBestDriverAsync(
                lat: trip.StartLat, lng: trip.StartLng, radiusKm: 20.0, take: 10);
            if (candidate is null)
            {
                trip.MarkNoDriverAvailable();
                await _repo.UpdateAsync(trip, ct);
                return trip;
            }

            const int offerWindowSeconds = 15;
            const int safetySeconds = 5;

            // Lock the DRIVER to prevent concurrent assignment to multiple trips
            var driverLocked = await _match.TryLockDriverAsync(
                candidate.DriverId,
                trip.Id,
                TimeSpan.FromSeconds(offerWindowSeconds + safetySeconds)
            );
            if (!driverLocked)
            {
                // Driver is already locked by another trip, mark as no driver available
                trip.MarkNoDriverAvailable();
                await _repo.UpdateAsync(trip, ct);
                return trip;
            }

            // Assign driver to trip
            trip.AssignDriver(candidate.DriverId);
            await _repo.UpdateAsync(trip, ct);

            // Track that we've tried this driver
            await _match.AddTriedDriverAsync(trip.Id, candidate.DriverId);

            // Mark driver as assigned in DriverService (via gRPC)
            await _match.MarkDriverAssignedAsync(candidate.DriverId.ToString(), trip.Id);

            await _offers.SetPendingAsync(
                trip.Id,
                candidate.DriverId,
                TimeSpan.FromSeconds(offerWindowSeconds + safetySeconds),
                ct
            );

            var offered = new TripOffered(
                TripId: trip.Id,
                DriverId: candidate.DriverId,
                TtlSeconds: offerWindowSeconds
            );

            await _bus.PublishAsync(Routing.Keys.TripOffered, offered, ct);

            return trip;
        }


        public Task<Trip?> GetAsync(Guid id, CancellationToken ct = default)
            => _repo.GetAsync(id, ct);

        public async Task CancelAsync(Guid id, string reason, CancellationToken ct = default)
        {
            var t = await _repo.GetAsync(id, ct) ?? throw new KeyNotFoundException();
            t.Cancel(reason);
            await _repo.UpdateAsync(t, ct);

            // If driver was assigned, publish event to release driver
            if (t.AssignedDriverId.HasValue)
            {
                var cancelledEvt = new TripCancelled(
                    TripId: t.Id,
                    DriverId: t.AssignedDriverId.Value,
                    Reason: reason
                );
                await _bus.PublishAsync(Routing.Keys.TripCancelled, cancelledEvt, ct);
            }
        }
    }
}

public class CreateTripRequest
{
    public double PickupLat { get; set; }
    public double PickupLng { get; set; }
    public double DropoffLat { get; set; }
    public double DropoffLng { get; set; }
}