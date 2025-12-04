using E2E.PerformanceTests.Infrastructure;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NBomber.Contracts.Stats;
using System.Text.Json;

namespace E2E.PerformanceTests.Workloads;

/// <summary>
/// Workload A: High-Volume Trip Creation
/// Simulates rush hour with many simultaneous trip requests
/// </summary>
public class WorkloadA_TripCreation
{
    public static async Task<ScenarioStats> RunAsync()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   WORKLOAD A: HIGH-VOLUME TRIP CREATION                  ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Setup: Seed drivers
        using var redis = new RedisHelper();
        await redis.CleanupAsync();
        await redis.SeedDriversAsync(TestConfig.WorkloadA.DriversToSeed);

        var onlineDrivers = await redis.GetOnlineDriverCount();
        Console.WriteLine($"Online drivers: {onlineDrivers}");
        Console.WriteLine($"Test duration: {TestConfig.WorkloadA.DurationSeconds}s");
        Console.WriteLine($"Concurrent users: {TestConfig.WorkloadA.ConcurrentUsers}");
        Console.WriteLine($"Ramp-up period: {TestConfig.WorkloadA.RampUpSeconds}s");
        Console.WriteLine();

        // Create HTTP factory
        var httpFactory = HttpClientFactory.Create();

        // Define the trip creation scenario
        var scenario = Scenario.Create("trip_creation", async context =>
        {
            var passengerId = Guid.NewGuid();
            var token = JwtTokenHelper.GeneratePassengerToken(passengerId);

            var (pickupLat, pickupLng) = TestConfig.HCMCCoordinates.GetRandomLocation();
            var (dropoffLat, dropoffLng) = TestConfig.HCMCCoordinates.GetRandomLocation();

            var jsonBody = JsonSerializer.Serialize(new
            {
                pickupLat,
                pickupLng,
                dropoffLat,
                dropoffLng
            });

            var request = Http.CreateRequest("POST", $"{TestConfig.ApiGatewayUrl}/api/trips")
                .WithHeader("Authorization", $"Bearer {token}")
                .WithBody(new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"));

            var response = await Http.Send(httpFactory, request);

            return response;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.RampingInject(
                rate: TestConfig.WorkloadA.ConcurrentUsers,
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromSeconds(TestConfig.WorkloadA.RampUpSeconds)
            ),
            Simulation.Inject(
                rate: TestConfig.WorkloadA.ConcurrentUsers,
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromSeconds(TestConfig.WorkloadA.DurationSeconds - TestConfig.WorkloadA.RampUpSeconds)
            )
        );

        // Run the test
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        // Print results
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  WORKLOAD A RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════════════");

        if (stats.ScenarioStats.Length == 0)
        {
            Console.WriteLine("❌ No statistics available");
            return null!;
        }

        var scenarioStats = stats.ScenarioStats[0];

        if (scenarioStats.StepStats.Length == 0)
        {
            Console.WriteLine("✓ Test completed successfully!");
            Console.WriteLine($"Scenario: {scenarioStats.ScenarioName}");
            Console.WriteLine($"Duration: {scenarioStats.Duration}");
            return scenarioStats;
        }

        var stepStats = scenarioStats.StepStats[0];

        Console.WriteLine($"Total Requests: {stepStats.Ok.Request.Count + stepStats.Fail.Request.Count}");
        Console.WriteLine($"Success: {stepStats.Ok.Request.Count} ({stepStats.Ok.Request.Percent}%)");
        Console.WriteLine($"Failed: {stepStats.Fail.Request.Count} ({stepStats.Fail.Request.Percent}%)");
        Console.WriteLine();
        Console.WriteLine("Latency (Request):");
        Console.WriteLine($"  p50: {stepStats.Ok.Latency.Percent50}ms");
        Console.WriteLine($"  p75: {stepStats.Ok.Latency.Percent75}ms");
        Console.WriteLine($"  p90: {stepStats.Ok.Latency.Percent90}ms");
        Console.WriteLine($"  p99: {stepStats.Ok.Latency.Percent99}ms");
        Console.WriteLine($"  Mean: {stepStats.Ok.Latency.Mean}ms");
        Console.WriteLine($"  StdDev: {stepStats.Ok.Latency.StdDev}ms");
        Console.WriteLine();
        Console.WriteLine($"Throughput: {stepStats.Ok.Request.RPS} req/s");
        Console.WriteLine();

        // Redis metrics
        var finalOnlineDrivers = await redis.GetOnlineDriverCount();
        var availableDrivers = await redis.GetAvailableDriverCount();
        var memoryBytes = await redis.GetMemoryUsageBytes();
        var memoryMB = memoryBytes / (1024.0 * 1024.0);

        Console.WriteLine("Redis Metrics:");
        Console.WriteLine($"  Online drivers: {finalOnlineDrivers}");
        Console.WriteLine($"  Available drivers: {availableDrivers}");
        Console.WriteLine($"  Memory usage: {memoryMB:F2} MB");
        Console.WriteLine();

        // Export results
        ExportResults("WorkloadA", stepStats, new Dictionary<string, object>
        {
            { "online_drivers", finalOnlineDrivers },
            { "available_drivers", availableDrivers },
            { "redis_memory_mb", memoryMB }
        });

        return scenarioStats;
    }

    private static void ExportResults(string workloadName, StepStats stats, Dictionary<string, object> additionalMetrics)
    {
        var results = new
        {
            workload = workloadName,
            timestamp = DateTime.UtcNow,
            total_requests = stats.Ok.Request.Count + stats.Fail.Request.Count,
            success_count = stats.Ok.Request.Count,
            success_rate = stats.Ok.Request.Percent,
            failed_count = stats.Fail.Request.Count,
            latency = new
            {
                p50 = stats.Ok.Latency.Percent50,
                p75 = stats.Ok.Latency.Percent75,
                p90 = stats.Ok.Latency.Percent90(),
                p99 = stats.Ok.Latency.Percent99,
                mean = stats.Ok.Latency.Mean(),
                stddev = stats.Ok.Latency.StdDev,
                min = stats.Ok.Latency.MinMs,
                max = stats.Ok.Latency.MaxMs
            },
            throughput_rps = stats.Ok.Request.RPS,
            additional_metrics = additionalMetrics
        };

        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        var fileName = $"results_{workloadName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        File.WriteAllText(fileName, json);
        Console.WriteLine($"Results exported to: {fileName}");
    }
}
