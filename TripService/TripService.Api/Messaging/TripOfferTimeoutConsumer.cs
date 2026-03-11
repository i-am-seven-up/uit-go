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

namespace TripService.Api.Messaging;

/// <summary>
/// Handles TripOfferTimeout event when a driver declines or fails to respond to a trip offer.
/// Triggers the retry mechanism to find another available driver.
/// </summary>
public sealed class TripOfferTimeoutConsumer : BaseRabbitConsumer<TripOfferTimeout>
{
    private readonly ITripRepository _repo;
    private readonly TripMatchService _matchService;
    private readonly IOfferStore _offers;
    private readonly IEventPublisher _bus;
    private readonly ILogger<TripOfferTimeoutConsumer> _logger;
    private const int MAX_RETRY_ATTEMPTS = 3;
    private const int OFFER_TTL_SECONDS = 15;

    public TripOfferTimeoutConsumer(
        ILogger<TripOfferTimeoutConsumer> logger,
        IOptions<RabbitMqOptions> options,
        ITripRepository repo,
        TripMatchService matchService,
        IOfferStore offers,
        IEventPublisher bus)
        : base(logger, options, Routing.Exchange)
    {
        _repo = repo;
        _matchService = matchService;
        _offers = offers;
        _bus = bus;
        _logger = logger;
    }

    protected override string RoutingKey => Routing.Keys.TripOfferTimeout;
    protected override string QueueName => "trip.offer.timeout";

    protected override async Task HandleAsync(
        TripOfferTimeout message,
        BasicDeliverEventArgs ea,
        IModel channel,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing timeout for Trip {TripId}, Driver {DriverId} (retry attempt {RetryCount})",
            message.TripId, message.DriverId, message.RetryCount);

        // 1. Get trip
        var trip = await _repo.GetAsync(message.TripId, ct);
        if (trip == null)
        {
            _logger.LogWarning("Trip {TripId} not found", message.TripId);
            return;
        }

        // Note: Offer will be automatically removed via Redis TTL expiry

        // 2. Check if we've exceeded retry limit
        var triedDrivers = await _matchService.GetTriedDriversAsync(message.TripId);
        if (triedDrivers.Count >= MAX_RETRY_ATTEMPTS)
        {
            _logger.LogWarning(
                "Trip {TripId} exceeded max retry attempts ({Count}/{Max}), no more drivers to try",
                message.TripId, triedDrivers.Count, MAX_RETRY_ATTEMPTS);

            // TODO: Publish TripMatchingFailed event or update trip status
            return;
        }

        // 4. Find next available driver (excluding already tried drivers)
        var nextDriver = await _matchService.FindBestDriverAsync(
            lat: trip.StartLat,
            lng: trip.StartLng,
            radiusKm: 20.0,
            take: 10,
            excludeDriverIds: triedDrivers);

        if (nextDriver == null)
        {
            _logger.LogWarning(
                "No available drivers found for Trip {TripId} after {Count} attempts",
                message.TripId, triedDrivers.Count);

            // TODO: Publish TripMatchingFailed event
            return;
        }

        // 5. Track this new driver as tried
        await _matchService.AddTriedDriverAsync(trip.Id, nextDriver.DriverId);

        // 6. Create new offer
        await _offers.SetPendingAsync(
            trip.Id,
            nextDriver.DriverId,
            TimeSpan.FromSeconds(OFFER_TTL_SECONDS),
            ct);

        // 7. Publish TripOffered event for the new driver
        var offeredEvent = new TripOffered(
            TripId: trip.Id,
            DriverId: nextDriver.DriverId,
            TtlSeconds: OFFER_TTL_SECONDS
        );

        await _bus.PublishAsync(Routing.Keys.TripOffered, offeredEvent, ct);

        _logger.LogInformation(
            "Retrying Trip {TripId} with new Driver {DriverId} (attempt {Attempt})",
            trip.Id, nextDriver.DriverId, triedDrivers.Count + 1);
    }
}
