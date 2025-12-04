namespace E2E.PerformanceTests.Infrastructure;

/// <summary>
/// Factory for creating HTTP clients for NBomber scenarios
/// </summary>
public static class HttpClientFactory
{
    public static HttpClient Create()
    {
        return new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public static HttpClient Create(string name)
    {
        return new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }
}
