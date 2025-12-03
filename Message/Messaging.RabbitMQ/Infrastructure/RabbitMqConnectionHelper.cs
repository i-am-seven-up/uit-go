using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System;
using System.Threading;

namespace Messaging.RabbitMQ.Infrastructure
{
    internal static class RabbitMqConnectionHelper
    {
        public static IConnection ConnectWithRetry(ConnectionFactory factory, int maxRetries = 10)
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    attempts++;
                    return factory.CreateConnection();
                }
                catch (BrokerUnreachableException)
                {
                    if (attempts >= maxRetries) throw;
                    
                    // Linear/Exponential backoff
                    var delaySeconds = Math.Min(Math.Pow(2, attempts), 30);
                    Console.WriteLine($"[RabbitMQ] Connection failed. Retrying in {delaySeconds}s... (Attempt {attempts}/{maxRetries})");
                    Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
                }
            }
        }
    }
}
