using E2E.PerformanceTests.Infrastructure;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NBomber.Contracts.Stats;
using System.Text.Json;

namespace E2E.PerformanceTests.Workloads;

/// <summary>
/// Workload D: GEO Search Stress Test (Hot Read Path)
/// Tests Redis GEORADIUS performance under heavy concurrent search load
/// </summary>
public class WorkloadD_GeoSearchStress
{
    public static async Task<ScenarioStats> RunAsync()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   WORKLOAD D: HARDCORE GEO SEARCH STRESS (HOT READ)     ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Setup: Seed drivers
        using var redis = new RedisHelper();
        await redis.CleanupAsync();
        await redis.SeedDriversAsync(TestConfig.WorkloadD.DriversToSeed);

        var onlineDrivers = await redis.GetOnlineDriverCount();
        Console.WriteLine($"✓ Seeded {onlineDrivers} drivers");
        Console.WriteLine();
        Console.WriteLine("HARDCORE TEST PROFILE:");
        Console.WriteLine($"  Search rate: {TestConfig.WorkloadD.OptimizedSearchRate} searches/sec");
        Console.WriteLine($"  Search radius: {TestConfig.WorkloadD.SearchRadiusKm} km");
        Console.WriteLine($"  Test duration: {TestConfig.WorkloadD.DurationSeconds}s");
        Console.WriteLine($"  Total searches: {TestConfig.WorkloadD.OptimizedSearchRate * TestConfig.WorkloadD.DurationSeconds}");
        Console.WriteLine();

        var initialMemory = await redis.GetMemoryUsageBytes();
        Console.WriteLine($"Initial Redis memory: {initialMemory / (1024.0 * 1024.0):F2} MB");
        Console.WriteLine();

        // Create HTTP factory
        var httpFactory = HttpClientFactory.Create();
        var random = new Random();

        // Define the GEO search scenario
        var scenario = Scenario.Create("geo_search_stress", async context =>
        {
            // Random search location in HCMC area
            var (lat, lng) = TestConfig.HCMCCoordinates.GetRandomLocation();
            var radiusKm = TestConfig.WorkloadD.SearchRadiusKm;

            // Note: Endpoint is /api/drivers/search (not /nearby)
            var request = Http.CreateRequest("GET",
                $"{TestConfig.ApiGatewayUrl}/api/drivers/search?lat={lat}&lng={lng}&radiusKm={radiusKm}");

            var response = await Http.Send(httpFactory, request);

            return response;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // Constant search load to stress Redis GEORADIUS
            Simulation.Inject(
                rate: TestConfig.WorkloadD.OptimizedSearchRate,
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromSeconds(TestConfig.WorkloadD.DurationSeconds)
            )
        );

        // Run the test
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        // Print results
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  WORKLOAD D RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════════════");

        if (stats.ScenarioStats.Length == 0)
        {
            Console.WriteLine("❌ No statistics available");
            return null!;
        }

        var scenarioStats = stats.ScenarioStats[0];

        if (scenarioStats.StepStats.Length == 0)
        {
            Console.WriteLine("✓ Test completed successfully (No step stats available)");
            Console.WriteLine($"Scenario: {scenarioStats.ScenarioName}");
            Console.WriteLine($"Duration: {scenarioStats.Duration}");
            return scenarioStats;
        }

        var stepStats = scenarioStats.StepStats[0];

        Console.WriteLine($"Total Searches: {stepStats.Ok.Request.Count + stepStats.Fail.Request.Count}");
        Console.WriteLine($"Success: {stepStats.Ok.Request.Count} ({stepStats.Ok.Request.Percent}%)");
        Console.WriteLine($"Failed: {stepStats.Fail.Request.Count} ({stepStats.Fail.Request.Percent}%)");
        Console.WriteLine();
        Console.WriteLine("Latency (Search):");
        Console.WriteLine($"  p50: {stepStats.Ok.Latency.Percent50}ms");
        Console.WriteLine($"  p75: {stepStats.Ok.Latency.Percent75}ms");
        Console.WriteLine($"  p90: {stepStats.Ok.Latency.Percent90}ms");
        Console.WriteLine($"  p95: {stepStats.Ok.Latency.Percent95}ms");
        Console.WriteLine($"  p99: {stepStats.Ok.Latency.Percent99}ms");
        Console.WriteLine($"  Mean: {stepStats.Ok.Latency.Mean}ms");
        Console.WriteLine($"  StdDev: {stepStats.Ok.Latency.StdDev}ms");
        Console.WriteLine();
        Console.WriteLine($"Throughput: {stepStats.Ok.Request.RPS} searches/s");
        Console.WriteLine();

        // Redis metrics
        var finalMemory = await redis.GetMemoryUsageBytes();
        var memoryMB = finalMemory / (1024.0 * 1024.0);

        Console.WriteLine("Redis Metrics:");
        Console.WriteLine($"  Final memory: {memoryMB:F2} MB");
        Console.WriteLine($"  Memory delta: {(finalMemory - initialMemory) / (1024.0 * 1024.0):F2} MB");
        Console.WriteLine();

        // Validation
        var p95LatencyMs = stepStats.Ok.Latency.Percent95;
        var errorRate = stepStats.Fail.Request.Percent;

        Console.WriteLine("HARDCORE TEST VALIDATION:");
        Console.WriteLine($"  p95 Latency: {p95LatencyMs}ms (Target: <15ms) {(p95LatencyMs < 15 ? "✓ PASS" : "✗ FAIL")}");
        Console.WriteLine($"  Error Rate: {errorRate}% (Target: <1%) {(errorRate < 1 ? "✓ PASS" : "✗ FAIL")}");
        Console.WriteLine($"  Throughput: {stepStats.Ok.Request.RPS} searches/s (Target: ≥5000/s) {(stepStats.Ok.Request.RPS >= 5000 ? "✓ PASS" : "✗ FAIL")}");
        Console.WriteLine();

        return scenarioStats;
    }
}
