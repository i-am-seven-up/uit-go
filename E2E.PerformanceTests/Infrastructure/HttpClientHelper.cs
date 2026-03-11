using System.Net.Sockets;

namespace E2E.PerformanceTests.Infrastructure;

/// <summary>
/// Helper for creating high-throughput HTTP clients optimized for load testing
/// </summary>
public static class HttpClientHelper
{
    /// <summary>
    /// Creates an HttpClient configured for high concurrency and throughput
    /// </summary>
    /// <returns>Configured HttpClient instance</returns>
    public static HttpClient CreateHighThroughputClient()
    {
        var handler = new SocketsHttpHandler
        {
            // Connection pooling settings
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            
            // CRITICAL: Increase max connections per server
            // Default is ~100, we need 1000+ for high concurrency
            MaxConnectionsPerServer = 1000,
            
            // Enable HTTP/2 multiplexing
            EnableMultipleHttp2Connections = true,
            
            // Timeouts
            ConnectTimeout = TimeSpan.FromSeconds(10),
            
            // Keep-alive settings
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30)
        };
        
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
    
    /// <summary>
    /// Creates an HttpClient with custom connection limit
    /// </summary>
    public static HttpClient CreateClient(int maxConnectionsPerServer = 1000)
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = maxConnectionsPerServer,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true
        };
        
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
}
