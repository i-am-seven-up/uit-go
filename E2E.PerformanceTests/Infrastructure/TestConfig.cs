namespace E2E.PerformanceTests.Infrastructure;

public static class TestConfig
{
    // API Gateway URL (default for local docker-compose)
    // Use 127.0.0.1 instead of localhost to force IPv4 (avoid IPv6 connection issues)
    public static string ApiGatewayUrl => Environment.GetEnvironmentVariable("API_GATEWAY_URL") ?? "http://127.0.0.1:8080";

    // Service URLs (direct access for debugging)
    public static string TripServiceUrl => Environment.GetEnvironmentVariable("TRIP_SERVICE_URL") ?? "http://127.0.0.1:5002";
    public static string DriverServiceUrl => Environment.GetEnvironmentVariable("DRIVER_SERVICE_URL") ?? "http://127.0.0.1:5003";
    public static string UserServiceUrl => Environment.GetEnvironmentVariable("USER_SERVICE_URL") ?? "http://127.0.0.1:5001";

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
        public static int ConcurrentUsers => GetEnvInt("WORKLOAD_A_USERS", 100);
        public static int DurationSeconds => GetEnvInt("WORKLOAD_A_DURATION", 60);
        public static int RampUpSeconds => GetEnvInt("WORKLOAD_A_RAMPUP", 10);
        public static int DriversToSeed => GetEnvInt("WORKLOAD_A_DRIVERS", 500);
    }

    public static class WorkloadB
    {
        public static int ConcurrentDrivers => GetEnvInt("WORKLOAD_B_DRIVERS", 50);
        public static int DurationSeconds => GetEnvInt("WORKLOAD_B_DURATION", 30);
        public static double AcceptRate => GetEnvDouble("WORKLOAD_B_ACCEPT_RATE", 0.7); // 70% accept
    }

    public static class WorkloadC
    {
        public static int ConcurrentDrivers => GetEnvInt("WORKLOAD_C_DRIVERS", 200);
        public static int UpdateIntervalSeconds => GetEnvInt("WORKLOAD_C_INTERVAL", 5);
        public static int DurationSeconds => GetEnvInt("WORKLOAD_C_DURATION", 60);
        public static int ConcurrentTrips => GetEnvInt("WORKLOAD_C_TRIPS", 20);
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
