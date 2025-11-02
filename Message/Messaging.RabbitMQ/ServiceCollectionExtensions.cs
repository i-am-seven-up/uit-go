using Messaging.RabbitMQ.Abstractions;
using Messaging.RabbitMQ.Config;
using Messaging.RabbitMQ.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.RabbitMQ
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddRabbitMqEventBus(
            this IServiceCollection services,
            IConfiguration cfg,
            string exchangeName)
        {
            services.Configure<RabbitMqOptions>(cfg.GetSection("RabbitMQ"));

            // Publisher
            services.AddSingleton<IEventPublisher>(sp =>
            {
                var opt = sp.GetRequiredService<IOptions<RabbitMqOptions>>();
                return new RabbitMqPublisher(opt, exchangeName);
            });

            return services;
        }
    }
}
