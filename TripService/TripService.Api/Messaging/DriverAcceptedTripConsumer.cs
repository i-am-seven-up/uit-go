using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Config;
using Messaging.RabbitMQ.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TripService.Application.Abstractions;

namespace TripService.Api.Messaging
{
    public sealed class DriverAcceptedTripConsumer : BaseRabbitConsumer<DriverAcceptedTrip>
    {
        private readonly ITripRepository _repo;
        private readonly ILogger<DriverAcceptedTripConsumer> _logger;

        public DriverAcceptedTripConsumer(
            ILogger<DriverAcceptedTripConsumer> logger,
            IOptions<RabbitMqOptions> options,
            ITripRepository repo)
            : base(logger, options, Routing.Exchange)
        {
            _repo = repo;
            _logger = logger;
        }

        protected override string RoutingKey => Routing.Keys.DriverAcceptedTrip;
        protected override string QueueName => "trip.driver.accepted";

        protected override async Task HandleAsync(
            DriverAcceptedTrip message,
            BasicDeliverEventArgs ea,
            IModel channel,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "Driver {DriverId} accepted trip {TripId}",
                message.DriverId, message.TripId);

            var trip = await _repo.GetAsync(message.TripId, ct);
            if (trip != null && trip.AssignedDriverId == message.DriverId)
            {
                trip.DriverAccept();
                await _repo.UpdateAsync(trip, ct);

                _logger.LogInformation(
                    "Trip {TripId} transitioned to DriverAccepted",
                    message.TripId);
            }
        }
    }
}
