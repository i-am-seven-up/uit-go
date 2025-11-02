using Messaging.Contracts.Drivers;
using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Abstractions;
using Messaging.RabbitMQ.Config;
using Messaging.RabbitMQ.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DriverService.Api.Messaging
{
    public sealed class TripAssignedConsumer : BaseRabbitConsumer<TripAssigned>
    {
        private readonly IEventPublisher _bus;
        public TripAssignedConsumer(
            ILogger<TripAssignedConsumer> log,
            IOptions<RabbitMqOptions> opt,
            IEventPublisher bus)
            : base(log, opt, Routing.Exchange)
        {
            _bus = bus;
        }

        protected override string RoutingKey => Routing.Keys.TripAssigned;
        protected override string QueueName => "driver.assigned.forwarder";

        protected override async Task HandleAsync(
            TripAssigned message,
            BasicDeliverEventArgs ea,
            IModel channel,
            CancellationToken ct)
        {
            Console.WriteLine($"[TripAssigned] Trip={message.TripId} Driver={message.DriverId}");

            var evt = new DriverAssignedToTrip(
                TripId: message.TripId,
                DriverId: message.DriverId
            );
            await _bus.PublishAsync(Routing.Keys.DriverAssignedToTrip, evt, ct);
        }
    }
}
