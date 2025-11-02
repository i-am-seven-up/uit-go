using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.RabbitMQ.Abstractions
{
    public interface IEventPublisher
    {
        Task PublishAsync<T>(string routingKey, T payload, CancellationToken ct = default);
    }
}
