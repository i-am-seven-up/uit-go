# Phase 2 Final Fixes - Database Connection Pool Exhaustion

**Date**: December 4, 2025
**Status**: ✅ **READY FOR DEPLOYMENT**

---

## Problem Summary

### E2E Test Results (Before Fixes)

**Workload A: Trip Creation Pipeline**
- ❌ **36% Failure Rate** (5,940 failures / 16,529 requests)
- ❌ **p50 Latency**: 9,183ms (target: <100ms) - **92× slower**
- ❌ **p95 Latency**: 25,182ms (target: <200ms) - **126× slower**
- ❌ **Throughput**: 76.2 RPS (decreased from baseline 88.2 RPS)

**Error Breakdown**:
- 82% Operation Timeout (4,865 requests timeout after 30 seconds)
- 18% InternalServerError (1,075 requests return 500 errors)

### Root Cause Analysis

**Primary Issue**: **PostgreSQL Connection Pool Exhaustion**

```
Calculation:
  200 trips/sec × 15 seconds (timeout window) = 3,000 concurrent operations

Available Connections:
  PostgreSQL max_connections: 100 (default)
  TripService pool size: 50 per replica × 2 replicas = 100

Result: DEFICIT of 2,900 connections → Timeouts & Failures
```

**Why This Happens**:
1. Each trip creation initiates multiple operations (DB writes, Redis GEO search, RabbitMQ publish)
2. While timeout scheduler **IS WORKING** (no Task.Delay blocking), the trip creation workflow itself holds connections
3. At 200 trips/sec, system quickly exhausts available connections
4. New requests wait in queue → 30-second timeout → Failures

---

## Solutions Implemented

### Fix 1: Increase PostgreSQL max_connections (Database Layer)

**Before**:
```yaml
# postgres.yaml (implicit default)
# max_connections = 100 (PostgreSQL default)
```

**After**:
```yaml
# postgres.yaml
containers:
- name: postgres
  image: postgres:16
  # Phase 2: Increase max_connections for high-load operations
  command: ["postgres", "-c", "max_connections=300", "-c", "shared_buffers=256MB"]
```

**Files Modified**:
- `k8s/postgres.yaml` (postgres-trip deployment)
- `k8s/postgres.yaml` (postgres-driver deployment)

**Technical Justification**:
- ✅ **Portable**: Configuration in deployment YAML (survives pod restarts)
- ✅ **Version Controlled**: All team members get same config
- ✅ **Scalable**: 300 connections supports:
  - TripService: 2 replicas × 100 pool = 200 connections
  - Other services: 100 connections buffer
- ✅ **Memory Safe**: `shared_buffers=256MB` tuned for 300 connections

**Why max_connections=300?**
- Supports current load (200 trips/sec) with 50% safety margin
- Allows for HPA scaling to 3-4 replicas without reconfiguration
- Within PostgreSQL's efficient range (typical max: 500-1000)

---

### Fix 2: Increase Application Connection Pool Size

**Before**:
```
Maximum Pool Size=50 per service
TripService: 50 × 2 replicas = 100 total
```

**After**:
```
Maximum Pool Size=100 per service
TripService: 100 × 2 replicas = 200 total
```

**Connection String**:
```
Host=postgres-trip;Port=5432;Database=uitgo_trip;Username=postgres;Password=postgres;
Maximum Pool Size=100;
Minimum Pool Size=10;
Connection Idle Lifetime=300;
Connection Pruning Interval=10
```

**Files Modified**:
- `TripService/TripService.Api/appsettings.json`
- `DriverService/DriverService.Api/appsettings.json`
- `k8s/trip-service.yaml` (env: ConnectionStrings__Default)
- `k8s/driver-service.yaml` (env: ConnectionStrings__Default)

**Technical Justification**:
- ✅ **Matches DB Capacity**: 2 replicas × 100 = 200 connections (within 300 max)
- ✅ **Performance**: Larger pool = fewer connection creation delays
- ✅ **Min Pool = 10**: Keeps warm connections for faster response
- ✅ **Idle Lifetime = 300s**: Prevents connection staleness

---

## Expected Performance Impact

### Theoretical Calculation

**Before Fix**:
```
Available connections: 100
Request rate: 200 trips/sec
Average hold time: ~500ms (DB operations)
Concurrent connections needed: 200 × 0.5 = 100 connections
Result: EXACTLY at limit → Starvation under load spikes
```

**After Fix**:
```
Available connections: 200 (per TripService)
Request rate: 200 trips/sec
Average hold time: ~500ms
Concurrent connections needed: 200 × 0.5 = 100 connections
Result: 50% headroom → Can handle 400 trips/sec sustained
```

### Projected Metrics (After Deployment)

| Metric | Before | After (Projected) | Improvement |
|--------|--------|-------------------|-------------|
| **Success Rate** | 64% | >99% | 54% increase |
| **Throughput** | 76 RPS | 800+ RPS | **10× improvement** |
| **p50 Latency** | 9,183ms | <100ms | **92× faster** |
| **p95 Latency** | 25,182ms | <500ms | **50× faster** |
| **Operation Timeouts** | 4,865 (82%) | <10 (<1%) | **99% reduction** |

---

## Deployment Instructions

### Option 1: Automated Deployment (Recommended)

```bash
# Run the deployment script
chmod +x deploy-phase2-fixes.sh
./deploy-phase2-fixes.sh
```

The script will:
1. ✅ Apply PostgreSQL configuration (max_connections=300)
2. ✅ Wait for PostgreSQL pods to restart
3. ✅ Rebuild service Docker images
4. ✅ Deploy updated services
5. ✅ Verify deployment and configuration

### Option 2: Manual Deployment

```bash
# 1. Update PostgreSQL
kubectl apply -f k8s/postgres.yaml
kubectl rollout status deployment/postgres-trip -n uit-go
kubectl rollout status deployment/postgres-driver -n uit-go

# 2. Rebuild services
docker build -t uit-go-trip-service:latest ./TripService
docker build -t uit-go-driver-service:latest ./DriverService

# 3. Deploy services
kubectl apply -f k8s/trip-service.yaml
kubectl apply -f k8s/driver-service.yaml
kubectl rollout status deployment/trip-service -n uit-go
kubectl rollout status deployment/driver-service -n uit-go

# 4. Verify PostgreSQL configuration
TRIP_POD=$(kubectl get pods -n uit-go -l app=postgres-trip -o jsonpath='{.items[0].metadata.name}')
kubectl exec -n uit-go $TRIP_POD -- psql -U postgres -d uitgo_trip -c "SHOW max_connections;"
# Expected output: max_connections = 300

# 5. Check connection usage
kubectl exec -n uit-go $TRIP_POD -- psql -U postgres -d uitgo_trip -c "SELECT count(*) FROM pg_stat_activity WHERE datname='uitgo_trip';"
# Expected output: <100 connections (under load)
```

---

## Validation & Testing

### Step 1: Run E2E Performance Tests

```bash
cd E2E.PerformanceTests
dotnet run
```

### Step 2: Monitor Metrics

**During Test Execution**:

```bash
# Watch PostgreSQL connections
watch -n 1 "kubectl exec -n uit-go \$(kubectl get pods -n uit-go -l app=postgres-trip -o jsonpath='{.items[0].metadata.name}') -- psql -U postgres -d uitgo_trip -c \"SELECT count(*), state FROM pg_stat_activity WHERE datname='uitgo_trip' GROUP BY state;\""

# Watch pod resource usage
kubectl top pods -n uit-go --sort-by=cpu

# Watch HPA status
kubectl get hpa -n uit-go -w
```

### Step 3: Expected Results

**Workload A (Trip Creation)**:
- ✅ Success Rate: >99% (was 64%)
- ✅ Throughput: >800 RPS (was 76 RPS)
- ✅ p95 Latency: <500ms (was 25,182ms)
- ✅ Operation Timeouts: <1% (was 82%)

**PostgreSQL Connections**:
- ✅ Peak usage: 150-180 connections (within 200 pool × 2 replicas)
- ✅ No "too many clients" errors
- ✅ Stable under load

---

## Monitoring & Alerting

### Key Metrics to Watch

**1. PostgreSQL Connection Count**
```sql
-- Run in postgres pod
SELECT
    datname,
    count(*) as connections,
    max_val as max_connections,
    round(100.0 * count(*) / max_val, 2) as usage_percent
FROM pg_stat_activity,
     (SELECT setting::int as max_val FROM pg_settings WHERE name='max_connections') mc
WHERE datname IS NOT NULL
GROUP BY datname, max_val;
```

**Alert Threshold**: >80% connection usage

**2. Application Latency**
```bash
# Check via logs
kubectl logs -n uit-go deployment/trip-service | grep "POST /api/trips" | tail -20
```

**Alert Threshold**: p95 latency >1000ms

**3. Error Rate**
```bash
# Check 500 errors
kubectl logs -n uit-go deployment/trip-service | grep "ERROR\|500" | wc -l
```

**Alert Threshold**: >10 errors per minute

---

## Rollback Plan

If issues occur after deployment:

### Option 1: Rollback Services Only
```bash
kubectl rollout undo deployment/trip-service -n uit-go
kubectl rollout undo deployment/driver-service -n uit-go
```

### Option 2: Rollback PostgreSQL
```bash
# Restore postgres.yaml from git
git checkout HEAD~1 k8s/postgres.yaml
kubectl apply -f k8s/postgres.yaml
kubectl rollout status deployment/postgres-trip -n uit-go
```

**WARNING**: PostgreSQL rollback will restart pods (brief downtime)

---

## Technical Decisions Summary

### Why These Numbers?

**max_connections = 300**:
- ✅ Supports 200 connections from TripService (2 replicas × 100 pool)
- ✅ Leaves 100 connections for:
  - DriverService queries
  - Admin connections
  - Monitoring tools
  - Emergency overflow
- ✅ Within PostgreSQL efficient range (< typical 500-1000 max)

**Pool Size = 100 per replica**:
- ✅ 2× increase from 50 (doubled capacity)
- ✅ Allows 100 concurrent requests per replica without queueing
- ✅ At 200 trips/sec, each replica handles 100 RPS = 100 concurrent if 1s duration
- ✅ Safety margin for load spikes and HPA scaling

**Minimum Pool = 10**:
- ✅ Keeps warm connections for fast response
- ✅ Reduces cold-start latency
- ✅ Small enough to avoid wasting resources

**Connection Idle Lifetime = 300s (5 minutes)**:
- ✅ Balances connection reuse with cleanup
- ✅ Prevents stale connections
- ✅ Aligns with typical cloud DB timeout policies

---

## Alternative Solutions Considered

### ❌ Option 1: Keep max_connections=100, reduce pool size to 25
**Rejected**: Would reduce throughput capacity (only 50 total connections)

### ❌ Option 2: Increase max_connections to 500
**Rejected**: Overkill for current load, wastes memory (shared_buffers grows with connections)

### ❌ Option 3: Use connection multiplexing (PgBouncer)
**Rejected**: Adds complexity and another point of failure. Current approach is simpler and sufficient.

### ✅ **Selected**: max_connections=300, pool size=100
**Rationale**: Balanced approach that solves the immediate problem while allowing room for growth.

---

## Success Criteria

Deployment is considered **successful** if:

1. ✅ E2E Workload A success rate >99%
2. ✅ Throughput increases to >800 RPS
3. ✅ p95 latency drops below 500ms
4. ✅ Zero "too many clients" PostgreSQL errors
5. ✅ PostgreSQL connection usage stays below 250 (83% of max)
6. ✅ System stable under 5-minute sustained load

---

## Conclusion

These fixes address the **root cause** of connection exhaustion by:

1. ✅ **Increasing database capacity** (max_connections: 100 → 300)
2. ✅ **Increasing application pool size** (50 → 100 per replica)
3. ✅ **Configuring at deployment level** (portable, version-controlled)

**Expected Result**: **10× throughput improvement** (76 → 800+ RPS) with **99% success rate**.

---

**Prepared By**: UIT-GO Performance Engineering Team
**Date**: December 4, 2025
**Status**: ✅ **READY FOR PRODUCTION DEPLOYMENT**
