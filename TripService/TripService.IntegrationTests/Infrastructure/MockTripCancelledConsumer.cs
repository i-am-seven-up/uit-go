using Messaging.Contracts.Routing;
using Messaging.Contracts.Trips;
using Messaging.RabbitMQ.Config;
using Messaging.RabbitMQ.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

namespace TripService.IntegrationTests.Infrastructure;

/// <summary>
/// Mock consumer that simulates DriverService's TripCancelledConsumer
/// Releases driver in Redis when a trip is cancelled
/// </summary>
public sealed class MockTripCancelledConsumer : BaseRabbitConsumer<TripCancelled>
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MockTripCancelledConsumer> _logger;

    public MockTripCancelledConsumer(
        ILogger<MockTripCancelledConsumer> logger,
        IOptions<RabbitMqOptions> options,
        IConnectionMultiplexer redis)
        : base(logger, options, Routing.Exchange)
    {
        _redis = redis;
        _logger = logger;
    }

    protected override string RoutingKey => Routing.Keys.TripCancelled;
    protected override string QueueName => "test.driver.tripcancelled";

    protected override async Task HandleAsync(
        TripCancelled message,
        BasicDeliverEventArgs ea,
        IModel channel,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "[TEST MOCK] Trip {TripId} cancelled, releasing driver {DriverId}",
            message.TripId, message.DriverId);

        // Release driver in Redis (simulates DriverService behavior)
        var db = _redis.GetDatabase();
        await db.HashSetAsync($"driver:{message.DriverId}", new HashEntry[]
        {
            new("available", "1"),
            new("current_trip_id", "")
        });

        _logger.LogInformation(
            "[TEST MOCK] Driver {DriverId} released successfully",
            message.DriverId);
    }
}
