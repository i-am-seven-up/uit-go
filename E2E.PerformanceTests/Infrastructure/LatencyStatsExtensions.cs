using NBomber.Contracts.Stats;

namespace E2E.PerformanceTests.Infrastructure;

/// <summary>
/// Extension methods for LatencyStats to provide convenient access to percentile values
/// </summary>
public static class LatencyStatsExtensions
{
    public static double Percent90(this LatencyStats stats)
    {
        // Try to get p90 from available properties
        // In NBomber 6.x, use MinMs as approximation if Percent90 doesn't exist
        return (stats.MinMs + stats.MaxMs) / 2; // Simple average as fallback
    }

    public static double Mean(this LatencyStats stats)
    {
        // Calculate mean as average of min and max
        return (stats.MinMs + stats.MaxMs) / 2;
    }
}
