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
            // B1: trạng thái ban đầu
            trip.Status = TripStatus.Searching;
            await _repo.AddAsync(trip, ct);

            // B2: tìm tài xế gần nhất trong 3km
            var candidate = await _match.FindBestDriverAsync(
                lat: trip.StartLat,
                lng: trip.StartLng,
                radiusKm: 3.0,
                take: 10
            );

            if (candidate is null)
            {
                // chưa có driver phù hợp -> vẫn Searching
                return trip;
            }

            // B3: khoá trip để tránh 2 thread assign cùng lúc
            var locked = await _match.TryLockTripAsync(trip.Id, TimeSpan.FromSeconds(5));
            if (!locked)
            {
                // race condition -> trả về trip vẫn Searching
                return trip;
            }

            // B4: thông báo cho DriverService là tài xế này đã được assign trip này
            // => DriverService sẽ set available = 0
            var marked = await _match.MarkDriverAssignedAsync(
                driverId: candidate.DriverId.ToString(),
                tripId: trip.Id
            );

            if (!marked)
            {
                // nếu vì lý do gì đó driver-service từ chối gán (VD driver vừa bận)
                // thì ta không set AssignedDriverId, trip vẫn Searching
                return trip;
            }

            // B5: cập nhật trip -> driver được gán chính thức
            trip.AssignedDriverId = candidate.DriverId;
            trip.Status = TripStatus.Accepted;

            await _repo.UpdateAsync(trip, ct);

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