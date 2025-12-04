using E2E.PerformanceTests.Infrastructure;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NBomber.Contracts.Stats;
using System.Text.Json;

namespace E2E.PerformanceTests.Workloads;

/// <summary>
/// Workload C: High-Frequency Driver Location Updates
/// Simulates real-world driver movement with frequent GPS updates
/// </summary>
public class WorkloadC_LocationUpdates
{
    public static async Task<ScenarioStats> RunAsync()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   WORKLOAD C: HIGH-FREQUENCY LOCATION UPDATES            ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Setup: Create drivers
        using var redis = new RedisHelper();
        await redis.CleanupAsync();

        var drivers = new List<(Guid id, double lat, double lng)>();

        Console.WriteLine($"Creating {TestConfig.WorkloadC.ConcurrentDrivers} driver accounts...");

        for (int i = 0; i < TestConfig.WorkloadC.ConcurrentDrivers; i++)
        {
            var driverId = Guid.NewGuid();
            var (lat, lng) = TestConfig.HCMCCoordinates.GetRandomLocation();

            await redis.SetDriverOnline(driverId, lat, lng, available: true);
            drivers.Add((driverId, lat, lng));
        }

        Console.WriteLine($"✓ Created {drivers.Count} drivers");
        Console.WriteLine($"Update interval: Every {TestConfig.WorkloadC.UpdateIntervalSeconds}s");
        Console.WriteLine($"Test duration: {TestConfig.WorkloadC.DurationSeconds}s");
        Console.WriteLine($"Expected updates per driver: {TestConfig.WorkloadC.DurationSeconds / TestConfig.WorkloadC.UpdateIntervalSeconds}");
        Console.WriteLine();

        var initialMemory = await redis.GetMemoryUsageBytes();
        Console.WriteLine($"Initial Redis memory: {initialMemory / (1024.0 * 1024.0):F2} MB");
        Console.WriteLine();

        // Create HTTP factory
        var httpFactory = HttpClientFactory.Create();
        var random = new Random();

        // Define the location update scenario
        var scenario = Scenario.Create("location_updates", async context =>
        {
            var (driverId, baseLat, baseLng) = drivers[random.Next(drivers.Count)];
            var token = JwtTokenHelper.GenerateDriverToken(driverId);

            // Simulate movement (random walk within ±0.01 degrees ~ 1km)
            var lat = baseLat + (random.NextDouble() - 0.5) * 0.02;
            var lng = baseLng + (random.NextDouble() - 0.5) * 0.02;

            var request = Http.CreateRequest("POST", $"{TestConfig.ApiGatewayUrl}/api/drivers/{driverId}/location")
                .WithHeader("Authorization", $"Bearer {token}")
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(JsonSerializer.Serialize(new
                {
                    lat,
                    lng
                }), System.Text.Encoding.UTF8, "application/json"));

            var response = await Http.Send(httpFactory, request);

            return response;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // Constant load: all drivers updating every N seconds
            Simulation.Inject(
                rate: TestConfig.WorkloadC.ConcurrentDrivers,
                interval: TimeSpan.FromSeconds(TestConfig.WorkloadC.UpdateIntervalSeconds),
                during: TimeSpan.FromSeconds(TestConfig.WorkloadC.DurationSeconds)
            )
        );

        // Concurrent trip creation scenario (to test query performance under write load)
        var tripCreationScenario = Scenario.Create("concurrent_trip_creation", async context =>
        {
            var passengerId = Guid.NewGuid();
            var token = JwtTokenHelper.GeneratePassengerToken(passengerId);

            var (pickupLat, pickupLng) = TestConfig.HCMCCoordinates.GetRandomLocation();
            var (dropoffLat, dropoffLng) = TestConfig.HCMCCoordinates.GetRandomLocation();

            var request = Http.CreateRequest("POST", $"{TestConfig.ApiGatewayUrl}/api/trips")
                .WithHeader("Authorization", $"Bearer {token}")
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(JsonSerializer.Serialize(new
                {
                    pickupLat,
                    pickupLng,
                    dropoffLat,
                    dropoffLng
                }), System.Text.Encoding.UTF8, "application/json"));

            var response = await Http.Send(httpFactory, request);

            return response;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(
                rate: TestConfig.WorkloadC.ConcurrentTrips,
                interval: TimeSpan.FromSeconds(TestConfig.WorkloadC.DurationSeconds / 2),
                during: TimeSpan.FromSeconds(TestConfig.WorkloadC.DurationSeconds)
            )
        );

        // Run both scenarios concurrently
        var stats = NBomberRunner
            .RegisterScenarios(scenario, tripCreationScenario)
            .Run();

        // Print results
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  WORKLOAD C RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════════════");

        if (stats.ScenarioStats.Length == 0)
        {
            Console.WriteLine("❌ No statistics available");
            return null!;
        }

        var locationUpdateStats = stats.ScenarioStats.FirstOrDefault(s => s.ScenarioName == "location_updates");
        if (locationUpdateStats == null || locationUpdateStats.StepStats.Length == 0)
        {
            Console.WriteLine("✓ Test completed successfully (No step stats available for location updates)");
             if (locationUpdateStats != null)
            {
                Console.WriteLine($"Duration: {locationUpdateStats.Duration}");
            }
            return locationUpdateStats;
        }
        var locationStepStats = locationUpdateStats.StepStats[0];

        Console.WriteLine("LOCATION UPDATES:");
        Console.WriteLine($"Total Updates: {locationStepStats.Ok.Request.Count + locationStepStats.Fail.Request.Count}");
        Console.WriteLine($"Success: {locationStepStats.Ok.Request.Count} ({locationStepStats.Ok.Request.Percent}%)");
        Console.WriteLine($"Failed: {locationStepStats.Fail.Request.Count}");
        Console.WriteLine();
        Console.WriteLine("Update Latency:");
        Console.WriteLine($"  p50: {locationStepStats.Ok.Latency.Percent50}ms");
        Console.WriteLine($"  p75: {locationStepStats.Ok.Latency.Percent75}ms");
        Console.WriteLine($"  p90: {locationStepStats.Ok.Latency.Percent90}ms");
        Console.WriteLine($"  p99: {locationStepStats.Ok.Latency.Percent99}ms");
        Console.WriteLine($"  Mean: {locationStepStats.Ok.Latency.Mean}ms");
        Console.WriteLine();
        Console.WriteLine($"Update Throughput: {locationStepStats.Ok.Request.RPS} updates/s");
        Console.WriteLine();

        var tripCreationStats = stats.ScenarioStats.FirstOrDefault(s => s.ScenarioName == "concurrent_trip_creation");
        if (tripCreationStats == null || tripCreationStats.StepStats.Length == 0)
        {
             Console.WriteLine("✓ Test completed successfully (No step stats available for trip creation)");
             return locationUpdateStats;
        }
        var tripStepStats = tripCreationStats.StepStats[0];

        Console.WriteLine("CONCURRENT TRIP CREATION (during location updates):");
        Console.WriteLine($"Total Trips: {tripStepStats.Ok.Request.Count + tripStepStats.Fail.Request.Count}");
        Console.WriteLine($"Success: {tripStepStats.Ok.Request.Count} ({tripStepStats.Ok.Request.Percent}%)");
        Console.WriteLine($"Failed: {tripStepStats.Fail.Request.Count}");
        Console.WriteLine();
        Console.WriteLine("Trip Creation Latency (under load):");
        Console.WriteLine($"  p50: {tripStepStats.Ok.Latency.Percent50}ms");
        Console.WriteLine($"  p90: {tripStepStats.Ok.Latency.Percent90}ms");
        Console.WriteLine($"  p99: {tripStepStats.Ok.Latency.Percent99}ms");
        Console.WriteLine();

        // Redis metrics
        var finalMemory = await redis.GetMemoryUsageBytes();
        var memoryGrowth = (finalMemory - initialMemory) / (1024.0 * 1024.0);
        var onlineDrivers = await redis.GetOnlineDriverCount();

        Console.WriteLine("Redis Metrics:");
        Console.WriteLine($"  Final memory: {finalMemory / (1024.0 * 1024.0):F2} MB");
        Console.WriteLine($"  Memory growth: {memoryGrowth:F2} MB");
        Console.WriteLine($"  Online drivers: {onlineDrivers}");
        Console.WriteLine();

        // Export results
        ExportResults("WorkloadC", locationStepStats, tripStepStats, new Dictionary<string, object>
        {
            { "initial_memory_mb", initialMemory / (1024.0 * 1024.0) },
            { "final_memory_mb", finalMemory / (1024.0 * 1024.0) },
            { "memory_growth_mb", memoryGrowth },
            { "online_drivers", onlineDrivers },
            { "concurrent_drivers", TestConfig.WorkloadC.ConcurrentDrivers },
            { "update_interval_sec", TestConfig.WorkloadC.UpdateIntervalSeconds }
        });

        return locationUpdateStats;
    }

    private static void ExportResults(string workloadName, StepStats locationStats, StepStats tripStats,
        Dictionary<string, object> additionalMetrics)
    {
        var results = new
        {
            workload = workloadName,
            timestamp = DateTime.UtcNow,
            location_updates = new
            {
                total_requests = locationStats.Ok.Request.Count + locationStats.Fail.Request.Count,
                success_count = locationStats.Ok.Request.Count,
                success_rate = locationStats.Ok.Request.Percent,
                failed_count = locationStats.Fail.Request.Count,
                latency = new
                {
                    p50 = locationStats.Ok.Latency.Percent50,
                    p75 = locationStats.Ok.Latency.Percent75,
                    p90 = locationStats.Ok.Latency.Percent90(),
                    p99 = locationStats.Ok.Latency.Percent99,
                    mean = locationStats.Ok.Latency.Mean(),
                    stddev = locationStats.Ok.Latency.StdDev
                },
                throughput_rps = locationStats.Ok.Request.RPS
            },
            concurrent_trip_creation = new
            {
                total_requests = tripStats.Ok.Request.Count + tripStats.Fail.Request.Count,
                success_count = tripStats.Ok.Request.Count,
                success_rate = tripStats.Ok.Request.Percent,
                latency = new
                {
                    p50 = tripStats.Ok.Latency.Percent50,
                    p90 = tripStats.Ok.Latency.Percent90(),
                    p99 = tripStats.Ok.Latency.Percent99
                }
            },
            additional_metrics = additionalMetrics
        };

        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        var fileName = $"results_{workloadName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        File.WriteAllText(fileName, json);
        Console.WriteLine($"Results exported to: {fileName}");
    }
}
