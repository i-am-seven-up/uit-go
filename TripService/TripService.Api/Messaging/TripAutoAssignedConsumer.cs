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
using TripService.Domain.Entities;

namespace TripService.Api.Messaging;

/// <summary>
/// Handles TripAutoAssigned event when a trip is automatically assigned to a driver
/// after the offer timeout (driver did not explicitly accept or decline).
/// </summary>
public sealed class TripAutoAssignedConsumer : BaseRabbitConsumer<TripAutoAssigned>
{
    private readonly ITripRepository _repo;
    private readonly IOfferStore _offers;
    private readonly IEventPublisher _bus;
    private readonly DriverQuery.DriverQueryClient _driverGrpc;
    private readonly ILogger<TripAutoAssignedConsumer> _logger;

    public TripAutoAssignedConsumer(
        ILogger<TripAutoAssignedConsumer> logger,
        IOptions<RabbitMqOptions> options,
        ITripRepository repo,
        IOfferStore offers,
        IEventPublisher bus,
        DriverQuery.DriverQueryClient driverGrpc)
        : base(logger, options, Routing.Exchange)
    {
        _repo = repo;
        _offers = offers;
        _bus = bus;
        _driverGrpc = driverGrpc;
        _logger = logger;
    }

    protected override string RoutingKey => Routing.Keys.TripAutoAssigned;
    protected override string QueueName => "trip.auto.assigned";

    protected override async Task HandleAsync(
        TripAutoAssigned message,
        BasicDeliverEventArgs ea,
        IModel channel,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Auto-assigning Trip {TripId} to Driver {DriverId} after timeout",
            message.TripId, message.DriverId);

        // 1. Get trip
        var trip = await _repo.GetAsync(message.TripId, ct);
        if (trip == null)
        {
            _logger.LogWarning("Trip {TripId} not found for auto-assignment", message.TripId);
            return;
        }

        // 2. Verify trip is still in FindingDriver status
        if (trip.Status != TripStatus.FindingDriver)
        {
            _logger.LogWarning(
                "Trip {TripId} is no longer in FindingDriver status (current: {Status}), skipping auto-assignment",
                message.TripId, trip.Status);
            return;
        }

        // 3. Call gRPC to mark driver as assigned
        try
        {
            var grpcResponse = await _driverGrpc.MarkTripAssignedAsync(
                new MarkTripAssignedRequest
                {
                    DriverId = message.DriverId.ToString(),
                    TripId = message.TripId.ToString()
                },
                cancellationToken: ct);

            if (!grpcResponse.Success)
            {
                _logger.LogWarning(
                    "Failed to mark Driver {DriverId} as assigned for Trip {TripId} via gRPC",
                    message.DriverId, message.TripId);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "gRPC call failed while auto-assigning Trip {TripId} to Driver {DriverId}",
                message.TripId, message.DriverId);
            return;
        }

        // 4. Update trip status
        trip.AssignedDriverId = message.DriverId;
        trip.Status = TripStatus.DriverAccepted;
        await _repo.UpdateAsync(trip, ct);

        // 5. Publish TripAssigned event
        var assignedEvent = new TripAssigned(
            TripId: trip.Id,
            DriverId: message.DriverId,
            AssignedAtUtc: DateTime.UtcNow
        );
        await _bus.PublishAsync(Routing.Keys.TripAssigned, assignedEvent, ct);

        // Note: Offer will be automatically removed via Redis TTL expiry
        _logger.LogInformation(
            "Successfully auto-assigned Trip {TripId} to Driver {DriverId}",
            message.TripId, message.DriverId);
    }
}
