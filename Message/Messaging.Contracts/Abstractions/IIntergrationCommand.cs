using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.Contracts.Abstractions
{
    /// Marker for commands (intent to perform, typically request/response). Define it here but may use gRPC instead
    public interface IIntegrationCommand : IIntegrationMessage { }
}
