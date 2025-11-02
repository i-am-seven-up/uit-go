using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Config;
using Messaging.RabbitMQ.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DriverService.Api.Messaging
{
    public sealed class TripRequestedConsumer : BaseRabbitConsumer<TripRequested>
    {
        public TripRequestedConsumer(
            ILogger<TripRequestedConsumer> log,
            IOptions<RabbitMqOptions> opt)
            : base(log, opt, Routing.Exchange) { }

        protected override string RoutingKey => Routing.Keys.TripRequested;
        protected override string QueueName => "driver.matching";

        protected override Task HandleAsync(
            TripRequested message,
            BasicDeliverEventArgs ea,
            IModel channel,
            CancellationToken ct)
        {
            Console.WriteLine($"[TripRequested] Trip={message.TripId} Start={message.Start} End={message.End}");
            return Task.CompletedTask;
        }
    }
}
