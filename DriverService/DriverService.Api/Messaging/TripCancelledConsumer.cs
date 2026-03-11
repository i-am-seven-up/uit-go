using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Config;
using Messaging.RabbitMQ.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

namespace DriverService.Api.Messaging
{
    public sealed class TripCancelledConsumer : BaseRabbitConsumer<TripCancelled>
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<TripCancelledConsumer> _logger;

        public TripCancelledConsumer(
            ILogger<TripCancelledConsumer> logger,
            IOptions<RabbitMqOptions> options,
            IConnectionMultiplexer redis)
            : base(logger, options, Routing.Exchange)
        {
            _redis = redis;
            _logger = logger;
        }

        protected override string RoutingKey => Routing.Keys.TripCancelled;
        protected override string QueueName => "driver.tripcancelled";

        protected override async Task HandleAsync(
            TripCancelled message,
            BasicDeliverEventArgs ea,
            IModel channel,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "Trip {TripId} cancelled, releasing driver {DriverId}. Reason: {Reason}",
                message.TripId, message.DriverId, message.Reason);

            // Release driver in Redis
            var db = _redis.GetDatabase();
            await db.HashSetAsync($"driver:{message.DriverId}", new HashEntry[]
            {
                new("available", "1"),
                new("current_trip_id", "")
            });

            _logger.LogInformation(
                "Driver {DriverId} is now available again",
                message.DriverId);

            // Here you could also send push notification to driver
            // or WebSocket message for real-time update
        }
    }
}
