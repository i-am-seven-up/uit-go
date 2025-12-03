# Phase 2 – Module A: Scalability & Performance Analysis

**Project**: UIT-Go Ride-Hailing Backend
**Current State**: Phase 1 Completed
**Objective**: Analyze scalability, establish baseline metrics, and prepare for performance improvements

---

## 1. Architectural Reasoning

### 1.1 Core Architectural Decisions

#### Why Redis GEO for Driver Matching?

**Decision**: Use Redis GEOSPATIAL indexes to store and query driver locations.

**Reasoning**:
- **O(log N) spatial queries**: Redis `GEORADIUS` provides sub-millisecond lookups for nearby drivers
- **In-memory performance**: Critical for real-time matching (target: <100ms trip creation)
- **Atomic operations**: `GEOADD` with TTL ensures stale drivers auto-expire
- **Horizontal scaling**: Redis Cluster can distribute geo data across nodes

**Phase 1 Implementation**:
```
Trip Created → GEORADIUS(pickup, 5km) → Sorted by distance → Lock nearest available driver
```

**Scalability Impact**:
- Can handle 10,000+ driver lookups/second on single Redis instance
- Lookup time remains constant regardless of total drivers (only searches within radius)

---

#### Why RabbitMQ for Service Communication?

**Decision**: Use RabbitMQ for event-driven communication between services.

**Reasoning**:
- **Decoupling**: TripService doesn't need to know DriverService implementation
- **Reliability**: Message persistence ensures no lost events during failures
- **Scalability**: Can add multiple consumers for parallel event processing
- **Backpressure**: Queue depth indicates system overload, preventing cascade failures

**Phase 1 Event Flow**:
```
TripService: Trip Created → [TripOffered] → Queue → DriverService: Notify driver
DriverService: Driver Accepted → [DriverAcceptedTrip] → Queue → TripService: Update state
```

**Scalability Impact**:
- Async processing allows TripService to handle trip creation without waiting for driver notification
- Can scale DriverService consumers independently of TripService

---

#### Why Event-Driven Architecture?

**Decision**: State transitions trigger domain events consumed by other services.

**Reasoning**:
- **Loose coupling**: Services can evolve independently
- **Audit trail**: Event log provides complete trip history
- **Eventual consistency**: Acceptable for non-critical updates (driver notifications)
- **Horizontal scaling**: Multiple service instances can process different events

**Phase 1 State Machine**:
```
Requested → FindingDriver → DriverAssigned → DriverAccepted → DriverOnTheWay →
DriverArrived → InProgress → Completed/Cancelled
```

Each transition publishes events, enabling:
- Real-time notifications
- Analytics processing
- Compensation logic (e.g., cancel releases driver)

---

### 1.2 Architectural Trade-offs

#### Trade-off 1: Synchronous vs Asynchronous Communication

| Aspect | Synchronous (gRPC) | Asynchronous (RabbitMQ) | Phase 1 Choice |
|--------|-------------------|------------------------|----------------|
| **Latency** | Low (1-5ms) | Higher (10-50ms) | **Mixed** |
| **Reliability** | Requires retry logic | Built-in persistence | **Mixed** |
| **Throughput** | Limited by slowest service | Queue absorbs bursts | **Mixed** |
| **Complexity** | Simpler to debug | Harder to trace | **Mixed** |

**Phase 1 Implementation**:
- **Sync**: Trip creation → Driver lookup (must be fast, <100ms)
- **Async**: Trip assignment → Driver notification (can tolerate delay)
- **Sync**: Driver accept → Trip update (user expects immediate feedback)

**Trade-off Decision**:
- Use **sync for critical path** (user-facing operations)
- Use **async for notifications** and non-critical updates
- **Risk**: If RabbitMQ is down, notifications fail but trips still created

**Scalability Impact**:
- Async absorbs traffic spikes (e.g., 1000 trips created in 10 seconds)
- Sync operations can become bottleneck if not properly optimized

---

#### Trade-off 2: Eventual Consistency vs Strong Consistency

| Aspect | Strong Consistency | Eventual Consistency | Phase 1 Choice |
|--------|-------------------|---------------------|----------------|
| **Accuracy** | Always correct | Temporarily stale | **Mixed** |
| **Performance** | Slower (locks/transactions) | Faster (no locks) | **Mixed** |
| **Scalability** | Limited | High | **Mixed** |
| **User Impact** | Predictable | Confusing if too slow | **Mixed** |

**Phase 1 Implementation**:
- **Strong Consistency**: Trip status in PostgreSQL (single source of truth)
- **Eventual Consistency**: Driver availability in Redis (synced via events)
- **Compensation**: Trip cancellation → Event → Redis driver release (eventual)

**Example Scenario**:
```
1. Trip assigned to Driver A (PostgreSQL: AssignedDriverId = A, Redis: driver:A = locked)
2. Trip cancelled (PostgreSQL: Status = Cancelled)
3. [TripCancelled] event published
4. 100ms delay (RabbitMQ processing)
5. Redis updated (driver:A = available)
```

**Trade-off Decision**:
- **Strong consistency for money/safety** (trip status, payment)
- **Eventual consistency for performance** (driver availability, location)
- **Risk**: Driver might appear unavailable for 100-500ms after trip cancellation

**Scalability Impact**:
- PostgreSQL write throughput limited (1000-5000 writes/sec per instance)
- Redis write throughput higher (50,000+ writes/sec)
- Can scale reads with replicas, but writes remain bottleneck

---

#### Trade-off 3: Memory (Redis) vs Persistent Storage (PostgreSQL)

| Aspect | In-Memory (Redis) | Disk-Based (PostgreSQL) | Phase 1 Choice |
|--------|------------------|------------------------|----------------|
| **Speed** | Sub-millisecond | 1-10ms | **Mixed** |
| **Cost** | Expensive (RAM) | Cheaper (SSD) | **Mixed** |
| **Durability** | Volatile (if no persistence) | Durable | **Mixed** |
| **Scalability** | Horizontal (Cluster) | Vertical (larger instance) | **Mixed** |

**Phase 1 Data Distribution**:
- **Redis**:
  - Driver locations (GEO index) - ephemeral, high-write
  - Driver availability (Hash) - ephemeral, high-read
  - Driver locks (String with TTL) - temporary, critical
- **PostgreSQL**:
  - Trip records - permanent, audit trail
  - Driver profiles - permanent, infrequent updates
  - User accounts - permanent, infrequent updates

**Trade-off Decision**:
- **Hot data in Redis** (driver locations updated every 5-10 seconds)
- **Cold data in PostgreSQL** (trip history, user profiles)
- **No dual-write** - Redis is cache, PostgreSQL is source of truth

**Scalability Impact**:
- Redis memory usage: ~1KB per driver (10,000 drivers = 10MB)
- PostgreSQL disk usage: ~2KB per trip (1M trips = 2GB)
- Redis can handle 10x more writes than PostgreSQL

---

### 1.3 How Phase 1 Lifecycle Enables Scalability

#### State Machine Benefits

**Current Implementation**:
```csharp
Requested → FindingDriver → DriverAssigned → DriverAccepted →
DriverOnTheWay → DriverArrived → InProgress → Completed
```

**Scalability Advantages**:

1. **Idempotent State Transitions**
   - Each state change is atomic (database transaction)
   - Failed transitions can be retried safely
   - Enables horizontal scaling of TripService instances

2. **Decoupled Driver Matching**
   - `FindingDriver` state allows async driver search
   - TripService returns immediately to user
   - Driver matching happens in background
   - Can scale matching service independently

3. **Retry Logic Built-in**
   - `DriverAssigned` → `DriverDeclined` → Back to `FindingDriver`
   - Retry count tracked in database
   - Max 3 retries before `NoDriverAvailable`
   - Prevents infinite loops in high-load scenarios

4. **Compensation Events**
   - Each state publishes events
   - Failed operations trigger compensating events
   - Example: `TripCancelled` → `ReleaseDriver` event
   - Enables eventual consistency without distributed transactions

#### High-Frequency Location Updates

**Current Implementation**:
```
Driver App → POST /api/drivers/location → Redis GEOADD
Frequency: Every 5-10 seconds per driver
```

**Scalability Design**:

1. **Write Amplification Prevention**
   - Location updates go ONLY to Redis (not PostgreSQL)
   - No historical location tracking (reduces storage)
   - TTL ensures stale entries auto-expire

2. **Read Optimization**
   - Trip creation queries Redis (no database join)
   - GEORADIUS returns only nearby drivers (not all drivers)
   - Result sorted by distance (nearest first)

3. **Lock Mechanism**
   - Driver lock stored in Redis with TTL (60 seconds)
   - Prevents multiple trips assigning same driver
   - Automatic lock release on TTL expiry (failsafe)

**Expected Load**:
- 10,000 online drivers × 1 update/10 sec = 1,000 writes/sec
- 100 trips/sec × 1 GEORADIUS query = 100 reads/sec
- Redis can handle this on single instance

---

## 2. Performance Testing Workloads

### Workload A: High-Volume Trip Creation

**Scenario**: Simulate rush hour with many simultaneous trip requests.

**Test Parameters** (Local Testing):
```
Concurrent Users: 100 passengers
Duration: 60 seconds
Ramp-up: 10 seconds (0 → 100 users)
Request Rate: ~50-100 trips/sec

Prerequisites:
- 500 drivers online (seeded in Redis)
- Drivers distributed across HCMC coordinates
- All drivers available (not on trips)
```

**Test Steps**:
1. Seed Redis with 500 drivers at random locations
2. Generate 100 passenger tokens
3. Each passenger creates 1 trip with random pickup/dropoff
4. Measure end-to-end latency (API call → Driver assigned)

**Key Performance Indicators (KPIs)**:

| Metric | Target (Phase 1) | Measured | Phase 2 Goal |
|--------|-----------------|----------|--------------|
| **p50 Latency** | <100ms | ___ ms | <80ms |
| **p90 Latency** | <200ms | ___ ms | <150ms |
| **p99 Latency** | <500ms | ___ ms | <300ms |
| **Success Rate** | >95% | ___ % | >98% |
| **TripService CPU** | <70% | ___ % | <50% |
| **Redis CPU** | <50% | ___ % | <40% |
| **PostgreSQL Connections** | <100 | ___ | <80 |
| **RabbitMQ Queue Depth** | <500 | ___ | <300 |

**Potential Bottlenecks**:
- Redis `GEORADIUS` query time with 500 drivers
- PostgreSQL `INSERT` transaction rate
- Driver lock contention (multiple trips, same driver)

---

### Workload B: Burst of Driver Accept/Decline Events

**Scenario**: Simulate many drivers responding to trip assignments simultaneously.

**Test Parameters** (Local Testing):
```
Concurrent Drivers: 50
Duration: 30 seconds
Event Mix: 70% Accept, 30% Decline
Request Rate: ~100 events/sec

Prerequisites:
- 50 trips created (status: DriverAssigned)
- Each trip has different assigned driver
- Drivers distributed geographically
```

**Test Steps**:
1. Create 50 trips in `DriverAssigned` state
2. Generate 50 driver tokens (matching assigned drivers)
3. 70% call `/api/drivers/trips/{id}/accept`
4. 30% call `/api/drivers/trips/{id}/decline`
5. Measure event processing latency

**Key Performance Indicators (KPIs)**:

| Metric | Target (Phase 1) | Measured | Phase 2 Goal |
|--------|-----------------|----------|--------------|
| **p50 Event Processing** | <50ms | ___ ms | <30ms |
| **p90 Event Processing** | <100ms | ___ ms | <80ms |
| **p99 Event Processing** | <300ms | ___ ms | <200ms |
| **RabbitMQ Publish Rate** | >500 msg/s | ___ | >1000 msg/s |
| **RabbitMQ Consumer Rate** | >500 msg/s | ___ | >1000 msg/s |
| **RabbitMQ Queue Depth (Peak)** | <100 | ___ | <50 |
| **TripService CPU** | <60% | ___ % | <40% |
| **DriverService CPU** | <60% | ___ % | <40% |
| **State Consistency** | 100% | ___ % | 100% |

**Verification**:
- All accepted trips → Status = `DriverAccepted`
- All declined trips → Status = `FindingDriver` or `DriverAssigned` (retry)
- No trips stuck in `DriverAssigned` after 5 seconds

**Potential Bottlenecks**:
- RabbitMQ message processing delay
- TripService event consumer thread pool
- PostgreSQL UPDATE transaction rate
- Network latency between services

---

### Workload C: High-Frequency Driver Location Updates

**Scenario**: Simulate real-world driver movement with frequent GPS updates.

**Test Parameters** (Local Testing):
```
Concurrent Drivers: 200
Update Frequency: Every 5 seconds per driver
Duration: 60 seconds
Total Updates: 200 × 12 = 2,400 updates

Prerequisites:
- 200 driver accounts created
- Drivers moving along simulated routes
- Location updates within HCMC bounds
```

**Test Steps**:
1. Generate 200 driver tokens
2. Each driver updates location every 5 seconds
3. Simulate movement (random walk from starting position)
4. Concurrent trip creation (20 trips during test)
5. Measure update latency and query performance

**Key Performance Indicators (KPIs)**:

| Metric | Target (Phase 1) | Measured | Phase 2 Goal |
|--------|-----------------|----------|--------------|
| **p50 Update Latency** | <20ms | ___ ms | <15ms |
| **p90 Update Latency** | <50ms | ___ ms | <30ms |
| **p99 Update Latency** | <100ms | ___ ms | <80ms |
| **Redis Memory Usage** | <50MB | ___ MB | <100MB (ok to grow) |
| **Redis CPU** | <40% | ___ % | <30% |
| **GEO Query Time (p50)** | <5ms | ___ ms | <3ms |
| **GEO Query Time (p99)** | <20ms | ___ ms | <15ms |
| **Update Throughput** | >1000/sec | ___ | >2000/sec |
| **DriverService CPU** | <50% | ___ % | <40% |

**Concurrent Trip Creation**:
- While location updates running, create 20 trips
- Measure trip creation latency with high Redis load
- Verify correct driver matching despite frequent updates

**Potential Bottlenecks**:
- Redis `GEOADD` write throughput
- Redis `GEORADIUS` read performance under write load
- DriverService API throughput
- Network bandwidth to Redis

---

## 3. Testing Infrastructure

### 3.1 Test Environment Setup

**Local Testing (Minimal Resources)**:
```yaml
Services:
  - TripService: 1 instance (2 CPU, 2GB RAM)
  - DriverService: 1 instance (2 CPU, 2GB RAM)
  - UserService: 1 instance (2 CPU, 2GB RAM)
  - PostgreSQL: 1 instance (2 CPU, 4GB RAM)
  - Redis: 1 instance (1 CPU, 2GB RAM)
  - RabbitMQ: 1 instance (1 CPU, 2GB RAM)

Total: 9 CPU, 15GB RAM (can run on modern laptop)
```

**Test Tools**:
- **Load Generator**: NBomber (C# load testing framework)
- **Metrics Collection**: Prometheus + Grafana (optional for visualization)
- **Result Export**: JSON/CSV for comparison

---

### 3.2 Pre-Test Checklist

**Before Running Workloads**:
- [ ] All services running (`docker-compose up -d`)
- [ ] Services healthy (check health endpoints)
- [ ] Redis empty (`redis-cli FLUSHALL`)
- [ ] Databases migrated
- [ ] RabbitMQ queues empty
- [ ] No background load

**During Test**:
- [ ] Monitor CPU/Memory via `docker stats`
- [ ] Monitor RabbitMQ queue depth via Management UI
- [ ] Monitor PostgreSQL connections
- [ ] Check logs for errors

**Post-Test**:
- [ ] Export metrics to JSON
- [ ] Save logs for analysis
- [ ] Clean up test data
- [ ] Document any failures

---

### 3.3 Baseline Metrics Template

**Phase 1 Baseline** (to be filled after running tests):

```
=== WORKLOAD A: Trip Creation ===
p50 Latency: ___ ms
p90 Latency: ___ ms
p99 Latency: ___ ms
Success Rate: ___ %
TripService CPU: ___ %
Redis CPU: ___ %

=== WORKLOAD B: Driver Responses ===
p50 Event Processing: ___ ms
p90 Event Processing: ___ ms
p99 Event Processing: ___ ms
RabbitMQ Queue Depth (Peak): ___
State Consistency: ___ %

=== WORKLOAD C: Location Updates ===
p50 Update Latency: ___ ms
p90 Update Latency: ___ ms
p99 Update Latency: ___ ms
Redis Memory: ___ MB
Update Throughput: ___ /sec
```

**Phase 2 Results** (after Module A implementation):
```
[Same metrics structure for comparison]
```

---

## 4. Success Criteria

### Phase 1 → Phase 2 Improvement Targets

**Latency Improvements**:
- Trip creation p99: 500ms → 300ms (40% reduction)
- Event processing p99: 300ms → 200ms (33% reduction)
- Location update p99: 100ms → 80ms (20% reduction)

**Throughput Improvements**:
- Trip creation: 50/sec → 100/sec (2x)
- Message processing: 500 msg/s → 1000 msg/s (2x)
- Location updates: 1000/sec → 2000/sec (2x)

**Resource Efficiency**:
- CPU usage: -20% (same load, less CPU)
- Memory: +20% acceptable (caching trade-off)
- Database connections: -20% (connection pooling)

**Reliability**:
- Success rate: 95% → 98%
- Zero data loss during load tests
- No service crashes under load

---

## 5. Next Steps

1. **Run Baseline Tests** (Phase 1)
   - Execute all 3 workloads
   - Record metrics in template
   - Identify bottlenecks

2. **Analyze Results**
   - Which workload performs worst?
   - What is the primary bottleneck?
   - Where should optimization focus?

3. **Implement Phase 2 Module A**
   - Apply scalability improvements
   - Based on baseline findings

4. **Re-run Tests** (Phase 2)
   - Execute same workloads
   - Record improved metrics
   - Compare with baseline

5. **Document Improvements**
   - What changed?
   - Why did performance improve?
   - What trade-offs were made?

---

## Appendix: Measurement Commands

### Check Redis Performance
```bash
# Monitor Redis operations
redis-cli --latency-history

# Check memory usage
redis-cli INFO memory

# Monitor commands
redis-cli MONITOR
```

### Check PostgreSQL Performance
```sql
-- Active connections
SELECT count(*) FROM pg_stat_activity;

-- Slow queries
SELECT query, mean_exec_time
FROM pg_stat_statements
ORDER BY mean_exec_time DESC
LIMIT 10;
```

### Check RabbitMQ Performance
```bash
# Queue depth
rabbitmqctl list_queues name messages

# Consumer rates
rabbitmqctl list_queues name messages_ready consumers
```

### Check Service Resources
```bash
# Real-time CPU/Memory
docker stats

# Container logs
docker logs uit-go-tripservice-1 --tail 100
```

---

**Document Version**: 1.0
**Last Updated**: 2025-12-01
**Status**: Ready for Phase 1 Baseline Testing
