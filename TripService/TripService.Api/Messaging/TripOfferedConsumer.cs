using Driver;
using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Abstractions;
using Messaging.RabbitMQ.Config;
using Messaging.RabbitMQ.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TripService.Application.Abstractions;
using TripService.Application.Services;
using TripService.Domain.Entities;

namespace TripService.Api.Messaging
{
    public sealed class TripOfferedConsumer : BaseRabbitConsumer<TripOffered>
    {
        private readonly IOfferStore _offers;
        private readonly ITripRepository _repo;
        private readonly TripMatchService _match;
        private readonly IEventPublisher _bus;
        private readonly DriverQuery.DriverQueryClient _driverGrpc;

        public TripOfferedConsumer(
            ILogger<TripOfferedConsumer> log,
            IOptions<RabbitMqOptions> opt,
            IOfferStore offers,
            ITripRepository repo,
            TripMatchService match,
            IEventPublisher bus,
            DriverQuery.DriverQueryClient driverGrpc)
            : base(log, opt, Routing.Exchange)
        {
            _offers = offers;
            _repo = repo;
            _match = match;
            _bus = bus;
            _driverGrpc = driverGrpc;
        }

        protected override string RoutingKey => Routing.Keys.TripOffered;
        protected override string QueueName => "trip.offers.pending";

        protected override async Task HandleAsync(
            TripOffered message,
            BasicDeliverEventArgs ea,
            IModel channel,
            CancellationToken ct)
        {
            // Chờ TTL
            await Task.Delay(TimeSpan.FromSeconds(message.TtlSeconds), ct);

            var declined = await _offers.IsDeclinedAsync(message.TripId, message.DriverId, ct);
            var stillExists = await _offers.ExistsAsync(message.TripId, message.DriverId, ct);

            if (declined)
            {
                var trip = await _repo.GetAsync(message.TripId, ct);
                Console.WriteLine($"[OfferFinalizer] Declined Trip={message.TripId} Driver={message.DriverId}");
                if (trip != null)
                {
                    // (tuỳ) publish notify user: driver từ chối, đang tìm tiếp
                }
                await FindAnotherDriverAsync(message.TripId, ct);
                return;
            }

            if (!stillExists)
            {
                // Offer key không còn (có thể hết TTL do race). Không làm gì thêm.
                return;
            }

            // AUTO-ASSIGN
            var trip2 = await _repo.GetAsync(message.TripId, ct);
            if (trip2 is null) return;

            var resp = await _driverGrpc.MarkTripAssignedAsync(
                new MarkTripAssignedRequest
                {
                    DriverId = message.DriverId.ToString(),
                    TripId = message.TripId.ToString()
                }, cancellationToken: ct);

            if (!resp.Success) return;

            trip2.AssignedDriverId = message.DriverId;
            trip2.Status = TripStatus.Accepted;
            await _repo.UpdateAsync(trip2, ct);

            var assigned = new TripAssigned(
                TripId: trip2.Id,
                DriverId: message.DriverId,
                AssignedAtUtc: DateTime.UtcNow
            );
            await _bus.PublishAsync(Routing.Keys.TripAssigned, assigned, ct);

            // (tuỳ) publish notify user/driver, bật realtime tracking
            Console.WriteLine($"[OfferFinalizer] Assigned Trip={trip2.Id} Driver={message.DriverId}");
        }

        private async Task FindAnotherDriverAsync(Guid tripId, CancellationToken ct)
        {
            var trip = await _repo.GetAsync(tripId, ct);
            if (trip is null) return;

            var next = await _match.FindBestDriverAsync(trip.StartLat, trip.StartLng, radiusKm: 20.0, take: 10);
            if (next is null) return;

            const int ttl = 15;
            await _offers.SetPendingAsync(trip.Id, next.DriverId, TimeSpan.FromSeconds(ttl), ct);

            var offered = new TripOffered(trip.Id, next.DriverId, ttl);
            await _bus.PublishAsync(Routing.Keys.TripOffered, offered, ct);

            Console.WriteLine($"[OfferFinalizer] Re-offer Trip={trip.Id} Driver={next.DriverId}");
        }
    }
}
