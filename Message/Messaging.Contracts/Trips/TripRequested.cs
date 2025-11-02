using Messaging.Contracts.Abstractions;
using Messaging.Contracts.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.Contracts.Trips
{
    public sealed record TripRequested(
    Guid TripId,
    Guid RiderId,
    GeoPoint Start,
    GeoPoint End
) : IIntegrationEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
    }
}
