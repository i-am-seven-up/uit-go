using Messaging.Contracts.Abstractions;
using Messaging.Contracts.Common;

namespace Messaging.Contracts.Trips
{
    public sealed record TripCreated(
        Guid TripId,
        Guid PassengerId,
        GeoPoint Start,
        GeoPoint End,
        DateTime CreatedAtUtc
    ) : IIntegrationEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
    }
}
