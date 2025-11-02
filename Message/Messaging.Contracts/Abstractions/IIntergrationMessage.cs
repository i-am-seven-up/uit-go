using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.Contracts.Abstractions
{
    public interface IIntegrationMessage
    {
        Guid MessageId { get; init; }         // unique per publish
        DateTime OccurredAtUtc { get; init; } // creation/publish time (UTC)
        string? CorrelationId { get; init; }  // trace same request across services
        string? CausationId { get; init; }    // which message/request caused this
    }
}
