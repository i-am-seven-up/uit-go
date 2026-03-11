using Messaging.Contracts.Abstractions;

namespace Messaging.Contracts.Trips
{
    /// <summary>
    /// Event raised when a trip offer times out and the driver declined or did not respond.
    /// This triggers the retry mechanism to find another driver.
    /// </summary>
    public sealed record TripOfferTimeout(
        Guid TripId,
        Guid DriverId,
        DateTime TimeoutAtUtc,
        int RetryCount
    ) : IIntegrationEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
    }
}
