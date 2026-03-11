using Messaging.Contracts.Abstractions;

namespace Messaging.Contracts.Trips
{
    /// <summary>
    /// Event raised when a trip is automatically assigned to a driver after the offer timeout
    /// (driver did not explicitly accept or decline within TTL period)
    /// </summary>
    public sealed record TripAutoAssigned(
        Guid TripId,
        Guid DriverId,
        DateTime TimeoutAtUtc
    ) : IIntegrationEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
    }
}
