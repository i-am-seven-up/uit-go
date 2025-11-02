using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Config;
using Messaging.RabbitMQ.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DriverService.Api.Messaging
{
    public sealed class TripCreatedConsumer : BaseRabbitConsumer<TripCreated>
    {
        public TripCreatedConsumer(
            ILogger<TripCreatedConsumer> log,
            IOptions<RabbitMqOptions> opt)
            : base(log, opt, Routing.Exchange) { }

        protected override string RoutingKey => Routing.Keys.TripCreated;
        protected override string QueueName => "driver.tripcreated";

        protected override Task HandleAsync(
            TripCreated message,
            BasicDeliverEventArgs ea,
            IModel channel,
            CancellationToken ct)
        {
            Console.WriteLine($"[TripCreated] Trip={message.TripId} From={message.Start} To={message.End}");
            // Ở phase này chỉ cần biết có chuyến mới, không cần push realtime.
            return Task.CompletedTask;
        }
    }
}
