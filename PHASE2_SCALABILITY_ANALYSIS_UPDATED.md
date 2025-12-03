# Phase 2 – Module A: Scalability & Performance Analysis (Updated)

**Project**: UIT-Go Ride-Hailing Backend
**Current State**: Phase 1 Baseline Completed & Workload C Optimized
**Objective**: Optimize TripService and Event Processing to handle high concurrency based on actual measured baselines.

---

## 1. Baseline Metrics & Revised Phase 2 Goals

We have established real-world baselines from the "Phase 1 Baseline" test runs. These actuals are significantly faster than the conservative estimates in the original plan, meaning our system is starting from a *better* place, but we must now set more ambitious targets for "High Scalability."

### Workload A: High-Volume Trip Creation
*Simulating 100 concurrent users creating trips.*

| Metric | Original Baseline Estimate | **Actual Measured Baseline** | **Revised Phase 2 Goal** |
|--------|----------------------------|------------------------------|--------------------------|
| **p50 Latency** | <100ms | **22.4 ms** | **< 20 ms** |
| **p95 Latency** | <200ms | **26.5 ms** | **< 25 ms** |
| **p99 Latency** | <500ms | **30.8 ms** | **< 30 ms** |
| **Throughput** | 50 RPS | **91 RPS** | **> 150 RPS** (Stress Test) |
| **Success Rate** | 95% | **100%** | **100%** |

**Analysis:**
- The system is performing exceptionally well at 100 concurrent users.
- **Action:** We must increase the load to **300-500 concurrent users** to find the *breaking point*. The current load is too light to trigger bottlenecks.

---

### Workload B: Driver Responses (Event Processing)
*Simulating 50 drivers accepting/declining trips.*

| Metric | Original Baseline Estimate | **Actual Measured Baseline** | **Revised Phase 2 Goal** |
|--------|----------------------------|------------------------------|--------------------------|
| **p50 Latency** | <50ms | **3.3 ms** | **< 3 ms** |
| **p95 Latency** | <100ms | **4.6 ms** | **< 4 ms** |
| **p99 Latency** | <300ms | **5.6 ms** | **< 5 ms** |
| **Throughput** | 500 msg/s | **50 RPS** (Test Limit) | **> 500 RPS** |

**Analysis:**
- The current test configuration (50 drivers) did not saturate the event bus.
- **Action:** Increase concurrent drivers to **500+** to test RabbitMQ consumer concurrency and database lock contention.

---

### Workload C: Location Updates (Already Optimized)
*Simulating 200 drivers updating location every 5s.*

| Metric | Original Baseline Estimate | **Actual Optimized Result** | **Status** |
|--------|----------------------------|-----------------------------|------------|
| **p50 Latency** | <20ms | **4.2 ms** | ✅ **Exceeded** |
| **p95 Latency** | <50ms | **5.5 ms** | ✅ **Exceeded** |
| **Failures** | 0 | **0** | ✅ **Passed** |

**Analysis:**
- Workload C is fully optimized via the Redis-only strategy. No further action needed for now.

---

## 2. Updated Implementation Plan

### Step 1: Stress Testing (Find the Breaking Point)
The current load (100 users) is handled too easily. We cannot optimize what isn't broken.
1.  **Update `TestConfig.cs`**:
    *   Increase `WorkloadA.ConcurrentUsers` to **500**.
    *   Increase `WorkloadB.ConcurrentDrivers` to **200**.
2.  **Run Stress Tests**: Execute `run-performance-tests.ps1` again.
3.  **Identify Real Bottlenecks**: Watch for:
    *   PostgreSQL Connection Pool exhaustion (Errors).
    *   Redis Timeout exceptions.
    *   RabbitMQ queue buildup.

### Step 2: TripService Optimization (If Bottlenecks Found)
*Focus: Connection Pooling & Efficient Querying*
1.  **Database Context**: Verify `DbContext` pooling is enabled in `Program.cs`.
2.  **Redis Multiplexer**: Ensure `ConnectionMultiplexer` is used as a Singleton and not recreated per request.
3.  **Indexing**: Verify `Drivers` table has geospatial index (if used) or if we are purely relying on Redis.

### Step 3: Consumer Scalability (MassTransit/RabbitMQ)
*Focus: Parallel Event Processing*
1.  **Prefetch Count**: Check RabbitMQ consumer configuration. Increase `PrefetchCount` from default (usually 1) to **16 or 32** to allow parallel message processing.
2.  **Concurrency Limit**: Configure the consumer factory to allow multiple concurrent threads processing messages.

---

## 3. Next Immediate Action

**Run the "Stress Test" Configuration**:
We will modify the test config to load the system with **5x** the current traffic and see actual degradation. Only then can we apply meaningful Phase 2 optimizations.
