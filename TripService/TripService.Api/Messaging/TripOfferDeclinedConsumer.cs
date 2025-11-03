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
    public sealed class TripOfferDeclinedConsumer : BaseRabbitConsumer<TripOfferDeclined>
    {
        private readonly IOfferStore _offers;

        public TripOfferDeclinedConsumer(
            ILogger<TripOfferDeclinedConsumer> log,
            IOptions<RabbitMqOptions> opt,
            IOfferStore offers)
            : base(log, opt, Routing.Exchange)
        {
            _offers = offers;
        }

        protected override string RoutingKey => Routing.Keys.TripOfferDeclined;
        protected override string QueueName => "trip.offers.declined";

        protected override async Task HandleAsync(
            TripOfferDeclined message,
            BasicDeliverEventArgs ea,
            IModel channel,
            CancellationToken ct)
        {
            await _offers.MarkDeclinedAsync(message.TripId, message.DriverId, ct);
            Console.WriteLine($"[TripOfferDeclined] Trip={message.TripId} Driver={message.DriverId}");
        }
    }
}
