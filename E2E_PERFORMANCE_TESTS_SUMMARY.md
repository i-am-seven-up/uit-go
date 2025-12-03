# E2E Performance Tests - Implementation Summary

**Date**: 2025-12-01
**Purpose**: Establish Phase 1 baseline metrics and enable Phase 2 Module A comparison

---

## What Was Created

I've created a comprehensive performance testing framework for your UIT-GO ride-hailing system with the following components:

### 1. **Architectural Analysis Document**
📄 `PHASE2_SCALABILITY_ANALYSIS.md`

A detailed analysis explaining:
- **Why Redis GEO?** - O(log N) spatial queries for sub-millisecond driver lookups
- **Why RabbitMQ?** - Async event-driven architecture for scalability
- **Why Event-Driven?** - Loose coupling and horizontal scaling capability

**Key Trade-offs Analyzed**:
- Sync vs Async communication (when to use each)
- Eventual vs Strong consistency (performance vs accuracy)
- Memory (Redis) vs Persistent storage (cost vs speed)

**How Phase 1 Lifecycle Enables Scalability**:
- Idempotent state transitions
- Decoupled driver matching
- Built-in retry logic
- Compensation events for consistency

---

### 2. **E2E Performance Test Project**
📁 `E2E.PerformanceTests/`

A complete .NET 8.0 console application using **NBomber** (load testing framework) with:

**Infrastructure**:
- `JwtTokenHelper.cs` - Generate authentication tokens for test users
- `RedisHelper.cs` - Seed drivers, query metrics, manage test data
- `TestConfig.cs` - Configurable test parameters via environment variables

**Three Production-Ready Workloads**:

#### **Workload A: High-Volume Trip Creation**
Simulates rush hour with 100 concurrent passengers requesting trips simultaneously.

**What it tests**:
- Trip creation latency (p50/p90/p99)
- Driver matching performance (GEORADIUS queries)
- PostgreSQL write throughput
- Redis driver locking mechanism
- Success rate under load

**Default parameters** (configurable):
- 100 concurrent users
- 60 second duration
- 500 seeded drivers
- Random HCMC locations

**KPIs measured**:
- p99 latency (target: <500ms → Phase 2 goal: <300ms)
- Throughput (target: ~50-100 req/s → Phase 2 goal: 100+ req/s)
- Success rate (target: >95% → Phase 2 goal: >98%)
- Redis/PostgreSQL CPU usage

---

#### **Workload B: Burst of Driver Accept/Decline Events**
Tests event-driven architecture with 50 drivers responding to trip assignments.

**What it tests**:
- RabbitMQ message processing speed
- Event consumer throughput
- State consistency after async operations
- Queue depth under burst load
- Trip retry logic (when drivers decline)

**Default parameters**:
- 50 concurrent drivers
- 70% accept rate, 30% decline rate
- 30 second burst duration

**KPIs measured**:
- Event processing latency p99 (target: <300ms → Phase 2: <200ms)
- RabbitMQ throughput (target: >500 msg/s → Phase 2: >1000 msg/s)
- Peak queue depth (target: <100 → Phase 2: <50)
- State consistency (target: 100%)

---

#### **Workload C: High-Frequency Driver Location Updates**
Simulates 200 drivers updating GPS location every 5 seconds while trips are created.

**What it tests**:
- Redis GEOADD write performance
- Redis memory growth with location data
- GEORADIUS query performance under write load
- Trip creation latency with high Redis load
- Location update throughput

**Default parameters**:
- 200 concurrent drivers
- Update every 5 seconds
- 60 second duration
- 20 concurrent trip creations

**KPIs measured**:
- Location update p99 (target: <100ms → Phase 2: <80ms)
- Update throughput (target: >1000/s → Phase 2: >2000/s)
- Redis memory growth (target: <50MB)
- Trip creation performance under load

---

### 3. **Test Runner Scripts**

#### **Windows (PowerShell)**
📄 `run-performance-tests.ps1`

```powershell
# Run all workloads
.\run-performance-tests.ps1

# Run specific workload
.\run-performance-tests.ps1 -Workload a

# Skip building (faster re-runs)
.\run-performance-tests.ps1 -SkipBuild

# Help
.\run-performance-tests.ps1 -Help
```

Features:
- ✅ Checks prerequisites (Docker, .NET, services running)
- ✅ Builds test project
- ✅ Runs workloads with progress tracking
- ✅ Exports results to JSON
- ✅ Calculates total duration

#### **Linux/macOS (Bash)**
📄 `run-performance-tests.sh`

```bash
chmod +x run-performance-tests.sh
./run-performance-tests.sh          # All workloads
./run-performance-tests.sh -w a     # Workload A only
./run-performance-tests.sh --help
```

---

### 4. **Baseline Results Template**
📄 `PERFORMANCE_BASELINE_TEMPLATE.md`

A comprehensive template for documenting:

**Phase 1 Results** (fill out after running tests):
- All KPI measurements from JSON exports
- Resource utilization (CPU, memory, connections)
- Observed bottlenecks
- Failed requests and errors

**Phase 2 Results** (after implementing Module A):
- Same metrics for comparison
- Improvement percentages
- What optimizations were applied
- Unexpected results

**Overall Summary**:
- Side-by-side comparison table
- Success criteria checklist (40% latency reduction, 2x throughput, etc.)
- Key findings and recommendations

---

### 5. **JSON Result Exports**

Each workload automatically exports detailed metrics to JSON:

```json
{
  "workload": "WorkloadA",
  "timestamp": "2025-12-01T14:30:22Z",
  "total_requests": 5000,
  "success_count": 4850,
  "success_rate": 97.0,
  "latency": {
    "p50": 85,
    "p75": 145,
    "p90": 180,
    "p99": 420,
    "mean": 125,
    "stddev": 78,
    "min": 45,
    "max": 1200
  },
  "throughput_rps": 83.33,
  "additional_metrics": {
    "online_drivers": 500,
    "available_drivers": 350,
    "redis_memory_mb": 42.5
  }
}
```

Files are timestamped: `results_WorkloadA_20251201_143022.json`

---

### 6. **Fixed Original Integration Tests**

Also fixed the issue with `run-tests.ps1` and `run-tests.sh`:
- Changed from non-existent `uit-go.sln`
- Now builds `TripService\TripService.sln` correctly
- Your existing integration tests will now run successfully

---

## How to Use This Testing Framework

### Step 1: Run Phase 1 Baseline Tests

1. **Ensure services are running**:
   ```bash
   docker-compose up -d
   docker ps  # Verify all services healthy
   ```

2. **Run all performance workloads**:
   ```powershell
   cd C:\Users\tophu\source\repos\uit-go
   .\run-performance-tests.ps1
   ```

3. **Wait for completion** (~5-10 minutes for all 3 workloads)

4. **Collect JSON results**:
   - `results_WorkloadA_*.json`
   - `results_WorkloadB_*.json`
   - `results_WorkloadC_*.json`

### Step 2: Document Phase 1 Baseline

1. Open `PERFORMANCE_BASELINE_TEMPLATE.md`

2. Fill in **Phase 1 Results** section for each workload:
   - Copy metrics from JSON files
   - Document your hardware specs
   - Note any bottlenecks observed
   - Record resource usage from `docker stats`

3. Save as `PHASE1_BASELINE_RESULTS.md`

### Step 3: Implement Phase 2 Module A

Based on baseline findings, implement scalability improvements:
- Database connection pooling
- Redis connection reuse
- RabbitMQ prefetch optimization
- Async/await improvements
- Caching strategies
- Whatever bottlenecks you identified

### Step 4: Re-run Tests (Phase 2)

1. **Same exact conditions**:
   ```powershell
   .\run-performance-tests.ps1
   ```

2. **Collect new JSON results**

3. **Fill in Phase 2 section** of template

4. **Compare improvements**:
   - Calculate improvement percentages
   - Verify success criteria met
   - Document what changed

### Step 5: Analyze & Report

Use the completed baseline template to:
- Show stakeholders performance improvements
- Justify scalability investments
- Identify remaining bottlenecks
- Plan Phase 3 optimizations

---

## Key Performance Targets

### Phase 1 → Phase 2 Improvement Goals

| Metric | Phase 1 Target | Phase 2 Goal | Improvement |
|--------|---------------|--------------|-------------|
| **Trip Creation p99** | <500ms | <300ms | **40% faster** |
| **Event Processing p99** | <300ms | <200ms | **33% faster** |
| **Location Update p99** | <100ms | <80ms | **20% faster** |
| **Trip Creation Throughput** | 50-100/s | 100+/s | **2x throughput** |
| **Message Processing** | >500 msg/s | >1000 msg/s | **2x throughput** |
| **Location Updates** | >1000/s | >2000/s | **2x throughput** |
| **CPU Usage** | Baseline | -20% | **Resource efficient** |
| **Success Rate** | >95% | >98% | **More reliable** |

---

## Customizing Test Parameters

All workloads support environment variable configuration:

```powershell
# Increase load for Workload A
$env:WORKLOAD_A_USERS = "200"
$env:WORKLOAD_A_DURATION = "120"
$env:WORKLOAD_A_DRIVERS = "1000"

# Run the test
.\run-performance-tests.ps1 -Workload a
```

```bash
# Linux/macOS
export WORKLOAD_A_USERS=200
export WORKLOAD_A_DURATION=120
./run-performance-tests.sh -w a
```

**Available parameters**:
- `WORKLOAD_A_USERS` - Concurrent passengers (default: 100)
- `WORKLOAD_A_DURATION` - Test duration in seconds (default: 60)
- `WORKLOAD_A_RAMPUP` - Ramp-up period (default: 10)
- `WORKLOAD_A_DRIVERS` - Drivers to seed (default: 500)
- `WORKLOAD_B_DRIVERS` - Concurrent drivers (default: 50)
- `WORKLOAD_B_ACCEPT_RATE` - Accept rate 0.0-1.0 (default: 0.7)
- `WORKLOAD_C_DRIVERS` - Concurrent drivers (default: 200)
- `WORKLOAD_C_INTERVAL` - Update interval seconds (default: 5)

---

## Monitoring During Tests

### Watch Docker Resources
```bash
docker stats
```

### Monitor Redis
```bash
docker exec -it uit-go-redis-1 redis-cli INFO memory
docker exec -it uit-go-redis-1 redis-cli DBSIZE
```

### Monitor RabbitMQ
Open: http://localhost:15672 (guest/guest)
- Watch queue depths
- Monitor message rates
- Check consumer status

### Monitor PostgreSQL
```bash
docker exec -it uit-go-postgres-1 psql -U postgres -d tripservice -c "SELECT count(*) FROM pg_stat_activity;"
```

---

## Troubleshooting

### Services Not Running
```bash
docker-compose up -d
docker ps  # Verify all containers
```

### Out of Memory
Reduce concurrent load:
```powershell
$env:WORKLOAD_A_USERS = "50"
$env:WORKLOAD_C_DRIVERS = "100"
```

### RabbitMQ Queue Buildup
Check consumer logs:
```bash
docker logs uit-go-tripservice-1 --tail 100
```

### Port Conflicts
Verify actual ports:
```bash
docker ps
```
Update `E2E.PerformanceTests/Infrastructure/TestConfig.cs` if needed.

---

## Files Created

```
uit-go/
├── PHASE2_SCALABILITY_ANALYSIS.md           # Architectural analysis
├── PERFORMANCE_BASELINE_TEMPLATE.md         # Results template
├── E2E_PERFORMANCE_TESTS_SUMMARY.md         # This file
├── run-performance-tests.ps1                # Windows test runner
├── run-performance-tests.sh                 # Linux/macOS test runner
├── run-tests.ps1                            # FIXED (existing integration tests)
├── run-tests.sh                             # FIXED (existing integration tests)
└── E2E.PerformanceTests/                    # Test project
    ├── E2E.PerformanceTests.csproj
    ├── Program.cs                           # Main entry point
    ├── README.md                            # Detailed usage guide
    ├── Infrastructure/
    │   ├── JwtTokenHelper.cs
    │   ├── RedisHelper.cs
    │   └── TestConfig.cs
    └── Workloads/
        ├── WorkloadA_TripCreation.cs
        ├── WorkloadB_DriverResponses.cs
        └── WorkloadC_LocationUpdates.cs
```

---

## What Makes This Framework Special

✅ **Production-Ready**: Uses NBomber, industry-standard load testing framework
✅ **Realistic Scenarios**: Based on actual user behavior (trip creation, driver responses, GPS updates)
✅ **Comprehensive Metrics**: p50/p75/p90/p99 latency, throughput, resource utilization
✅ **Easy to Run**: Single command execution with detailed progress output
✅ **Automated Export**: JSON results for programmatic analysis
✅ **Repeatable**: Same tests run on Phase 1 and Phase 2 for fair comparison
✅ **Configurable**: Environment variables allow tuning test intensity
✅ **Well-Documented**: Complete README, architectural analysis, and result templates

---

## Next Steps

1. **Run Phase 1 Baseline** (do this first!)
   ```powershell
   .\run-performance-tests.ps1
   ```

2. **Document Results**
   - Fill out `PERFORMANCE_BASELINE_TEMPLATE.md`
   - Save as `PHASE1_BASELINE_RESULTS.md`

3. **Analyze Bottlenecks**
   - Which workload performs worst?
   - What resource is the bottleneck?
   - Where should optimization focus?

4. **Implement Phase 2 Module A**
   - Apply performance optimizations
   - Based on baseline findings

5. **Re-test and Compare**
   - Run same workloads
   - Measure improvements
   - Validate success criteria met

---

## Questions?

Refer to these documents:
- **How to run tests**: `E2E.PerformanceTests/README.md`
- **Why this architecture**: `PHASE2_SCALABILITY_ANALYSIS.md`
- **What to measure**: `PERFORMANCE_BASELINE_TEMPLATE.md`
- **Existing integration tests**: `TEST_INSTRUCTIONS.md`

---

**Ready to establish your baseline?** 🚀

```powershell
.\run-performance-tests.ps1
```

**Good luck with Phase 2 Module A!**
