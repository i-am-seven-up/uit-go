using Messaging.RabbitMQ.Abstractions;
using Messaging.RabbitMQ.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace Messaging.RabbitMQ.Infrastructure
{
    public abstract class BaseRabbitConsumer<T> : BackgroundService, IRabbitConsumer
    {
        private readonly ILogger _log;
        private readonly RabbitMqOptions _opt;
        private readonly string _exchangeName;
        private IConnection? _conn;
        private IModel? _ch;
        private string? _queueName;

        protected BaseRabbitConsumer(
            ILogger logger,
            IOptions<RabbitMqOptions> opt,
            string exchangeName)
        {
            _log = logger;
            _opt = opt.Value;
            _exchangeName = exchangeName;
        }

        protected abstract string RoutingKey { get; }
        protected abstract string QueueName { get; }
        protected abstract Task HandleAsync(T message, BasicDeliverEventArgs ea, IModel channel, CancellationToken ct);

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = _opt.HostName,
                Port = _opt.Port,
                UserName = _opt.UserName,
                Password = _opt.Password,
                VirtualHost = _opt.VirtualHost,
                ClientProvidedName = $"{_opt.ClientName}-{QueueName}-consumer",
                DispatchConsumersAsync = true
            };

            _conn = factory.CreateConnection();
            _ch = _conn.CreateModel();
            _ch.ExchangeDeclare(_exchangeName, ExchangeType.Topic, durable: true);

            var q = _ch.QueueDeclare(queue: QueueName, durable: true, exclusive: false, autoDelete: false);
            _queueName = q.QueueName;

            _ch.QueueBind(_queueName, _exchangeName, RoutingKey);
            _ch.BasicQos(0, _opt.PrefetchCount, false);

            var consumer = new AsyncEventingBasicConsumer(_ch);
            consumer.Received += async (_, ea) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.Span);
                    var msg = JsonSerializer.Deserialize<T>(json, JsonSerializerSettings.Default)!;

                    await HandleAsync(msg, ea, _ch!, stoppingToken);
                    _ch!.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "[RabbitMQ] Error handling {RoutingKey}", RoutingKey);
                    _ch!.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            _ch.BasicConsume(_queueName, autoAck: false, consumer);
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _ch?.Dispose();
            _conn?.Dispose();
            base.Dispose();
        }
    }
}
