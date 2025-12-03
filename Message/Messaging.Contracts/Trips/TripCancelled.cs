using Messaging.Contracts.Abstractions;

namespace Messaging.Contracts.Trips
{
    public sealed record TripCancelled(
        Guid TripId,
        Guid DriverId,
        string Reason
    ) : IIntegrationEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
    }
}
