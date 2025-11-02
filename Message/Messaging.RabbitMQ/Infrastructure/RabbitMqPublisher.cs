using Messaging.RabbitMQ.Abstractions;
using Messaging.RabbitMQ.Config;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Messaging.RabbitMQ.Infrastructure
{
    public sealed class RabbitMqPublisher : IEventPublisher, IDisposable
    {
        private readonly IConnection _conn;
        private readonly IModel _ch;
        private readonly string _exchangeName;

        public RabbitMqPublisher(IOptions<RabbitMqOptions> opt, string exchangeName)
        {
            _exchangeName = exchangeName;

            var o = opt.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.HostName,
                Port = o.Port,
                UserName = o.UserName,
                Password = o.Password,
                VirtualHost = o.VirtualHost,
                ClientProvidedName = $"{o.ClientName}-publisher",
                DispatchConsumersAsync = true
            };
            _conn = factory.CreateConnection();
            _ch = _conn.CreateModel();
            _ch.ExchangeDeclare(exchangeName, ExchangeType.Topic, durable: true);
        }

        public Task PublishAsync<T>(string routingKey, T payload, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(payload);
            var body = Encoding.UTF8.GetBytes(json);

            var props = _ch.CreateBasicProperties();
            props.ContentType = "application/json";
            props.DeliveryMode = 2; // persistent

            _ch.BasicPublish(exchange: _exchangeName, routingKey: routingKey, basicProperties: props, body: body);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _ch?.Dispose();
            _conn?.Dispose();
        }
    }
}
