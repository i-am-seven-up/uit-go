using Messaging.RabbitMQ.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TripService.IntegrationTests.Infrastructure
{
    public class MockEventPublisher : IEventPublisher
    {
        public List<(string routingKey, object? payload)> PublishedEvents { get; } = new();

        public Task PublishAsync<T>(string routingKey, T payload, CancellationToken ct = default)
        {
            PublishedEvents.Add((routingKey, payload));
            return Task.CompletedTask;
        }
    }

    public class MockEventConsumer : IRabbitConsumer
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SubscribeAsync<T>(string routingKey, System.Func<T, CancellationToken, Task> handler, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
