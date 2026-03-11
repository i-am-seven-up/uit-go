using E2E.PerformanceTests.Infrastructure;
using E2E.PerformanceTests.Workloads;

namespace E2E.PerformanceTests;

class Program
{
    static async Task Main(string[] args)
    {
        try { Console.Clear(); } catch { /* Ignore if console not available */ }
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   UIT-GO E2E PERFORMANCE TESTS                           ║");
        Console.WriteLine("║   Phase 1 Baseline & Phase 2 Comparison                  ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Check if services are running
        Console.WriteLine("Checking prerequisites...");
        if (!await CheckServicesHealthy())
        {
            Console.WriteLine("❌ Services are not healthy. Please ensure docker-compose is running.");
            Console.WriteLine("   Run: docker-compose up -d");
            return;
        }

        Console.WriteLine("✓ All services are healthy");
        Console.WriteLine();

        // Parse command line arguments
        var workload = args.Length > 0 ? args[0].ToLower() : "";

        try
        {
            switch (workload)
            {
                case "a":
                case "workload-a":
                case "trip-creation":
                    await WorkloadA_TripCreation.RunAsync();
                    break;

                case "b":
                case "workload-b":
                case "driver-responses":
                    await WorkloadB_DriverResponses.RunAsync();
                    break;

                case "c":
                case "workload-c":
                case "location-updates":
                    await WorkloadC_LocationUpdates.RunAsync();
                    break;

                case "d":
                case "workload-d":
                case "geo-search":
                    await WorkloadD_GeoSearchStress.RunAsync();
                    break;

                case "all":
                case "":
                    await RunAllWorkloads();
                    break;

                default:
                    PrintUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"❌ Error: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Stack trace:");
            Console.WriteLine(ex.StackTrace);
        }

        // Don't wait for key press if running non-interactively
        if (Environment.UserInteractive)
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            try { Console.ReadKey(); } catch { /* Ignore if console not available */ }
        }
    }

    static async Task RunAllWorkloads()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  RUNNING ALL WORKLOADS");
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine();

        var results = new List<(string name, bool success)>();

        // Workload A
        Console.WriteLine("Starting Workload A in 5 seconds...");
        await Task.Delay(5000);
        try
        {
            await WorkloadA_TripCreation.RunAsync();
            results.Add(("Workload A: Trip Creation", true));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Workload A failed: {ex.Message}");
            results.Add(("Workload A: Trip Creation", false));
        }

        Console.WriteLine();
        Console.WriteLine("Waiting 10 seconds before next workload...");
        await Task.Delay(10000);

        // Workload B
        Console.WriteLine("Starting Workload B in 5 seconds...");
        await Task.Delay(5000);
        try
        {
            await WorkloadB_DriverResponses.RunAsync();
            results.Add(("Workload B: Driver Responses", true));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Workload B failed: {ex.Message}");
            results.Add(("Workload B: Driver Responses", false));
        }

        Console.WriteLine();
        Console.WriteLine("Waiting 10 seconds before next workload...");
        await Task.Delay(10000);

        // Workload C
        Console.WriteLine("Starting Workload C in 5 seconds...");
        await Task.Delay(5000);
        try
        {
            await WorkloadC_LocationUpdates.RunAsync();
            results.Add(("Workload C: Location Updates", true));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Workload C failed: {ex.Message}");
            results.Add(("Workload C: Location Updates", false));
        }

        Console.WriteLine();
        Console.WriteLine("Waiting 10 seconds before next workload...");
        await Task.Delay(10000);

        // Workload D
        Console.WriteLine("Starting Workload D in 5 seconds...");
        await Task.Delay(5000);
        try
        {
            await WorkloadD_GeoSearchStress.RunAsync();
            results.Add(("Workload D: GEO Search Stress", true));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Workload D failed: {ex.Message}");
            results.Add(("Workload D: GEO Search Stress", false));
        }

        // Summary
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   ALL WORKLOADS COMPLETED                                ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        foreach (var (name, success) in results)
        {
            var status = success ? "✓ PASSED" : "✗ FAILED";
            Console.WriteLine($"  {status}: {name}");
        }

        Console.WriteLine();
        var allPassed = results.All(r => r.success);
        if (allPassed)
        {
            Console.WriteLine("🎉 All workloads completed successfully!");
        }
        else
        {
            Console.WriteLine("⚠️  Some workloads failed. Check the logs above.");
        }
    }

    static async Task<bool> CheckServicesHealthy()
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var services = new Dictionary<string, string>
        {
            { "API Gateway", TestConfig.ApiGatewayUrl },
            { "TripService", TestConfig.TripServiceUrl },
            { "DriverService", TestConfig.DriverServiceUrl }
        };

        bool allHealthy = true;

        foreach (var (name, url) in services)
        {
            try
            {
                // Try to connect to health endpoint or root
                var response = await httpClient.GetAsync($"{url}/health");
                if (!response.IsSuccessStatusCode)
                {
                    // Try root endpoint if health check not available
                    response = await httpClient.GetAsync(url);
                }

                Console.WriteLine($"  ✓ {name} ({url})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ {name} ({url}) - {ex.Message}");
                allHealthy = false;
            }
        }

        // Check Redis
        try
        {
            using var redis = new RedisHelper();
            await redis.GetOnlineDriverCount();
            Console.WriteLine($"  ✓ Redis ({TestConfig.RedisConnectionString})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Redis ({TestConfig.RedisConnectionString}) - {ex.Message}");
            allHealthy = false;
        }

        return allHealthy;
    }

    static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run [workload]");
        Console.WriteLine();
        Console.WriteLine("Workloads:");
        Console.WriteLine("  a, workload-a, trip-creation     - Run Workload A (Hardcore Trip E2E Matching Pipeline)");
        Console.WriteLine("  b, workload-b, driver-responses  - Run Workload B (Driver Accept/Decline Bursts)");
        Console.WriteLine("  c, workload-c, location-updates  - Run Workload C (Hardcore Location Updates - 10K drivers)");
        Console.WriteLine("  d, workload-d, geo-search        - Run Workload D (Hardcore GEO Search Stress - 8K searches/s)");
        Console.WriteLine("  all                              - Run all workloads sequentially");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run                       # Run all workloads");
        Console.WriteLine("  dotnet run a                     # Run only Workload A");
        Console.WriteLine("  dotnet run workload-b            # Run only Workload B");
        Console.WriteLine();
        Console.WriteLine("Environment Variables (optional):");
        Console.WriteLine("  API_GATEWAY_URL                  # Default: http://localhost:8080");
        Console.WriteLine("  WORKLOAD_A_USERS                 # Default: 100");
        Console.WriteLine("  WORKLOAD_A_DURATION              # Default: 60 seconds");
        Console.WriteLine("  WORKLOAD_B_DRIVERS               # Default: 50");
        Console.WriteLine("  WORKLOAD_C_DRIVERS               # Default: 200");
    }
}
