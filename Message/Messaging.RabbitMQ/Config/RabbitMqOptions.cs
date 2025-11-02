using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.RabbitMQ.Config
{
    public sealed class RabbitMqOptions
    {
        public string HostName { get; set; } = "rabbitmq";
        public int Port { get; set; } = 5672;
        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string VirtualHost { get; set; } = "/";
        public ushort PrefetchCount { get; set; } = 50; 
        public string ClientName { get; set; } = "uitgo-app"; 
    }
}
