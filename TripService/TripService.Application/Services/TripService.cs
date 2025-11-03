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
                lat: trip.StartLat, lng: trip.StartLng, radiusKm: 20.0, take: 10);
            if (candidate is null) return trip;

            const int offerWindowSeconds = 15;   // thời gian tài xế được phép decline
            const int safetySeconds = 5;    // đệm chống lệch thời gian

            var locked = await _match.TryLockTripAsync(
                trip.Id,
                TimeSpan.FromSeconds(offerWindowSeconds + safetySeconds)
            );
            if (!locked) return trip;

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