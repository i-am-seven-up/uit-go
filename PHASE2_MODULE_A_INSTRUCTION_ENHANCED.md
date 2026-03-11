# Phase 2 — Module A: Scalability & Performance (ENHANCED)
Project: **UIT-Go**
Architecture: **.NET microservices + gRPC + RabbitMQ + Redis GEO + Postgres**
Role: **System Architect**

---

# 1. Phase 2 Goal

Scale UIT-Go from Phase 1 baseline to production-ready architecture capable of handling:
- **Driver location updates: 20,000-50,000 writes/sec** (current: ~40/sec)
- **Driver GEO search: 5,000-10,000 reads/sec** (current: unmeasured)
- **Trip matching pipeline: 800-1,500 trips/sec** (current: ~1-2/sec)
- **p95 latency < 20ms for GEO operations** (current: unmeasured)
- **Zero consumer thread blocking** (current: 15s blocks on every offer)

**Success Metric**: Clear before/after performance proof with NBomber load tests showing **3-5× improvement** in throughput and **40-70% reduction** in p95 latency.

---

# 2. Current Architecture Baseline (Phase 1 - VERIFIED)

## 2.1 Current Trip Matching Flow
```
POST /trips
  → TripService creates Trip entity
  → Publishes TripCreated event
  → (Currently: No TripCreatedConsumer - trip matching not implemented yet)
  → Manual or external trigger for driver matching

When Trip Offer occurs:
  → TripOfferedConsumer receives TripOffered event
  → ❌ BLOCKS for 15 seconds: await Task.Delay(TimeSpan.FromSeconds(message.TtlSeconds))
  → Checks if driver declined
  → Auto-assigns if no response
  → Re-publishes TripOffered for retry if declined
```

**File**: `TripService/TripService.Api/Messaging/TripOfferedConsumer.cs:51`

## 2.2 Current Redis GEO Implementation
```csharp
private const string GEO_KEY = "drivers:online";
// ALL drivers stored under single key
// File: DriverService/DriverService.Application/Services/DriverLocationService.cs:15
```

**Problem**: Single Redis key becomes hotspot at >1,000 ops/sec

## 2.3 Current Lock Implementation
```csharp
// Locks EXIST but are NOT USED in critical path
public async Task<bool> TryLockTripAsync(Guid tripId, TimeSpan ttl)
public async Task<bool> TryLockDriverAsync(Guid driverId, Guid tripId, TimeSpan ttl)
// File: TripService/TripService.Application/Services/TripMatchService.cs:84-104
```

**Problem**: Locks implemented but not integrated into TripOfferedConsumer or matching logic

## 2.4 Current gRPC Calls (No Resilience)
```csharp
var resp = await _driverGrpc.MarkTripAssignedAsync(
    new MarkTripAssignedRequest { ... },
    cancellationToken: ct);
// No timeout, no retry, no circuit breaker
// File: TripService/TripService.Api/Messaging/TripOfferedConsumer.cs:78
```

## 2.5 Current Test Load (E2E.PerformanceTests)
- **WorkloadA**: 100 concurrent users, 60s duration
- **WorkloadB**: 50 concurrent drivers, 30s duration
- **WorkloadC**: 200 drivers × 5s interval = **40 updates/sec**

**Problem**: Load too low to expose bottlenecks

---

# 3. Bottleneck Analysis (Priority Order)

## 🔴 CRITICAL PRIORITY 1: Consumer Thread Blocking
**Impact**: Limits entire system throughput to ~66 concurrent offers (1000ms / 15s per thread)

**Current Code**:
```csharp
// TripOfferedConsumer.cs:51
await Task.Delay(TimeSpan.FromSeconds(message.TtlSeconds), ct);
```

**Effect**:
- Blocks RabbitMQ consumer thread for 15 seconds
- Throughput collapses under load
- Queue backlog grows linearly with trip volume

**Critical Path**: Directly blocks trip assignment pipeline

---

## 🔴 CRITICAL PRIORITY 2: Redis GEO Hotspot
**Impact**: Limits GEO search throughput to ~1,500-2,000 ops/sec

**Current Code**:
```csharp
// DriverLocationService.cs:15
private const string GEO_KEY = "drivers:online";
// All GEOADD/GEORADIUS operations hit same key
```

**Effect**:
- Single-threaded Redis command execution on hot key
- CPU contention at ~2K ops/sec
- Increased p95/p99 latency

**Critical Path**: Every driver location update + every driver search

---

## 🟡 HIGH PRIORITY 3: Missing Trip-Level Locks
**Impact**: Race condition allows double-assignment under concurrent load

**Current State**:
- Lock methods exist but unused: `TripMatchService.TryLockTripAsync()` (line 84)
- `TripOfferedConsumer` doesn't acquire locks before assignment (line 78-89)

**Effect**:
- Multiple drivers can be assigned to same trip under race conditions
- Requires manual reconciliation
- Poor user experience

**Critical Path**: Trip assignment logic

---

## 🟡 HIGH PRIORITY 4: No gRPC Resilience
**Impact**: Cascading failures under service pressure

**Current Code**:
```csharp
// TripOfferedConsumer.cs:78 - No CallOptions configured
var resp = await _driverGrpc.MarkTripAssignedAsync(
    new MarkTripAssignedRequest { ... },
    cancellationToken: ct);
```

**Effect**:
- No timeout → hung requests pile up
- No retry → transient failures become permanent
- No circuit breaker → cascading failures across services

**Critical Path**: TripService → DriverService communication

---

## 🟢 MEDIUM PRIORITY 5: Synchronous Pipeline
**Impact**: Cannot scale horizontally; business logic in consumers

**Current State**:
- Trip matching logic inside `TripOfferedConsumer` (lines 44-129)
- Consumer performs: DB queries + Redis ops + gRPC calls + event publishing
- No separation of concerns

**Effect**:
- Cannot scale individual steps independently
- Consumer redelivery triggers full pipeline retry
- Hard to monitor individual stage performance

---

## 🟢 MEDIUM PRIORITY 6: RabbitMQ Configuration
**Impact**: Suboptimal throughput and memory usage

**Current State**: Default RabbitMQ settings (not explicitly configured)

**Missing**:
- Prefetch count tuning (recommend: 10-50 based on workload)
- Consumer concurrency settings
- Queue durability vs performance trade-offs

---

# 4. Architecture Improvements (Prioritized Implementation)

## 🔴 PRIORITY 1: Replace Task.Delay with Redis Sorted Set Timeout Scheduler

### Implementation Plan

**Step 1.1**: Create TimeoutScheduler Service
```csharp
// New file: TripService/TripService.Application/Services/TripOfferTimeoutScheduler.cs
public class TripOfferTimeoutScheduler
{
    private readonly IConnectionMultiplexer _redis;
    private const string TIMEOUT_KEY = "trip:offers:timeouts";

    public async Task ScheduleTimeoutAsync(Guid tripId, Guid driverId, int ttlSeconds)
    {
        var db = _redis.GetDatabase();
        var expireAtUnix = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds).ToUnixTimeSeconds();
        await db.SortedSetAddAsync(TIMEOUT_KEY, $"{tripId}:{driverId}", expireAtUnix);
    }

    public async Task<List<(Guid TripId, Guid DriverId)>> GetExpiredOffersAsync()
    {
        var db = _redis.GetDatabase();
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expired = await db.SortedSetRangeByScoreAsync(
            TIMEOUT_KEY,
            stop: nowUnix,
            order: Order.Ascending,
            take: 100);

        // Remove from sorted set
        if (expired.Length > 0)
            await db.SortedSetRemoveAsync(TIMEOUT_KEY, expired);

        return expired.Select(Parse).ToList();
    }
}
```

**Step 1.2**: Create OfferTimeoutWorker (Background Service)
```csharp
// New file: TripService/TripService.Api/BackgroundServices/OfferTimeoutWorker.cs
public class OfferTimeoutWorker : BackgroundService
{
    private readonly TripOfferTimeoutScheduler _scheduler;
    private readonly IOfferStore _offers;
    private readonly IEventPublisher _bus;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var expired = await _scheduler.GetExpiredOffersAsync();

            foreach (var (tripId, driverId) in expired)
            {
                var declined = await _offers.IsDeclinedAsync(tripId, driverId, ct);
                if (declined)
                {
                    await _bus.PublishAsync(Routing.Keys.TripOfferTimeout,
                        new TripOfferTimeout(tripId, driverId), ct);
                }
                else
                {
                    // Auto-assign
                    await _bus.PublishAsync(Routing.Keys.TripAutoAssigned,
                        new TripAutoAssigned(tripId, driverId), ct);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct); // Poll every 1s
        }
    }
}
```

**Step 1.3**: Refactor TripOfferedConsumer (Remove Blocking)
```csharp
// Modified: TripService/TripService.Api/Messaging/TripOfferedConsumer.cs
protected override async Task HandleAsync(
    TripOffered message,
    BasicDeliverEventArgs ea,
    IModel channel,
    CancellationToken ct)
{
    // ✅ NO MORE Task.Delay - just schedule timeout
    await _timeoutScheduler.ScheduleTimeoutAsync(
        message.TripId,
        message.DriverId,
        message.TtlSeconds);

    // Store offer
    await _offers.SetPendingAsync(
        message.TripId,
        message.DriverId,
        TimeSpan.FromSeconds(message.TtlSeconds),
        ct);

    // Consumer immediately returns - no blocking!
}
```

**Expected Improvement**:
- ✅ Consumer throughput: **unlimited** (was: ~66 concurrent)
- ✅ Queue backlog: **eliminated**
- ✅ p95 latency: **<5ms** (was: 15000ms)

---

## 🔴 PRIORITY 2: Redis GEO Partitioning (Geohash Sharding)

### Implementation Plan

**Step 2.1**: Calculate Geohash Partition Key
```csharp
// New file: DriverService/DriverService.Application/Helpers/GeohashHelper.cs
public static class GeohashHelper
{
    private const int PRECISION = 5; // ~4.9km × 4.9km cells

    public static string GetPartitionKey(double lat, double lng)
    {
        var geohash = CalculateGeohash(lat, lng, PRECISION);
        return $"drivers:online:{geohash}";
    }

    public static List<string> GetNeighborPartitions(double lat, double lng)
    {
        var center = CalculateGeohash(lat, lng, PRECISION);
        var neighbors = GetAdjacentGeohashes(center);
        return neighbors.Select(g => $"drivers:online:{g}").ToList();
    }

    private static string CalculateGeohash(double lat, double lng, int precision)
    {
        // Use NetTopologySuite.Geometries or NGeoHash library
        return NGeoHash.GeoHash.Encode(lat, lng, precision);
    }
}
```

**Step 2.2**: Update DriverLocationService
```csharp
// Modified: DriverService/DriverService.Application/Services/DriverLocationService.cs
public async Task UpdateLocationAsync(Guid driverId, double lat, double lng)
{
    var db = _redis.GetDatabase();

    // 1. Remove from old partition (if exists)
    var oldPartition = await GetDriverCurrentPartitionAsync(driverId);
    if (oldPartition != null)
        await db.GeoRemoveAsync(oldPartition, driverId.ToString());

    // 2. Add to new partition
    var newPartition = GeohashHelper.GetPartitionKey(lat, lng);
    await db.GeoAddAsync(newPartition, lng, lat, driverId.ToString());

    // 3. Store partition mapping for quick lookup
    await db.StringSetAsync($"driver:{driverId}:partition", newPartition, TimeSpan.FromHours(24));

    // 4. Set TTL on partition key (auto-cleanup)
    await db.KeyExpireAsync(newPartition, TimeSpan.FromHours(24));
}

public async Task<List<DriverLocation>> SearchNearbyAsync(double lat, double lng, double radiusKm)
{
    var db = _redis.GetDatabase();
    var partitions = GeohashHelper.GetNeighborPartitions(lat, lng);

    // Query multiple partitions in parallel
    var tasks = partitions.Select(p =>
        db.GeoRadiusAsync(p, lng, lat, radiusKm, GeoUnit.Kilometers));

    var results = await Task.WhenAll(tasks);
    return results.SelectMany(r => r).Select(MapToDriverLocation).ToList();
}
```

**Step 2.3**: Add Partition Management
```csharp
// New file: DriverService/DriverService.Api/BackgroundServices/PartitionCleanupWorker.cs
// Cleans up empty partitions and expired driver mappings every 1 hour
```

**Expected Improvement**:
- ✅ GEO write throughput: **20,000-50,000 ops/sec** (was: ~2,000)
- ✅ GEO read throughput: **5,000-10,000 ops/sec** (was: ~1,500)
- ✅ p95 latency: **<8ms** (was: 40-80ms)
- ✅ Redis CPU: **<40%** (was: 90%+)

---

## 🟡 PRIORITY 3: Integrate Trip & Driver Locks into Pipeline

### Implementation Plan

**Step 3.1**: Create Lock Acquisition Helper
```csharp
// New file: TripService/TripService.Application/Services/TripLockManager.cs
public class TripLockManager
{
    private readonly TripMatchService _match;
    private readonly ILogger<TripLockManager> _logger;

    public async Task<TripDriverLock?> AcquireLocksAsync(
        Guid tripId,
        Guid driverId,
        TimeSpan ttl)
    {
        // Try acquire trip lock first
        var tripLocked = await _match.TryLockTripAsync(tripId, ttl);
        if (!tripLocked)
        {
            _logger.LogWarning("Failed to acquire trip lock: {TripId}", tripId);
            return null;
        }

        // Then try driver lock
        var driverLocked = await _match.TryLockDriverAsync(driverId, tripId, ttl);
        if (!driverLocked)
        {
            // Release trip lock on failure
            await ReleaseTripLockAsync(tripId);
            _logger.LogWarning("Failed to acquire driver lock: {DriverId}", driverId);
            return null;
        }

        return new TripDriverLock(tripId, driverId, ttl);
    }

    public async Task ReleaseLocksAsync(Guid tripId, Guid driverId)
    {
        await Task.WhenAll(
            ReleaseTripLockAsync(tripId),
            ReleaseDriverLockAsync(driverId));
    }
}
```

**Step 3.2**: Integrate into TripAutoAssignedConsumer (New)
```csharp
// New file: TripService/TripService.Api/Messaging/TripAutoAssignedConsumer.cs
// Handles TripAutoAssigned event from OfferTimeoutWorker
protected override async Task HandleAsync(TripAutoAssigned message, ...)
{
    // 1. Acquire locks
    var locks = await _lockManager.AcquireLocksAsync(
        message.TripId,
        message.DriverId,
        TimeSpan.FromSeconds(30));

    if (locks == null)
    {
        _logger.LogWarning("Lock acquisition failed, trip may already be assigned");
        return; // Already locked by another process
    }

    try
    {
        // 2. Double-check trip status
        var trip = await _repo.GetAsync(message.TripId, ct);
        if (trip.Status != TripStatus.FindingDriver)
        {
            return; // Race condition: trip already assigned
        }

        // 3. Mark driver as assigned via gRPC
        var resp = await _driverGrpc.MarkTripAssignedAsync(...);
        if (!resp.Success)
        {
            return;
        }

        // 4. Update trip
        trip.AssignedDriverId = message.DriverId;
        trip.Status = TripStatus.DriverAccepted;
        await _repo.UpdateAsync(trip, ct);

        // 5. Publish success
        await _bus.PublishAsync(Routing.Keys.TripAssigned,
            new TripAssigned(trip.Id, message.DriverId, DateTime.UtcNow), ct);
    }
    finally
    {
        // 6. Release locks
        await _lockManager.ReleaseLocksAsync(message.TripId, message.DriverId);
    }
}
```

**Expected Improvement**:
- ✅ Zero double-assignments
- ✅ Race conditions eliminated
- ✅ Lock overhead: **<2ms p95**

---

## 🟡 PRIORITY 4: Add gRPC Resilience (Polly Policies)

### Implementation Plan

**Step 4.1**: Install Polly NuGet Package
```bash
dotnet add TripService/TripService.Api package Polly
dotnet add TripService/TripService.Api package Polly.Extensions.Http
```

**Step 4.2**: Configure Resilience Policy
```csharp
// Modified: TripService/TripService.Api/Program.cs
builder.Services.AddGrpcClient<DriverQuery.DriverQueryClient>(o =>
{
    o.Address = new Uri(builder.Configuration["DriverService:GrpcUrl"]!);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .Or<RpcException>(ex => ex.StatusCode == StatusCode.Unavailable)
        .WaitAndRetryAsync(
            retryCount: 2,
            sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Log.Warning($"Retry {retryAttempt} after {timespan.TotalMilliseconds}ms");
            });
}

static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
{
    return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromMilliseconds(500));
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, duration) =>
            {
                Log.Error($"Circuit breaker opened for {duration.TotalSeconds}s");
            },
            onReset: () => Log.Information("Circuit breaker reset"));
}
```

**Step 4.3**: Add Deadline to gRPC Calls
```csharp
// Modified: TripService/TripService.Api/Messaging/TripAutoAssignedConsumer.cs
var resp = await _driverGrpc.MarkTripAssignedAsync(
    new MarkTripAssignedRequest { ... },
    deadline: DateTime.UtcNow.AddMilliseconds(300), // Hard deadline
    cancellationToken: ct);
```

**Expected Improvement**:
- ✅ Timeout failures: **<1%** (was: unbounded)
- ✅ Transient error recovery: **automatic**
- ✅ Cascading failure prevention: **circuit breaker active**
- ✅ p99 latency: **<500ms** (hard deadline)

---

## 🟢 PRIORITY 5: Extract Workers for Async Pipeline (Optional Enhancement)

*This is a larger refactor for Phase 2.5 or Phase 3. Not critical for initial Phase 2 goals.*

**New Architecture**:
```
TripCreated Event
  → FindDriverWorker (queries Redis GEO)
    → publishes DriverFoundForTrip
  → OfferWorker (acquires locks, sends offer)
    → publishes TripOffered
  → OfferTimeoutWorker (polls Redis sorted set)
    → publishes TripOfferTimeout OR TripAutoAssigned
  → AssignmentWorker (finalizes assignment)
    → publishes TripAssigned
```

**Benefits**:
- Each worker scales independently
- Retry granularity at worker level
- Better observability per stage

---

## 🟢 PRIORITY 6: RabbitMQ Tuning

### Configuration Changes

```json
// appsettings.Production.json
{
  "RabbitMQ": {
    "PrefetchCount": 20,  // Process 20 messages per consumer before ACK
    "ConsumerConcurrency": 10,  // 10 concurrent consumer threads per queue
    "PublisherConfirms": false,  // Disable for higher throughput (enable for critical events)
    "HeartbeatInterval": 60
  }
}
```

```csharp
// Modified: BaseRabbitConsumer.cs
_channel.BasicQos(
    prefetchSize: 0,
    prefetchCount: _options.PrefetchCount,  // Was: 10 (default)
    global: false);
```

**Expected Improvement**:
- ✅ Consumer throughput: **+30-50%**
- ✅ Memory efficiency: **better batching**

---

# 5. Execution Timeline (6 Weeks - REALISTIC)

## Week 1: Baseline Testing & Architecture Design (Days 1-5)

**Day 1-2: Run Current Baseline Tests**
- Execute E2E.PerformanceTests at LOW load (current config)
- Capture metrics:
  - Workload A: Trip creation throughput & latency
  - Workload B: Driver response handling
  - Workload C: Location update throughput (~40/sec)
- Export NBomber reports to `reports/baseline/`
- Monitor: Redis CPU, RabbitMQ queue depth, PostgreSQL connections

**Day 3-4: Architecture Design**
- Design Timeout Scheduler architecture (Redis Sorted Set)
- Design Geohash partitioning strategy (precision 5)
- Design lock integration points
- Create sequence diagrams for new flow

**Day 5: Write ADRs**
- ADR-001: Timeout Scheduler with Redis Sorted Set
- ADR-002: Redis GEO Partitioning Strategy
- ADR-003: Lock Acquisition Order (Trip → Driver)
- ADR-004: gRPC Resilience Policies
- ADR-005: RabbitMQ Prefetch Tuning

## Week 2: PRIORITY 1 - Timeout Scheduler (Days 6-10)

**Day 6-7: Implement Core Components**
- Create `TripOfferTimeoutScheduler` service
- Create `OfferTimeoutWorker` background service
- Unit tests for scheduler logic

**Day 8: Refactor TripOfferedConsumer**
- Remove `Task.Delay(15s)`
- Integrate scheduler
- Integration tests

**Day 9: Create TripAutoAssignedConsumer**
- Handle auto-assignment event
- Integration tests

**Day 10: End-to-End Testing**
- Test full offer → timeout → auto-assign flow
- Verify no blocking
- Measure throughput improvement

**Validation**:
- ✅ Consumer no longer blocks
- ✅ Queue backlog = 0 under moderate load (500 trips/min)

## Week 3: PRIORITY 2 - Redis GEO Partitioning (Days 11-15)

**Day 11-12: Implement Geohash Helpers**
- Add NGeoHash NuGet package
- Create `GeohashHelper` class
- Unit tests for partition calculation

**Day 13-14: Refactor DriverLocationService**
- Implement partitioned GEOADD
- Implement multi-partition GEORADIUS
- Add partition cleanup logic
- Integration tests with Redis

**Day 15: Performance Validation**
- Run isolated GEO write/read load tests
- Target: 10,000 writes/sec sustained
- Verify partition distribution

**Validation**:
- ✅ GEO throughput >10K ops/sec
- ✅ Redis CPU <50% under load

## Week 4: PRIORITY 3 & 4 - Locks + gRPC (Days 16-20)

**Day 16-17: Lock Integration**
- Create `TripLockManager` service
- Integrate into `TripAutoAssignedConsumer`
- Create lock release cleanup job
- Unit + integration tests

**Day 18-19: gRPC Resilience**
- Install Polly
- Configure retry/timeout/circuit breaker policies
- Add deadlines to all gRPC calls
- Test failure scenarios

**Day 20: Validation**
- Test double-assignment prevention
- Test gRPC failure recovery
- Chaos test: kill DriverService mid-request

**Validation**:
- ✅ Zero double-assignments in 1000-trip test
- ✅ gRPC transient failures auto-recover

## Week 5: HIGH-LOAD Testing & Optimization (Days 21-25)

**Day 21: Configure Hardcore Tests**
- Update `TestConfig.cs` for high load:
  - WorkloadA: 500 users, 180s
  - WorkloadC: 2000 drivers, 3s interval = 666 updates/sec
- Add WorkloadD: GEO search stress test

**Day 22-23: Run Full Test Suite**
- Execute all workloads sequentially
- Monitor for: memory leaks, connection exhaustion, queue buildup
- Capture metrics

**Day 24-25: Analyze & Optimize**
- Identify remaining bottlenecks from metrics
- Tune: RabbitMQ prefetch, PostgreSQL connection pool
- Re-run tests
- Compare before/after charts

**Validation**:
- ✅ Trip throughput >800/sec
- ✅ Location updates >5,000/sec
- ✅ p95 latency <50ms for all operations

## Week 6: Documentation & Review (Days 26-30)

**Day 26-27: Generate Reports**
- Export all NBomber HTML reports
- Create comparison charts (before/after)
- Document performance improvements

**Day 28-29: Write PHASE2_REPORT.md**
- Architecture changes summary
- Bottleneck analysis
- Performance results with charts
- Trade-offs and decisions
- Lessons learned
- Phase 3 recommendations

**Day 30: Code Review & Cleanup**
- Remove debug logs
- Ensure all tests pass
- Update README.md
- Tag release: `v2.0.0-phase2-complete`

---

# 6. Success Criteria (Measurable KPIs)

## Hard Requirements (Must Pass)

| Metric | Phase 1 Baseline | Phase 2 Target | Validation Method |
|--------|------------------|----------------|-------------------|
| **Consumer Blocking** | 15,000ms per offer | 0ms | Code review + NBomber metrics |
| **Trip Throughput** | ~2 trips/sec | **≥800 trips/sec** | NBomber WorkloadA |
| **Location Update Throughput** | ~40/sec | **≥5,000/sec** | NBomber WorkloadC |
| **GEO Search Throughput** | Untested | **≥3,000/sec** | New WorkloadD |
| **p95 Latency (GEO ops)** | >80ms | **<15ms** | NBomber percentiles |
| **p99 Latency (GEO ops)** | >200ms | **<50ms** | NBomber percentiles |
| **Redis CPU (under load)** | ~90%+ | **<50%** | Redis INFO stats |
| **RabbitMQ Queue Backlog** | Growing | **Stable (0-10)** | Management UI |
| **Double Assignments** | Possible | **0** | Integration test with 1000 concurrent trips |
| **gRPC Timeout Rate** | High | **<1%** | Application logs |

## Soft Goals (Nice to Have)

- p50 latency <5ms for all operations
- System stable for 10+ minutes under max load
- Memory usage <2GB per service
- Zero manual interventions during 24h soak test

---

# 7. Risk Mitigation

## Risk 1: Geohash Partitioning Too Complex
**Mitigation**: Start with precision 5, measure, adjust if needed. Fallback: single partition for low-density areas.

## Risk 2: Redis Sorted Set Polling Overhead
**Mitigation**: Poll interval adaptive (1s when busy, 5s when idle). Monitor CPU impact.

## Risk 3: Lock Contention Deadlocks
**Mitigation**: Always acquire in same order (trip → driver). TTL ensures auto-release. Add lock timeout alerts.

## Risk 4: Timeline Slips
**Mitigation**: Priorities 1-2 are mandatory (Weeks 2-3). Priorities 3-4 can slip to Week 5 if needed. Week 6 buffer.

## Risk 5: Test Environment != Production
**Mitigation**: Use production-like config (separate Redis/RabbitMQ instances). Document environment differences in report.

---

# 8. Dependencies & Prerequisites

## Required Infrastructure
- Redis 7.0+ (GEO commands + sorted sets)
- RabbitMQ 3.12+
- PostgreSQL 15+
- .NET 8.0 SDK

## Required NuGet Packages
```bash
# TripService
dotnet add package NGeoHash --version 2.0.0
dotnet add package Polly --version 8.0.0
dotnet add package Polly.Extensions.Http --version 3.0.0

# DriverService
dotnet add package NGeoHash --version 2.0.0
```

## Development Tools
- NBomber 5.0+ for load testing
- Redis Insight for partition monitoring
- Seq or Grafana for log aggregation
- pgAdmin for PostgreSQL monitoring

---

# 9. Rollback Plan

If Phase 2 introduces regressions:

1. **Revert Timeout Scheduler**: Remove `OfferTimeoutWorker`, restore `Task.Delay` in `TripOfferedConsumer`
2. **Revert GEO Partitioning**: Change `GetPartitionKey()` to return `"drivers:online"` (single partition)
3. **Disable Lock Integration**: Comment out lock acquisition in consumers
4. **Remove gRPC Policies**: Remove `.AddPolicyHandler()` registrations

Feature flags recommended for gradual rollout:
```json
{
  "Features": {
    "UseTimeoutScheduler": true,
    "UseGeoPartitioning": true,
    "UseDistributedLocks": true,
    "EnableGrpcResilience": true
  }
}
```

---

# 10. Phase 3 Preview (Future Work)

After Phase 2 completion, consider:
- **Caching Layer**: Redis cache for trip details, driver profiles
- **Read Replicas**: PostgreSQL read replicas for heavy queries
- **Kafka Integration**: Replace RabbitMQ for event streaming at >10K events/sec
- **Sharded Database**: Partition trips by geohash for horizontal scaling
- **Real-time Tracking**: WebSocket connections for live location updates
- **Analytics Pipeline**: Separate write path for metrics (ClickHouse, Prometheus)

---

# Appendix A: File Change Checklist

## New Files to Create (11 files)
1. `TripService/TripService.Application/Services/TripOfferTimeoutScheduler.cs`
2. `TripService/TripService.Api/BackgroundServices/OfferTimeoutWorker.cs`
3. `TripService/TripService.Api/Messaging/TripAutoAssignedConsumer.cs`
4. `TripService/TripService.Api/Messaging/TripOfferTimeoutConsumer.cs`
5. `TripService/TripService.Application/Services/TripLockManager.cs`
6. `DriverService/DriverService.Application/Helpers/GeohashHelper.cs`
7. `DriverService/DriverService.Api/BackgroundServices/PartitionCleanupWorker.cs`
8. `E2E.PerformanceTests/Workloads/WorkloadD_GeoSearchStress.cs`
9. `docs/adr/ADR-001-timeout-scheduler.md`
10. `docs/adr/ADR-002-geo-partitioning.md`
11. `docs/adr/ADR-003-lock-strategy.md`

## Files to Modify (6 files)
1. `TripService/TripService.Api/Messaging/TripOfferedConsumer.cs` (remove Task.Delay)
2. `DriverService/DriverService.Application/Services/DriverLocationService.cs` (add partitioning)
3. `TripService/TripService.Api/Program.cs` (register new services + Polly)
4. `E2E.PerformanceTests/Infrastructure/TestConfig.cs` (add high-load configs)
5. `TripService/TripService.Application/Services/TripMatchService.cs` (integrate locks)
6. `SharedLib/Events/TripEvents.cs` (add TripAutoAssigned, TripOfferTimeout events)

## Dependencies to Add (3 packages)
1. `NGeoHash` (DriverService + TripService)
2. `Polly` (TripService)
3. `Polly.Extensions.Http` (TripService)

---

**Document Version**: 2.0 Enhanced
**Last Updated**: 2025-01-04
**Author**: System Architect
**Status**: Ready for Implementation

