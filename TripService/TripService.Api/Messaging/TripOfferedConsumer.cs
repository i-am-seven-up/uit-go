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
        private readonly TripOfferTimeoutScheduler _timeoutScheduler;
        private readonly ILogger<TripOfferedConsumer> _logger;

        public TripOfferedConsumer(
            ILogger<TripOfferedConsumer> log,
            IOptions<RabbitMqOptions> opt,
            IOfferStore offers,
            TripOfferTimeoutScheduler timeoutScheduler)
            : base(log, opt, Routing.Exchange)
        {
            _offers = offers;
            _timeoutScheduler = timeoutScheduler;
            _logger = log;
        }

        protected override string RoutingKey => Routing.Keys.TripOffered;
        protected override string QueueName => "trip.offers.pending";

        protected override async Task HandleAsync(
            TripOffered message,
            BasicDeliverEventArgs ea,
            IModel channel,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "Received TripOffered event for Trip={TripId}, Driver={DriverId}, TTL={TtlSeconds}s",
                message.TripId, message.DriverId, message.TtlSeconds);

            // ✅ NO MORE BLOCKING DELAY!
            // Instead, schedule the timeout using Redis Sorted Set
            await _timeoutScheduler.ScheduleTimeoutAsync(
                message.TripId,
                message.DriverId,
                message.TtlSeconds);

            // Store the pending offer in Redis
            await _offers.SetPendingAsync(
                message.TripId,
                message.DriverId,
                TimeSpan.FromSeconds(message.TtlSeconds),
                ct);

            _logger.LogDebug(
                "Trip offer scheduled: Trip={TripId}, Driver={DriverId}, ExpiresAt={ExpiresAt}",
                message.TripId, message.DriverId, message.ExpiresAtUtc);

            // ✅ CONSUMER RETURNS IMMEDIATELY - NO BLOCKING!
            // OfferTimeoutWorker will handle the timeout processing
        }
    }
}
