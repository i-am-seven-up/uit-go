namespace E2E.PerformanceTests.Infrastructure;

public static class TestConfig
{
    // API Gateway URL (Kubernetes ingress on port 80)
    // Use 127.0.0.1 instead of localhost to force IPv4 (avoid IPv6 connection issues)
    public static string ApiGatewayUrl => Environment.GetEnvironmentVariable("API_GATEWAY_URL") ?? "http://127.0.0.1:80";

    // Service URLs (routed through API Gateway - production-like E2E tests)
    public static string TripServiceUrl => Environment.GetEnvironmentVariable("TRIP_SERVICE_URL") ?? "http://127.0.0.1:80";
    public static string DriverServiceUrl => Environment.GetEnvironmentVariable("DRIVER_SERVICE_URL") ?? "http://127.0.0.1:80";
    public static string UserServiceUrl => Environment.GetEnvironmentVariable("USER_SERVICE_URL") ?? "http://127.0.0.1:80";

    // Redis
    public static string RedisConnectionString => Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "127.0.0.1:6379,allowAdmin=true";

    // PostgreSQL (for verification)
    public static string PostgresConnectionString => Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
        ?? "Host=127.0.0.1;Port=5432;Database=tripservice;Username=postgres;Password=postgres";

    // RabbitMQ (for monitoring)
    public static string RabbitMqConnectionString => Environment.GetEnvironmentVariable("RABBITMQ_CONNECTION")
        ?? "amqp://guest:guest@127.0.0.1:5672";

    // Test Parameters
    public static class WorkloadA
    {
        // HARDCORE TEST - Scenario 1: Trip E2E Matching Pipeline
        // Ramp: 0→200 trips/sec over 60s
        // Sustain: 200 trips/sec for 5 minutes
        // Spike: 500 trips/sec for 30s
        public static int RampRate => GetEnvInt("WORKLOAD_A_RAMP_RATE", 200);
        public static int RampDurationSeconds => GetEnvInt("WORKLOAD_A_RAMP_DURATION", 60);
        public static int SustainRate => GetEnvInt("WORKLOAD_A_SUSTAIN_RATE", 200);
        public static int SustainDurationSeconds => GetEnvInt("WORKLOAD_A_SUSTAIN_DURATION", 300); // 5 minutes
        public static int SpikeRate => GetEnvInt("WORKLOAD_A_SPIKE_RATE", 500);
        public static int SpikeDurationSeconds => GetEnvInt("WORKLOAD_A_SPIKE_DURATION", 30);
        public static int DriversToSeed => GetEnvInt("WORKLOAD_A_DRIVERS", 1000);
    }

    public static class WorkloadB
    {
        public static int ConcurrentDrivers => GetEnvInt("WORKLOAD_B_DRIVERS", 50);
        public static int DurationSeconds => GetEnvInt("WORKLOAD_B_DURATION", 30);
        public static double AcceptRate => GetEnvDouble("WORKLOAD_B_ACCEPT_RATE", 0.7); // 70% accept
    }

    public static class WorkloadC
    {
        // HARDCORE TEST - Scenario 2: Driver Location Updates (Hot Write Path)
        // Baseline: 2000 drivers × 5s interval = 400 updates/sec
        // Optimized: 10,000 drivers × 5s interval = 2,000 updates/sec
        // Extreme: 20,000 drivers × 5s interval = 4,000 updates/sec
        public static int BaselineDrivers => GetEnvInt("WORKLOAD_C_BASELINE_DRIVERS", 2000);
        public static int OptimizedDrivers => GetEnvInt("WORKLOAD_C_OPTIMIZED_DRIVERS", 10000);
        public static int ExtremeDrivers => GetEnvInt("WORKLOAD_C_EXTREME_DRIVERS", 20000);
        public static int UpdateIntervalSeconds => GetEnvInt("WORKLOAD_C_INTERVAL", 5);
        public static int DurationSeconds => GetEnvInt("WORKLOAD_C_DURATION", 180); // 3 minutes
        public static int ConcurrentTrips => GetEnvInt("WORKLOAD_C_TRIPS", 20);
    }

    public static class WorkloadD
    {
        // HARDCORE TEST - Scenario 3: GEO Search Stress (Hot Read Path)
        // Baseline: 2,000 searches/sec for 2 minutes
        // Optimized: 5,000 searches/sec for 2 minutes (reduced from 8K for realistic production load)
        // Extreme: 10,000 searches/sec for 2 minutes (reduced from 15K)
        public static int BaselineSearchRate => GetEnvInt("WORKLOAD_D_BASELINE_RATE", 2000);
        public static int OptimizedSearchRate => GetEnvInt("WORKLOAD_D_OPTIMIZED_RATE", 5000);
        public static int ExtremeSearchRate => GetEnvInt("WORKLOAD_D_EXTREME_RATE", 10000);
        public static int DurationSeconds => GetEnvInt("WORKLOAD_D_DURATION", 120); // 2 minutes
        public static int DriversToSeed => GetEnvInt("WORKLOAD_D_DRIVERS", 5000);
        public static double SearchRadiusKm => GetEnvDouble("WORKLOAD_D_RADIUS", 5.0);
    }


    // HCMC Coordinates (for realistic location data)
    public static class HCMCCoordinates
    {
        public static double MinLat => 10.700;
        public static double MaxLat => 10.850;
        public static double MinLng => 106.600;
        public static double MaxLng => 106.750;

        public static (double lat, double lng) GetRandomLocation()
        {
            var random = new Random();
            var lat = MinLat + (random.NextDouble() * (MaxLat - MinLat));
            var lng = MinLng + (random.NextDouble() * (MaxLng - MinLng));
            return (lat, lng);
        }

        // Predefined locations for consistency
        public static (double lat, double lng) District1 => (10.762622, 106.660172);
        public static (double lat, double lng) District3 => (10.773996, 106.697214);
        public static (double lat, double lng) BinhThanh => (10.801196, 106.717972);
        public static (double lat, double lng) TanBinh => (10.799442, 106.654222);
    }

    private static int GetEnvInt(string key, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    private static double GetEnvDouble(string key, double defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return double.TryParse(value, out var result) ? result : defaultValue;
    }
}
