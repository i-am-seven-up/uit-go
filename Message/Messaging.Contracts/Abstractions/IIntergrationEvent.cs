using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.Contracts.Abstractions
{
    /// Marker for domain/integration events (past-tense, fan-out via broker).
    public interface IIntegrationEvent : IIntegrationMessage { }
}
