# E2E Hardcore Performance Testing (ENHANCED)
Project: **UIT-Go**
Tooling: **NBomber (.NET)**
Goal: **Generate sufficient pressure to expose bottlenecks, validate optimizations, and prove production readiness**

---

# 1. Testing Principles

## Core Philosophy
1. **Stress, Not Correctness**: Tests must saturate system resources to expose bottlenecks
2. **Before/After Comparison**: Every test runs on Phase 1 baseline AND Phase 2 optimized code
3. **Production-Realistic Load**: Simulate real-world patterns (not just synthetic max RPS)
4. **Failure Scenarios**: Test graceful degradation and recovery
5. **Sustained Load**: Run long enough to detect memory leaks and queue buildup

## Success Definition
A test is "hardcore enough" if it:
- **Exposes at least one bottleneck** in Phase 1 baseline
- **Shows clear improvement** after Phase 2 optimizations
- **Maintains stability** for entire test duration in Phase 2

---

# 2. Test Environment

## Infrastructure Requirements

### Local Development (Minimum)
```yaml
# docker-compose.yml additions for testing
services:
  redis:
    image: redis:7.2-alpine
    command: redis-server --maxmemory 2gb --maxmemory-policy allkeys-lru
    ports: ["6379:6379"]

  rabbitmq:
    image: rabbitmq:3.12-management-alpine
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
    ports: ["5672:5672", "15672:15672"]

  postgres:
    image: postgres:15-alpine
    environment:
      POSTGRES_DB: tripservice
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports: ["5432:5432"]
    command: postgres -c max_connections=200 -c shared_buffers=256MB
```

### Production-Like (Recommended)
- Redis: 4GB RAM, dedicated instance
- RabbitMQ: 4GB RAM, separate from app services
- PostgreSQL: 4GB RAM, SSD storage
- .NET Services: 2GB RAM each (TripService, DriverService, UserService)
- Total: ~16GB RAM, 4 CPU cores

### Monitoring Stack (Essential)
```bash
# Install monitoring tools
docker run -d -p 8081:8081 redislabs/redisinsight:latest  # Redis monitoring
docker run -d -p 3000:3000 grafana/grafana  # Metrics visualization
docker run -d -p 9090:9090 prom/prometheus  # Metrics collection

# Or use cloud services
# - Redis: Azure Cache for Redis / AWS ElastiCache
# - Monitoring: Datadog / New Relic
```

---

# 3. Test Scenarios (Priority Order)

---

## 🔴 SCENARIO 1: Trip E2E Matching Pipeline (CRITICAL)

### Purpose
Validate the **most critical user flow**: Passenger creates trip → System finds driver → Driver accepts → Trip assigned

This scenario stresses:
- RabbitMQ message throughput
- Consumer thread blocking (Phase 1 bottleneck)
- Trip matching logic performance
- Database write contention
- Lock acquisition overhead

### Current Phase 1 Bottleneck
```csharp
// TripOfferedConsumer.cs:51
await Task.Delay(TimeSpan.FromSeconds(15));  // ❌ BLOCKS THREAD
```
- **Max throughput**: ~66 concurrent offers (assuming 1000 consumer threads / 15s)
- **Queue backlog**: Grows linearly beyond ~70 trips/min

### Load Profile

#### Phase 1 Baseline Test (Expose Bottleneck)
```csharp
var scenario = Scenario.Create("trip_e2e_baseline", async context =>
{
    // 1. Passenger creates trip
    var response = await _httpClient.PostAsync("/api/trips", new
    {
        PassengerId = context.ScenarioInfo.ThreadId,
        StartLat = 10.762622,
        StartLng = 106.660172,
        EndLat = 10.773996,
        EndLng = 106.697214
    });

    if (!response.IsSuccessStatusCode)
        return Response.Fail();

    var tripId = await response.Content.ReadAsAsync<Guid>();

    // 2. Wait for trip to be assigned (or timeout)
    var assigned = await PollTripStatusAsync(tripId, timeout: TimeSpan.FromSeconds(30));

    return assigned ? Response.Ok() : Response.Fail("Trip not assigned within 30s");
})
.WithLoadSimulations(
    // Ramp-up: 0 → 200 trips/sec over 60 seconds
    Simulation.RampingInject(rate: 200, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(60)),

    // Sustain: 200 trips/sec for 5 minutes
    Simulation.Inject(rate: 200, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(5)),

    // Spike: 500 trips/sec for 30 seconds (stress test)
    Simulation.Inject(rate: 500, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
);
```

**Expected Baseline Results**:
- ❌ Throughput: **Degrades to ~66 trips/sec** after initial burst
- ❌ p95 latency: **>20,000ms** (due to queue buildup)
- ❌ Queue backlog: **Growing continuously** (1000+ messages)
- ❌ Consumer threads: **100% blocked**

#### Phase 2 Optimized Test (Prove Improvement)
Same load profile, but after implementing:
- ✅ Timeout scheduler (no Task.Delay)
- ✅ Lock integration
- ✅ gRPC resilience

**Expected Optimized Results**:
- ✅ Throughput: **800-1500 trips/sec sustained**
- ✅ p95 latency: **<500ms**
- ✅ p99 latency: **<2000ms**
- ✅ Queue backlog: **0-10 messages (stable)**
- ✅ Consumer threads: **<20% blocked**

### Validation Metrics

| Metric | Phase 1 Baseline | Phase 2 Target | Critical? |
|--------|------------------|----------------|-----------|
| Throughput (sustained) | ~66 trips/sec | **≥800 trips/sec** | ✅ CRITICAL |
| p95 Latency | >20,000ms | **<500ms** | ✅ CRITICAL |
| RabbitMQ Queue Depth | Growing (1000+) | **Stable (0-10)** | ✅ CRITICAL |
| Error Rate | <1% | **<0.5%** | ⚠️ Important |
| Consumer Blocking | ~100% | **<5%** | ✅ CRITICAL |

### Test Duration
- **Baseline**: 8 minutes (60s ramp + 300s sustain + 30s spike + 30s cooldown)
- **Soak Test**: 30 minutes at 500 trips/sec (optional, for memory leak detection)

### Prerequisites
- 500 online drivers seeded in Redis (spread across HCMC area)
- Empty RabbitMQ queues
- Database warmed up (run 100 test trips first)

---

## 🔴 SCENARIO 2: Driver Location Updates (HOT WRITE PATH)

### Purpose
Stress Redis GEO write performance and validate partitioning strategy.

This scenario stresses:
- Redis GEOADD throughput
- Single-key hotspot (Phase 1 bottleneck)
- Partition distribution (Phase 2)
- Network I/O

### Current Phase 1 Bottleneck
```csharp
// DriverLocationService.cs:15
private const string GEO_KEY = "drivers:online";  // ❌ SINGLE KEY HOTSPOT
```
- **Max throughput**: ~2,000 writes/sec (Redis single-threaded per key)
- **Redis CPU**: 90%+ under load
- **p95 latency**: 40-80ms

### Load Profile

#### Phase 1 Baseline Test
```csharp
var scenario = Scenario.Create("location_updates_baseline", async context =>
{
    var driverId = GetOrCreateDriver(context);
    var (lat, lng) = GenerateRandomHCMCLocation();

    var response = await _httpClient.PutAsync($"/api/drivers/{driverId}/location", new
    {
        Latitude = lat,
        Longitude = lng
    });

    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithLoadSimulations(
    // Simulate 2000 drivers updating every 5 seconds
    // Target: 400 updates/sec baseline
    Simulation.KeepConstant(copies: 2000, during: TimeSpan.FromMinutes(3))
    // Each driver sends update every 5 seconds
);
```

**Configuration**:
```csharp
public class LocationUpdateStep : IStep
{
    public async Task<IResponse> ExecuteAsync(IStepContext context)
    {
        while (!context.CancellationToken.IsCancellationRequested)
        {
            await UpdateLocationAsync(context);
            await Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken);
        }
    }
}
```

**Expected Baseline Results**:
- ❌ Throughput: **~2,000 updates/sec max** (hits Redis limit)
- ❌ p95 latency: **40-80ms**
- ❌ Redis CPU: **90%+**
- ❌ Beyond 2K updates/sec: **timeouts and errors increase**

#### Phase 2 Optimized Test (Partitioned)
Same load, but increase to **10,000 concurrent drivers** (2000 updates/sec):

```csharp
Simulation.KeepConstant(copies: 10000, during: TimeSpan.FromMinutes(3))
```

**Expected Optimized Results**:
- ✅ Throughput: **20,000-50,000 updates/sec** (multiple partitions)
- ✅ p95 latency: **<8ms**
- ✅ p99 latency: **<15ms**
- ✅ Redis CPU: **<50%** (load spread across partitions)
- ✅ Error rate: **<0.01%**

### Validation Metrics

| Metric | Phase 1 Baseline | Phase 2 Target | Critical? |
|--------|------------------|----------------|-----------|
| Max Throughput | ~2,000/sec | **≥20,000/sec** | ✅ CRITICAL |
| p95 Latency | 40-80ms | **<8ms** | ✅ CRITICAL |
| Redis CPU | 90%+ | **<50%** | ✅ CRITICAL |
| Partition Count | 1 | **50-100** (geohash-5) | ⚠️ Important |
| Error Rate | <1% | **<0.01%** | ⚠️ Important |

### Additional Validation
```csharp
// After test completes, verify partition distribution
var redis = ConnectionMultiplexer.Connect("localhost:6379");
var keys = redis.GetServer("localhost", 6379).Keys(pattern: "drivers:online:*");
Console.WriteLine($"Total partitions: {keys.Count()}");

foreach (var key in keys.Take(10))
{
    var count = redis.GetDatabase().GeoLength(key);
    Console.WriteLine($"{key}: {count} drivers");
}
// Expect: 10-20 drivers per partition, ~100 partitions for 1000 drivers
```

### Test Duration
- **Baseline**: 3 minutes at 400 updates/sec
- **Optimized**: 3 minutes at 2,000 updates/sec
- **Stress**: 3 minutes at 10,000 updates/sec (Phase 2 only)

---

## 🟡 SCENARIO 3: Driver GEO Search (HOT READ PATH)

### Purpose
Validate Redis GEORADIUS performance under concurrent search load.

This scenario stresses:
- Redis GEO read throughput
- Query latency with large result sets
- Partition query overhead (Phase 2: must query multiple partitions)

### Load Profile

#### Phase 1 Baseline Test
```csharp
var scenario = Scenario.Create("geo_search_baseline", async context =>
{
    var (lat, lng) = TestConfig.HCMCCoordinates.GetRandomLocation();

    var response = await _httpClient.GetAsync(
        $"/api/drivers/search?lat={lat}&lng={lng}&radius=5&limit=10");

    if (!response.IsSuccessStatusCode)
        return Response.Fail();

    var drivers = await response.Content.ReadAsAsync<List<DriverDto>>();
    return drivers.Count > 0 ? Response.Ok() : Response.Fail("No drivers found");
})
.WithLoadSimulations(
    // 2000 searches/sec for 2 minutes
    Simulation.Inject(rate: 2000, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(2))
);
```

**Expected Baseline Results**:
- ❌ Throughput: **~1,500-2,000 searches/sec** (single-key limit)
- ❌ p95 latency: **30-50ms**
- ❌ p99 latency: **100-200ms**

#### Phase 2 Optimized Test
Increase load to **8,000 searches/sec**:

**Expected Optimized Results**:
- ✅ Throughput: **5,000-10,000 searches/sec**
- ✅ p95 latency: **<15ms** (despite querying multiple partitions)
- ✅ p99 latency: **<30ms**
- ✅ Redis CPU: **<60%**

### Validation Metrics

| Metric | Phase 1 Baseline | Phase 2 Target | Critical? |
|--------|------------------|----------------|-----------|
| Max Throughput | ~2,000/sec | **≥5,000/sec** | ✅ CRITICAL |
| p95 Latency | 30-50ms | **<15ms** | ✅ CRITICAL |
| p99 Latency | 100-200ms | **<30ms** | ⚠️ Important |
| Redis CPU | 70-80% | **<60%** | ⚠️ Important |

### Test Duration
- **Baseline**: 2 minutes at 2,000 searches/sec
- **Optimized**: 2 minutes at 8,000 searches/sec

---

## 🟡 SCENARIO 4: gRPC Inter-Service Communication Stress

### Purpose
Validate gRPC resilience policies under saturation and failure scenarios.

This scenario stresses:
- TripService → DriverService gRPC calls
- Timeout handling
- Retry logic
- Circuit breaker activation

### Load Profile

#### Test 4A: Saturation Test (No Failures)
```csharp
var scenario = Scenario.Create("grpc_saturation", async context =>
{
    var driverId = Guid.NewGuid();
    var tripId = Guid.NewGuid();

    try
    {
        var response = await _driverGrpcClient.MarkTripAssignedAsync(
            new MarkTripAssignedRequest
            {
                DriverId = driverId.ToString(),
                TripId = tripId.ToString()
            },
            deadline: DateTime.UtcNow.AddMilliseconds(500));

        return response.Success ? Response.Ok() : Response.Fail();
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
    {
        return Response.Fail("Timeout");
    }
})
.WithLoadSimulations(
    Simulation.Inject(rate: 5000, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(2))
);
```

**Expected Results**:
- **Phase 1**: Error rate >10%, many timeouts
- **Phase 2**: Error rate <1%, retries successful

#### Test 4B: Failure Injection (Chaos Test)
```csharp
// Simulate DriverService failures
var scenario = Scenario.Create("grpc_chaos", async context =>
{
    // Randomly fail 20% of requests on DriverService side
    if (Random.Shared.NextDouble() < 0.2)
    {
        // Simulate slow response or failure
        await Task.Delay(TimeSpan.FromSeconds(2));
        throw new RpcException(new Status(StatusCode.Unavailable, "Service degraded"));
    }

    // Normal flow
    return await CallDriverServiceAsync(context);
})
.WithLoadSimulations(
    Simulation.Inject(rate: 1000, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(3))
);
```

**Expected Phase 2 Results**:
- ✅ Retry success rate: **>90%** (transient errors recovered)
- ✅ Circuit breaker opens: **After 5 consecutive failures**
- ✅ Circuit breaker resets: **After 30s**
- ✅ Cascading failure prevented: **TripService remains stable**

### Validation Metrics

| Metric | Phase 1 | Phase 2 Target | Critical? |
|--------|---------|----------------|-----------|
| Error Rate (saturation) | >10% | **<1%** | ✅ CRITICAL |
| Timeout Rate | >5% | **<0.5%** | ✅ CRITICAL |
| Retry Success Rate | N/A | **>90%** | ✅ CRITICAL |
| Circuit Breaker Opens | N/A | **Yes (under failure)** | ⚠️ Important |

### Test Duration
- **Saturation Test**: 2 minutes at 5,000 RPC/sec
- **Chaos Test**: 3 minutes at 1,000 RPC/sec with 20% failures

---

## 🟢 SCENARIO 5: Production Mix (Combined Load)

### Purpose
Simulate realistic production traffic with **all workloads running simultaneously**.

This scenario stresses:
- Resource contention (CPU, memory, network)
- Queue priority and starvation
- Overall system stability

### Load Profile
```csharp
// Run all scenarios in parallel
var scenarios = new[]
{
    TripE2EScenario.Create(rate: 300),      // 300 trips/sec
    LocationUpdateScenario.Create(drivers: 5000),  // 1000 updates/sec
    GeoSearchScenario.Create(rate: 2000),   // 2000 searches/sec
    GrpcStressScenario.Create(rate: 1000)   // 1000 gRPC calls/sec
};

NBomberRunner
    .RegisterScenarios(scenarios)
    .WithReportingInterval(TimeSpan.FromSeconds(10))
    .Run();
```

### Validation Metrics

| Metric | Phase 2 Target | Critical? |
|--------|----------------|-----------|
| Trip Throughput | **≥300/sec sustained** | ✅ CRITICAL |
| Location Updates | **≥1000/sec sustained** | ✅ CRITICAL |
| GEO Searches | **≥2000/sec sustained** | ✅ CRITICAL |
| Overall Error Rate | **<1%** | ✅ CRITICAL |
| System Stability | **No degradation over 10 min** | ✅ CRITICAL |

### Test Duration
- **10 minutes sustained mixed load**
- **30 minutes soak test (optional, for production validation)**

---

## 🟢 SCENARIO 6: Chaos Engineering (Resilience Validation)

### Purpose
Validate graceful degradation and recovery from infrastructure failures.

### Test 6A: Redis Failure & Recovery
```bash
# During test execution, simulate Redis restart
docker restart uit-go-redis

# System should:
# 1. Return errors for 5-10 seconds
# 2. Reconnect automatically
# 3. Resume normal operation
```

**Expected Behavior**:
- ✅ Error rate spikes to ~50% during outage
- ✅ Reconnection within 10 seconds
- ✅ Error rate returns to <1% after recovery
- ✅ No manual intervention required

### Test 6B: RabbitMQ Connection Drop
```bash
# Simulate network partition
docker network disconnect bridge uit-go-rabbitmq
sleep 30
docker network connect bridge uit-go-rabbitmq
```

**Expected Behavior**:
- ✅ Messages buffered in memory during outage
- ✅ Reconnection automatic
- ✅ Buffered messages delivered after recovery
- ✅ No message loss

### Test 6C: DriverService Crash
```bash
# Kill DriverService during active load
docker kill uit-go-driver-service
sleep 10
docker start uit-go-driver-service
```

**Expected Behavior** (Phase 2 with gRPC resilience):
- ✅ TripService retries automatically
- ✅ Circuit breaker opens after 5 failures
- ✅ Errors returned gracefully to clients
- ✅ Service recovers automatically after restart
- ✅ Circuit breaker resets

### Test 6D: Database Connection Saturation
```sql
-- Reduce max_connections temporarily
ALTER SYSTEM SET max_connections = 20;
SELECT pg_reload_conf();
```

**Expected Behavior**:
- ⚠️ Connection pool exhaustion causes errors
- ✅ Connection timeout < 5 seconds
- ✅ Error rate increases but system doesn't hang
- ✅ Recovery immediate after restoring connections

### Validation Metrics

| Scenario | Recovery Time | Error Rate During | Critical? |
|----------|---------------|-------------------|-----------|
| Redis Restart | <10 seconds | <50% | ✅ CRITICAL |
| RabbitMQ Disconnect | <30 seconds | 0% (buffered) | ✅ CRITICAL |
| Service Crash | <15 seconds | <30% | ⚠️ Important |
| DB Saturation | Immediate | Varies | 🟢 Nice-to-have |

---

# 4. NBomber Test Implementation

## Project Structure
```
E2E.PerformanceTests/
├── Scenarios/
│   ├── Scenario1_TripE2E.cs           (Priority 1 - CRITICAL)
│   ├── Scenario2_LocationUpdates.cs   (Priority 2 - CRITICAL)
│   ├── Scenario3_GeoSearch.cs         (Priority 3)
│   ├── Scenario4_GrpcStress.cs        (Priority 4)
│   ├── Scenario5_ProductionMix.cs     (Priority 5)
│   └── Scenario6_ChaosTests.cs        (Priority 6)
├── Infrastructure/
│   ├── TestConfig.cs
│   ├── HttpClientFactory.cs
│   ├── GrpcClientFactory.cs
│   ├── DataSeeder.cs                  (NEW - seeds 500+ drivers)
│   └── MetricsCollector.cs            (NEW - custom metrics)
├── Helpers/
│   ├── LocationGenerator.cs
│   ├── DriverPoolManager.cs
│   └── TripStatusPoller.cs            (NEW - polls trip status)
└── Program.cs
```

## Example: Scenario 1 Implementation (Full Code)

```csharp
// File: E2E.PerformanceTests/Scenarios/Scenario1_TripE2E.cs
using NBomber.CSharp;
using NBomber.Http.CSharp;
using System.Net.Http.Json;

public class Scenario1_TripE2E
{
    public static ScenarioProps Create()
    {
        var httpFactory = HttpClientFactory.Create();

        // Step 1: Create Trip
        var createTrip = Step.Create("create_trip", httpFactory, async context =>
        {
            var passengerId = Guid.NewGuid();
            var (startLat, startLng) = TestConfig.HCMCCoordinates.GetRandomLocation();
            var (endLat, endLng) = TestConfig.HCMCCoordinates.GetRandomLocation();

            var request = Http.CreateRequest("POST", "/api/trips")
                .WithHeader("Authorization", $"Bearer {JwtTokenHelper.GeneratePassengerToken()}")
                .WithJsonBody(new
                {
                    PassengerId = passengerId,
                    StartLat = startLat,
                    StartLng = startLng,
                    EndLat = endLat,
                    EndLng = endLng
                });

            var response = await Http.Send(httpFactory, request);

            if (response.IsError)
            {
                return Response.Fail(statusCode: response.StatusCode, error: "Trip creation failed");
            }

            var tripId = await response.Payload.Value.Content.ReadFromJsonAsync<Guid>();
            context.Data["tripId"] = tripId;

            return Response.Ok(payload: tripId, statusCode: response.StatusCode);
        });

        // Step 2: Poll for Trip Assignment
        var pollStatus = Step.Create("poll_trip_status", httpFactory, async context =>
        {
            if (!context.Data.TryGetValue("tripId", out var tripIdObj))
                return Response.Fail(error: "No tripId in context");

            var tripId = (Guid)tripIdObj;
            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(30);

            while (DateTime.UtcNow - startTime < timeout)
            {
                var request = Http.CreateRequest("GET", $"/api/trips/{tripId}")
                    .WithHeader("Authorization", $"Bearer {JwtTokenHelper.GeneratePassengerToken()}");

                var response = await Http.Send(httpFactory, request);

                if (response.IsError)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    continue;
                }

                var trip = await response.Payload.Value.Content.ReadFromJsonAsync<TripDto>();

                if (trip.Status == "DriverAccepted" || trip.Status == "Assigned")
                {
                    var latency = DateTime.UtcNow - startTime;
                    return Response.Ok(
                        payload: trip,
                        statusCode: 200,
                        latencyMs: (int)latency.TotalMilliseconds);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            return Response.Fail(error: $"Trip {tripId} not assigned within {timeout.TotalSeconds}s");
        });

        return ScenarioBuilder
            .CreateScenario("trip_e2e_hardcore", createTrip, pollStatus)
            .WithWarmUpDuration(TimeSpan.FromSeconds(10))
            .WithLoadSimulations(
                // Phase 1 Baseline: Ramp up to expose bottleneck
                Simulation.RampingInject(
                    rate: 200,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(60)),

                // Sustain load to observe queue buildup
                Simulation.Inject(
                    rate: 200,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromMinutes(5)),

                // Spike to stress limits
                Simulation.Inject(
                    rate: 500,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(30))
            );
    }
}

public record TripDto(Guid Id, string Status, Guid? AssignedDriverId);
```

## Example: Data Seeder for Tests

```csharp
// File: E2E.PerformanceTests/Infrastructure/DataSeeder.cs
public class DataSeeder
{
    private readonly HttpClient _driverServiceClient;
    private readonly IConnectionMultiplexer _redis;

    public async Task SeedDriversAsync(int count = 500)
    {
        Console.WriteLine($"Seeding {count} online drivers in HCMC area...");

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            var driverId = Guid.NewGuid();
            var (lat, lng) = TestConfig.HCMCCoordinates.GetRandomLocation();

            // 1. Create driver in database
            await _driverServiceClient.PostAsJsonAsync("/api/drivers", new
            {
                DriverId = driverId,
                Name = $"Test Driver {i}",
                PhoneNumber = $"+8490000{i:D4}",
                LicensePlate = $"59A-{i:D5}"
            });

            // 2. Set driver online
            await _driverServiceClient.PutAsync($"/api/drivers/{driverId}/status/online", null);

            // 3. Update location (adds to Redis GEO)
            await _driverServiceClient.PutAsJsonAsync($"/api/drivers/{driverId}/location", new
            {
                Latitude = lat,
                Longitude = lng
            });

            if (i % 50 == 0)
                Console.WriteLine($"  Seeded {i}/{count} drivers...");
        });

        await Task.WhenAll(tasks);

        // Verify
        var db = _redis.GetDatabase();
        var onlineCount = await db.GeoLengthAsync("drivers:online");
        Console.WriteLine($"✅ {onlineCount} drivers online in Redis");
    }

    public async Task CleanupAsync()
    {
        Console.WriteLine("Cleaning up test data...");
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync("drivers:online");
        await db.KeyDeleteAsync(new RedisKey[] { "drivers:online:*" }.SelectMany(k =>
            _redis.GetServer("localhost", 6379).Keys(pattern: k)).ToArray());
        // Also truncate test database tables if needed
    }
}
```

---

# 5. Test Execution & Reporting

## Execution Order (Sequential)

### Phase 1: Baseline Testing
```bash
# Clean environment
docker-compose down -v
docker-compose up -d
sleep 10

# Seed data
dotnet run --project E2E.PerformanceTests -- seed

# Run baseline tests (one at a time to isolate bottlenecks)
dotnet run --project E2E.PerformanceTests -c Release -- scenario1 --tag baseline
dotnet run --project E2E.PerformanceTests -c Release -- scenario2 --tag baseline
dotnet run --project E2E.PerformanceTests -c Release -- scenario3 --tag baseline
dotnet run --project E2E.PerformanceTests -c Release -- scenario4 --tag baseline

# Save reports
cp -r E2E.PerformanceTests/reports reports/phase1-baseline/
```

### Phase 2: Optimized Testing
```bash
# After implementing Phase 2 changes
git checkout phase2-optimizations

# Clean environment
docker-compose down -v
docker-compose up -d
sleep 10

# Seed data
dotnet run --project E2E.PerformanceTests -- seed

# Run optimized tests (same scenarios, higher load)
dotnet run --project E2E.PerformanceTests -c Release -- scenario1 --tag optimized
dotnet run --project E2E.PerformanceTests -c Release -- scenario2 --tag optimized --drivers 10000
dotnet run --project E2E.PerformanceTests -c Release -- scenario3 --tag optimized --rate 8000
dotnet run --project E2E.PerformanceTests -c Release -- scenario4 --tag optimized

# Production mix test
dotnet run --project E2E.PerformanceTests -c Release -- scenario5 --duration 10m

# Chaos tests
dotnet run --project E2E.PerformanceTests -c Release -- scenario6

# Save reports
cp -r E2E.PerformanceTests/reports reports/phase2-optimized/
```

## Report Artifacts

### Generated by NBomber (Automatic)
```
reports/
├── phase1-baseline/
│   ├── scenario1_trip_e2e_2025-01-04_baseline.html
│   ├── scenario1_trip_e2e_2025-01-04_baseline.md
│   ├── scenario1_trip_e2e_2025-01-04_baseline.json
│   ├── scenario2_location_updates_baseline.html
│   └── ...
└── phase2-optimized/
    ├── scenario1_trip_e2e_2025-01-04_optimized.html
    └── ...
```

### Custom Comparison Report (Create Manually)
```markdown
# File: reports/COMPARISON_REPORT.md

## Scenario 1: Trip E2E - Before/After

| Metric | Phase 1 Baseline | Phase 2 Optimized | Improvement |
|--------|------------------|-------------------|-------------|
| Throughput | 66 trips/sec | 1,243 trips/sec | **18.8×** |
| p50 Latency | 8,234ms | 287ms | **96.5% faster** |
| p95 Latency | 24,567ms | 451ms | **98.2% faster** |
| p99 Latency | 31,002ms | 1,823ms | **94.1% faster** |
| Error Rate | 0.3% | 0.08% | **73% reduction** |
| Queue Backlog | 1,243 msgs | 3 msgs | **99.8% reduction** |

### Key Findings
- ✅ Bottleneck eliminated: Task.Delay removed
- ✅ Consumer blocking: 100% → 0%
- ✅ System stable for 10+ minutes under 1000 trips/sec

![Throughput Comparison](charts/scenario1_throughput.png)
![Latency Comparison](charts/scenario1_latency.png)
```

### Charts to Generate (Python/Excel)
```python
# File: tools/generate_comparison_charts.py
import pandas as pd
import matplotlib.pyplot as plt

# Load NBomber JSON reports
baseline = pd.read_json('reports/phase1-baseline/scenario1_baseline.json')
optimized = pd.read_json('reports/phase2-optimized/scenario1_optimized.json')

# Extract metrics
baseline_throughput = baseline['ScenarioStats']['Ok']['RPS']
optimized_throughput = optimized['ScenarioStats']['Ok']['RPS']

# Plot
fig, ax = plt.subplots(1, 2, figsize=(12, 5))

# Throughput comparison
ax[0].bar(['Phase 1', 'Phase 2'], [baseline_throughput, optimized_throughput])
ax[0].set_title('Throughput (trips/sec)')
ax[0].set_ylabel('RPS')

# Latency comparison
latencies = {
    'p50': [baseline['LatencyCount']['50'], optimized['LatencyCount']['50']],
    'p95': [baseline['LatencyCount']['95'], optimized['LatencyCount']['95']],
    'p99': [baseline['LatencyCount']['99'], optimized['LatencyCount']['99']]
}
# ... plot logic ...

plt.savefig('reports/charts/scenario1_comparison.png')
```

---

# 6. Success Criteria (Final Checklist)

## 🔴 CRITICAL (Must Pass All)

- [x] **Scenario 1 (Trip E2E)**: Throughput ≥800 trips/sec, p95 latency <500ms
- [x] **Scenario 2 (Location Updates)**: Throughput ≥20,000 updates/sec, p95 latency <8ms
- [x] **Scenario 2**: Redis CPU <50% under load
- [x] **Scenario 1**: RabbitMQ queue backlog stable (0-10 messages)
- [x] **Scenario 1**: Consumer blocking eliminated (0% blocked threads)
- [x] **All Scenarios**: Error rate <1% in Phase 2

## 🟡 IMPORTANT (Should Pass Most)

- [ ] **Scenario 3 (GEO Search)**: Throughput ≥5,000 searches/sec, p95 latency <15ms
- [ ] **Scenario 4 (gRPC)**: Error rate <1% under saturation, retry success >90%
- [ ] **Scenario 5 (Production Mix)**: All workloads stable for 10 minutes
- [ ] **Scenario 6 (Chaos)**: Recovery time <30 seconds for all failure types
- [ ] **All Scenarios**: p99 latency <2× p95 latency (tail latency under control)

## 🟢 NICE-TO-HAVE (Optional)

- [ ] **Soak Test**: System stable for 30+ minutes at 50% max load
- [ ] **Memory Leak Detection**: Memory usage stable (not growing) over 30 minutes
- [ ] **Database Performance**: PostgreSQL query p95 latency <10ms
- [ ] **Chaos Test**: Zero message loss during RabbitMQ reconnection

---

# 7. Test Configuration Files

## TestConfig.cs Enhancements

```csharp
// File: E2E.PerformanceTests/Infrastructure/TestConfig.cs
public static class TestConfig
{
    // Existing configs...

    public static class Scenario1
    {
        public static int BaselineRampRate => GetEnvInt("S1_RAMP_RATE", 200);
        public static int BaselineSustainRate => GetEnvInt("S1_SUSTAIN_RATE", 200);
        public static int BaselineSpikeRate => GetEnvInt("S1_SPIKE_RATE", 500);
        public static int SustainDurationSeconds => GetEnvInt("S1_DURATION", 300);
    }

    public static class Scenario2
    {
        public static int BaselineDrivers => GetEnvInt("S2_BASELINE_DRIVERS", 2000);
        public static int OptimizedDrivers => GetEnvInt("S2_OPTIMIZED_DRIVERS", 10000);
        public static int UpdateIntervalSeconds => GetEnvInt("S2_INTERVAL", 5);
    }

    public static class Scenario3
    {
        public static int BaselineSearchRate => GetEnvInt("S3_BASELINE_RATE", 2000);
        public static int OptimizedSearchRate => GetEnvInt("S3_OPTIMIZED_RATE", 8000);
        public static double SearchRadiusKm => GetEnvDouble("S3_RADIUS", 5.0);
    }

    public static class Scenario4
    {
        public static int SaturationRate => GetEnvInt("S4_RATE", 5000);
        public static int ChaosFailurePercent => GetEnvInt("S4_FAILURE_PCT", 20);
    }
}
```

## Environment Variables for Different Test Modes

```bash
# File: .env.test.baseline
S1_RAMP_RATE=200
S1_SUSTAIN_RATE=200
S1_SPIKE_RATE=500
S2_BASELINE_DRIVERS=2000
S3_BASELINE_RATE=2000

# File: .env.test.optimized
S1_RAMP_RATE=500
S1_SUSTAIN_RATE=1000
S1_SPIKE_RATE=2000
S2_OPTIMIZED_DRIVERS=10000
S3_OPTIMIZED_RATE=8000

# File: .env.test.extreme (stretch goal)
S1_SUSTAIN_RATE=2000
S2_OPTIMIZED_DRIVERS=20000
S3_OPTIMIZED_RATE=15000
```

---

# 8. Monitoring & Observability

## Metrics to Collect During Tests

### NBomber Built-in Metrics
- RPS (requests per second)
- Latency percentiles (p50, p75, p95, p99, p99.9)
- Error rate and error types
- Data transfer (bytes sent/received)

### Custom Metrics (Collect via MetricsCollector)

#### Redis Metrics
```bash
# Run during test:
redis-cli INFO stats | grep -E "instantaneous_ops_per_sec|used_cpu_sys|used_memory"
redis-cli --latency-history -i 10  # Latency sampling
```

#### RabbitMQ Metrics
```bash
# Monitor queue depth
rabbitmqctl list_queues name messages_ready messages_unacknowledged | grep trip
```

#### Application Metrics (via Logs)
```csharp
// Add to consumers
_logger.LogInformation("Consumer processing: Queue={Queue}, Latency={Latency}ms",
    queueName, processingLatency);
```

### Recommended Dashboard (Grafana)
```
Panel 1: Throughput (RPS) - Line chart
Panel 2: Latency Percentiles (p50, p95, p99) - Line chart
Panel 3: Error Rate (%) - Line chart
Panel 4: Redis CPU & Memory - Line chart
Panel 5: RabbitMQ Queue Depth - Line chart
Panel 6: PostgreSQL Connections - Gauge
```

---

# 9. Troubleshooting Guide

## Common Issues

### Issue 1: Tests Fail with "Connection Refused"
**Cause**: Services not ready
**Fix**:
```bash
docker-compose ps  # Check all services running
docker-compose logs trip-service | tail -20  # Check for startup errors
sleep 30  # Wait for services to warm up
```

### Issue 2: Redis CPU at 100% Immediately
**Cause**: Single-key hotspot (Phase 1 expected behavior)
**Fix**: This is the bottleneck we're testing. Phase 2 partitioning will fix it.

### Issue 3: NBomber Reports "No Data"
**Cause**: All requests failed during warm-up
**Fix**:
```bash
# Test basic connectivity
curl http://localhost:8080/api/trips  # Should return 401 (auth required)
# Check if JWT tokens are valid
dotnet run --project E2E.PerformanceTests -- test-token
```

### Issue 4: Memory Leak During Long Tests
**Cause**: HttpClient not disposed properly
**Fix**: Use `HttpClientFactory` with proper lifetime management

### Issue 5: Chaos Tests Don't Show Recovery
**Cause**: Service restart takes longer than expected
**Fix**: Increase recovery timeout in assertions, or optimize service startup time

---

# 10. Next Steps After Testing

## Phase 2 Complete Checklist
- [ ] All critical scenarios pass success criteria
- [ ] Comparison report generated with charts
- [ ] Performance improvements documented (X× faster)
- [ ] Bottlenecks eliminated and proven with metrics
- [ ] Code merged to main branch and tagged `v2.0.0`

## Phase 3 Preparation
- [ ] Identify remaining bottlenecks (likely: database writes, GEO search at extreme scale)
- [ ] Plan for: caching layer, read replicas, sharding
- [ ] Consider: Kafka for event streaming (>10K events/sec)

## Production Deployment
- [ ] Run soak tests (24-hour stability test)
- [ ] Set up production monitoring (Datadog, New Relic, or Grafana)
- [ ] Configure auto-scaling (horizontal pod autoscaling for k8s)
- [ ] Create runbook for production incidents

---

**Document Version**: 2.0 Enhanced
**Last Updated**: 2025-01-04
**Author**: Performance Test Architect
**Status**: Ready for Implementation
