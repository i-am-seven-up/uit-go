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

        public TripService(ITripRepository repo, IEventPublisher bus)
        {
            _repo = repo;
            _bus = bus;
        }

        public async Task<Trip> CreateAsync(Trip trip, CancellationToken ct = default)
        {
            trip.Status = TripStatus.Searching; 
            await _repo.AddAsync(trip, ct);

            var evt = new TripRequested(
            TripId: trip.Id,
            RiderId: trip.RiderId,
            Start: new GeoPoint(trip.StartLat, trip.StartLng),
            End: new GeoPoint(trip.EndLat, trip.EndLng))
            {
                //CorrelationId = ...
            };

            await _bus.PublishAsync(Routing.Keys.TripRequested, evt, ct); 

            return trip;
        }

        public Task<Trip?> GetAsync(Guid id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

        public async Task CancelAsync(Guid id, CancellationToken ct = default)
        {
            var t = await _repo.GetAsync(id, ct) ?? throw new KeyNotFoundException();
            t.Status = TripStatus.Canceled;
            await _repo.UpdateAsync(t, ct);
        }
    }
}
