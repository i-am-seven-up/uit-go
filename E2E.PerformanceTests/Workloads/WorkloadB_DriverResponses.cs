using E2E.PerformanceTests.Infrastructure;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NBomber.Contracts.Stats;
using System.Text.Json;

namespace E2E.PerformanceTests.Workloads;

/// <summary>
/// Workload B: Burst of Driver Accept/Decline Events
/// Simulates many drivers responding to trip assignments simultaneously
/// </summary>
public class WorkloadB_DriverResponses
{
    public static async Task<ScenarioStats> RunAsync()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   WORKLOAD B: DRIVER ACCEPT/DECLINE BURSTS               ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Setup: Create trips with assigned drivers
        using var redis = new RedisHelper();
        await redis.CleanupAsync();

        var drivers = new List<Guid>();
        var trips = new List<(Guid tripId, Guid driverId)>();

        Console.WriteLine($"Creating {TestConfig.WorkloadB.ConcurrentDrivers} trips with assigned drivers...");

        using var httpClient = new HttpClient();

        for (int i = 0; i < TestConfig.WorkloadB.ConcurrentDrivers; i++)
        {
            var driverId = Guid.NewGuid();
            var passengerId = Guid.NewGuid();
            drivers.Add(driverId);

            // Set driver online
            var (lat, lng) = TestConfig.HCMCCoordinates.GetRandomLocation();
            await redis.SetDriverOnline(driverId, lat, lng, available: true);

            // Create trip (should assign to this driver)
            var passengerToken = JwtTokenHelper.GeneratePassengerToken(passengerId);
            var (pickupLat, pickupLng) = (lat, lng); // Same location as driver
            var (dropoffLat, dropoffLng) = TestConfig.HCMCCoordinates.GetRandomLocation();

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {passengerToken}");

            var requestBody = JsonSerializer.Serialize(new
            {
                pickupLat,
                pickupLng,
                dropoffLat,
                dropoffLng
            });

            var response = await httpClient.PostAsync(
                $"{TestConfig.ApiGatewayUrl}/api/trips",
                new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json")
            );

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var tripResponse = JsonSerializer.Deserialize<TripResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (tripResponse != null && tripResponse.Id != Guid.Empty)
                {
                    trips.Add((tripResponse.Id, driverId));
                }
            }

            if ((i + 1) % 10 == 0)
            {
                Console.WriteLine($"  Created {i + 1}/{TestConfig.WorkloadB.ConcurrentDrivers} trips");
            }
        }

        Console.WriteLine($"✓ Created {trips.Count} trips");
        Console.WriteLine($"Accept rate: {TestConfig.WorkloadB.AcceptRate * 100}%");
        Console.WriteLine($"Test duration: {TestConfig.WorkloadB.DurationSeconds}s");
        Console.WriteLine();

        // Wait a bit for trip assignments to stabilize
        await Task.Delay(2000);

        // Create HTTP factory
        var httpFactory = HttpClientFactory.Create();
        var random = new Random();

        // Define the driver response scenario
        var scenario = Scenario.Create("driver_responses", async context =>
        {
            // Pick a random trip
            var (tripId, driverId) = trips[random.Next(trips.Count)];
            var token = JwtTokenHelper.GenerateDriverToken(driverId);

            // Decide accept or decline based on accept rate
            var shouldAccept = random.NextDouble() < TestConfig.WorkloadB.AcceptRate;
            var action = shouldAccept ? "accept" : "decline";

            var request = Http.CreateRequest("POST",
                    $"{TestConfig.ApiGatewayUrl}/api/drivers/trips/{tripId}/{action}")
                .WithHeader("Authorization", $"Bearer {token}");

            var response = await Http.Send(httpFactory, request);

            return response;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(
                rate: TestConfig.WorkloadB.ConcurrentDrivers,
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromSeconds(TestConfig.WorkloadB.DurationSeconds)
            )
        );

        // Run the test
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        // Print results
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  WORKLOAD B RESULTS");
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

        Console.WriteLine($"Total Requests: {stepStats.Ok.Request.Count + stepStats.Fail.Request.Count}");
        Console.WriteLine($"Success: {stepStats.Ok.Request.Count} ({stepStats.Ok.Request.Percent}%)");
        Console.WriteLine($"Failed: {stepStats.Fail.Request.Count} ({stepStats.Fail.Request.Percent}%)");
        Console.WriteLine();
        Console.WriteLine("Event Processing Latency:");
        Console.WriteLine($"  p50: {stepStats.Ok.Latency.Percent50}ms");
        Console.WriteLine($"  p75: {stepStats.Ok.Latency.Percent75}ms");
        Console.WriteLine($"  p90: {stepStats.Ok.Latency.Percent90}ms");
        Console.WriteLine($"  p99: {stepStats.Ok.Latency.Percent99}ms");
        Console.WriteLine($"  Mean: {stepStats.Ok.Latency.Mean}ms");
        Console.WriteLine();
        Console.WriteLine($"Event Processing Rate: {stepStats.Ok.Request.RPS} events/s");
        Console.WriteLine();

        // Verify state consistency
        await Task.Delay(3000); // Wait for event processing
        Console.WriteLine("Verifying state consistency...");

        var availableDrivers = await redis.GetAvailableDriverCount();
        Console.WriteLine($"  Available drivers after test: {availableDrivers}/{drivers.Count}");
        Console.WriteLine();

        // Export results
        ExportResults("WorkloadB", stepStats, new Dictionary<string, object>
        {
            { "total_trips", trips.Count },
            { "accept_rate_configured", TestConfig.WorkloadB.AcceptRate },
            { "available_drivers_after", availableDrivers }
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

    private class TripResponse
    {
        public Guid Id { get; set; }
        public Guid PassengerId { get; set; }
        public Guid? AssignedDriverId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
