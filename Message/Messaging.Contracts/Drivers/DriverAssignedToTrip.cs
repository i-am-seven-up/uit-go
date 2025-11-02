using Messaging.Contracts.Abstractions;

namespace Messaging.Contracts.Drivers
{
    public sealed record DriverAssignedToTrip(
        Guid TripId,
        Guid DriverId
    ) : IIntegrationEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
    }
}
