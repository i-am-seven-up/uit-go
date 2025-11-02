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
        private readonly IEventPublisher _bus;

        private readonly TripMatchService _match;

        public TripService(ITripRepository repo, IEventPublisher bus, TripMatchService match)
        {
            _repo = repo;
            _bus = bus;
            _match = match; 
        }

        public async Task<Trip> CreateAsync(Trip trip, CancellationToken ct = default)
        {
            trip.Status = TripStatus.Searching;
            await _repo.AddAsync(trip, ct);

            var createdEvt = new TripCreated(
                TripId: trip.Id,
                PassengerId: trip.PassengerId,         
                Start: new GeoPoint(trip.StartLat, trip.StartLng),
                End: new GeoPoint(trip.EndLat, trip.EndLng),
                CreatedAtUtc: DateTime.UtcNow
            );
            await _bus.PublishAsync(Routing.Keys.TripCreated, createdEvt, ct);

            var candidate = await _match.FindBestDriverAsync(
                lat: trip.StartLat,
                lng: trip.StartLng,
                radiusKm: 20.0,
                take: 10
            );
            if (candidate is null) return trip;

            var locked = await _match.TryLockTripAsync(trip.Id, TimeSpan.FromSeconds(5));
            if (!locked) return trip;

            var marked = await _match.MarkDriverAssignedAsync(
                driverId: candidate.DriverId.ToString(),
                tripId: trip.Id
            );
            if (!marked) return trip;

            trip.AssignedDriverId = candidate.DriverId;
            trip.Status = TripStatus.Accepted;
            await _repo.UpdateAsync(trip, ct);

            var assignedEvt = new TripAssigned(
                TripId: trip.Id,
                DriverId: candidate.DriverId,
                AssignedAtUtc: DateTime.UtcNow
            );
            await _bus.PublishAsync(Routing.Keys.TripAssigned, assignedEvt, ct);

            return trip;
        }


        public Task<Trip?> GetAsync(Guid id, CancellationToken ct = default)
            => _repo.GetAsync(id, ct);

        public async Task CancelAsync(Guid id, CancellationToken ct = default)
        {
            var t = await _repo.GetAsync(id, ct) ?? throw new KeyNotFoundException();
            t.Status = TripStatus.Canceled;
            await _repo.UpdateAsync(t, ct);
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