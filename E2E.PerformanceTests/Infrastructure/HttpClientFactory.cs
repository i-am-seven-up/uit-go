namespace E2E.PerformanceTests.Infrastructure;

/// <summary>
/// Factory for creating HTTP clients for NBomber scenarios
/// Uses connection pooling to prevent socket exhaustion under high load
/// </summary>
public static class HttpClientFactory
{
    // Shared HttpClient with connection pooling configured
    // This prevents socket exhaustion by reusing TCP connections
    private static readonly Lazy<HttpClient> _sharedClient = new Lazy<HttpClient>(() =>
    {
        var handler = new SocketsHttpHandler
        {
            // Connection pool settings for high throughput
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 200, // Allow up to 200 concurrent connections per endpoint

            // Enable HTTP/2 if available
            EnableMultipleHttp2Connections = true
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    });

    public static HttpClient Create()
    {
        return _sharedClient.Value;
    }

    public static HttpClient Create(string name)
    {
        // For now, return the same shared client
        // In the future, could maintain separate pools per name
        return _sharedClient.Value;
    }
}
