# E2E Performance Test Failure Analysis

**Test Date**: December 4, 2025 (21:30 - 21:35)  
**Test Duration**: ~5 minutes (all 4 workloads)  
**Overall Status**: ❌ **ALL WORKLOADS FAILED**

---

## Executive Summary

All 4 performance workloads experienced **significant to complete failures** during the test run. The failures are primarily caused by:

1. **Connection Exhaustion** - Port/socket exhaustion under high load
2. **Application Errors** - Internal server errors in trip creation
3. **Code Bugs** - Index out of range in driver response logic
4. **API Issues** - Missing/incorrect endpoints for GEO search

**Critical Finding**: The system is **NOT production-ready** and requires immediate fixes before deployment.

---

## Workload A: Trip Creation - ❌ FAILED (34% Error Rate)

### Test Configuration
- **Concurrent Users**: Ramping from 0 → 200 over 60s, then constant 200/s for 5min, spike to 500/s for 30s
- **Duration**: 1 minute 59 seconds (stopped early)
- **Target Load**: High-volume trip creation simulating rush hour

### Results

| Metric | Value | Status |
|--------|-------|--------|
| **Total Requests** | 15,923 | - |
| **Successful** | 10,501 (66%) | 🟡 Partial |
| **Failed** | 5,422 (34%) | ❌ High failure rate |
| **RPS (Success)** | 88.2 | ⚠️ Below target |
| **RPS (Failure)** | 45.6 | - |

### Latency Metrics

#### Successful Requests
- **p50**: 4,390.91ms (Target: <100ms) ❌ **44x slower**
- **p75**: 6,791.17ms ❌
- **p95**: 12,935.17ms ❌
- **p99**: 13,582.34ms ❌
- **Mean**: 4,919.62ms ❌

#### Failed Requests
- **p50**: 14,409.73ms
- **p95**: 15,679.49ms
- **Mean**: 9,895.99ms

### Root Cause Analysis

#### 1. **InternalServerError (5,422 failures)**
```
Status Code: 500 InternalServerError
Count: 5,422 requests (34% of total)
```

**Likely Causes**:
- Database connection pool exhaustion
- Unhandled exceptions in trip creation logic
- Redis command failures under high load
- Timeout issues in downstream services (DriverService/TripService)

#### 2. **Extreme Latency (4-14 seconds)**
**Why it's slow**:
- **Database bottleneck**: Trip creation involves multiple DB operations (INSERT trip, UPDATE driver status, etc.)
- **Redis GEO search**: Finding nearby drivers under high concurrency
- **Synchronous processing**: Blocking operations in the request pipeline
- **No connection pooling**: Creating new connections for each request

**Comparison with Phase 2 Baseline**:
| Metric | Phase 2 Baseline | Current Test | Degradation |
|--------|------------------|--------------|-------------|
| p50 Latency | 34.05ms | 4,390.91ms | **129x slower** |
| p95 Latency | 58.4ms | 12,935.17ms | **221x slower** |
| RPS | 181.6 | 88.2 | **51% reduction** |
| Error Rate | 0% | 34% | **Complete degradation** |

---

## Workload B: Driver Responses - ❌ FAILED (100% Error Rate)

### Test Configuration
- **Concurrent Drivers**: 50
- **Duration**: 30 seconds
- **Target Load**: 50 requests/second

### Results

| Metric | Value | Status |
|--------|-------|--------|
| **Total Requests** | 1,500 | - |
| **Successful** | 0 (0%) | ❌ **Complete failure** |
| **Failed** | 1,500 (100%) | ❌ **Critical** |
| **RPS (Failure)** | 50 | - |

### Root Cause Analysis

#### **Code Bug: Index Out of Range**
```
Status Code: -101
Error: Index was out of range. Must be non-negative and less than 
       the size of the collection. (Parameter 'index')
Count: 1,500 (100% of requests)
```

**Root Cause**: This is a **code bug** in [WorkloadB_DriverResponses.cs](file:///c:/Users/tophu/source/repos/uit-go/E2E.PerformanceTests/Workloads/WorkloadB_DriverResponses.cs).

**Likely Issue**:
- Accessing an empty list/array without bounds checking
- Trying to select a random driver from an empty driver pool
- Incorrect indexing logic when selecting trips to respond to

**Fix Required**: Review the workload code to ensure:
1. Driver list is properly populated before test starts
2. Bounds checking before accessing collections
3. Proper error handling for empty collections

---

## Workload C: Location Updates - ❌ FAILED (60% Error Rate)

### Test Configuration
- **Concurrent Drivers**: 10,000 drivers
- **Update Rate**: 10,000 updates every 5 seconds
- **Duration**: 15 seconds (stopped early, target was 3 minutes)
- **Target Load**: Hardcore location update stress test

### Results

| Metric | Value | Status |
|--------|-------|--------|
| **Total Requests** | 21,201 | - |
| **Successful** | 8,461 (40%) | ❌ Low success rate |
| **Failed** | 12,740 (60%) | ❌ **Majority failed** |
| **RPS (Success)** | 564.1 | ⚠️ Far below target |
| **RPS (Failure)** | 849.3 | - |

### Latency Metrics

#### Successful Requests
- **p50**: 7,962.62ms (Target: <50ms) ❌ **159x slower**
- **p95**: 9,936.9ms ❌
- **Mean**: 7,433.57ms ❌

#### Failed Requests
- **p50**: 5,148.67ms
- **p95**: 7,819.26ms

### Root Cause Analysis

#### **Connection Refused (12,740 failures)**
```
Status Code: -101
Error: No connection could be made because the target machine 
       actively refused it. (127.0.0.1:80)
Count: 12,740 requests (60% of total)
```

**Root Cause**: **Port 80 is not accepting connections**

**Why it's failing**:
1. **Wrong port configuration**: Tests are trying to connect to `127.0.0.1:80` but the API is likely running on port `8080`
2. **Connection pool exhaustion**: Too many concurrent connections overwhelming the server
3. **Service not running on port 80**: No service listening on the target port

**Comparison with Phase 2 Baseline**:
| Metric | Phase 2 Baseline | Current Test | Degradation |
|--------|------------------|--------------|-------------|
| Concurrent Drivers | 2,000 | 10,000 | 5x scale |
| p50 Latency | 8.13ms | 7,962.62ms | **979x slower** |
| p95 Latency | 16.78ms | 9,936.9ms | **592x slower** |
| Error Rate | 0% | 60% | **Complete degradation** |

---

## Workload D: GEO Search Stress - ❌ FAILED (100% Error Rate)

### Test Configuration
- **Search Rate**: 8,000 searches/second
- **Duration**: 3 seconds (stopped early, target was 2 minutes)
- **Seeded Drivers**: Unknown (test failed before seeding completed)
- **Search Radius**: Configured radius

### Results

| Metric | Value | Status |
|--------|-------|--------|
| **Total Requests** | 17,154 | - |
| **Successful** | 0 (0%) | ❌ **Complete failure** |
| **Failed** | 17,154 (100%) | ❌ **Critical** |
| **RPS (Failure)** | 5,718 | - |

### Root Cause Analysis

#### **1. Socket Exhaustion (7,728 failures - 45%)**
```
Status Code: -101
Error: Only one usage of each socket address (protocol/network 
       address/port) is normally permitted. (127.0.0.1:80)
Count: 7,728 requests (45% of total)
```

**Root Cause**: **Ephemeral port exhaustion**

**Why it's happening**:
- **Too many concurrent connections**: 8,000 requests/second exhausts available ports
- **No connection pooling**: Creating new TCP connections for each request
- **Socket TIME_WAIT state**: Closed sockets remain in TIME_WAIT for 30-120 seconds
- **Windows port limit**: Default ephemeral port range is ~16,000 ports

**Fix Required**:
1. Implement HTTP connection pooling (reuse connections)
2. Increase ephemeral port range in Windows
3. Reduce connection creation rate
4. Use HTTP/2 or connection keep-alive

#### **2. NotFound - 404 Errors (9,426 failures - 55%)**
```
Status Code: 404 NotFound
Count: 9,426 requests (55% of total)
```

**Root Cause**: **API endpoint does not exist or incorrect URL**

**Likely Issues**:
- API endpoint `/api/drivers/nearby` is not implemented
- Incorrect base URL (using port 80 instead of 8080)
- Route not registered in API Gateway
- Service not deployed/running

---

## Critical Issues Summary

### 1. **Port Configuration Error** (Affects: Workload C, D)
- Tests are connecting to `127.0.0.1:80` but services run on port `8080`
- **Fix**: Update [TestConfig.cs](file:///c:/Users/tophu/source/repos/uit-go/E2E.PerformanceTests/Infrastructure/TestConfig.cs) to use correct ports

### 2. **Connection Pool Exhaustion** (Affects: All workloads)
- No HTTP connection pooling implemented
- Each request creates a new TCP connection
- **Fix**: Implement HttpClientFactory with connection pooling

### 3. **Code Bug in Workload B** (Affects: Workload B)
- Index out of range exception
- **Fix**: Add bounds checking in driver selection logic

### 4. **Missing API Endpoint** (Affects: Workload D)
- `/api/drivers/nearby` returns 404
- **Fix**: Verify endpoint exists or update test to use correct endpoint

### 5. **Database/Redis Bottleneck** (Affects: Workload A, C)
- Extreme latency (4-10 seconds vs target <100ms)
- Internal server errors under load
- **Fix**: 
  - Add database connection pooling
  - Optimize Redis queries
  - Add caching layer
  - Implement async processing

---

## Recommended Actions (Priority Order)

### 🔴 **Critical (Fix Immediately)**

1. **Fix port configuration** in TestConfig.cs
   - Change `127.0.0.1:80` → `127.0.0.1:8080`

2. **Fix Workload B index bug**
   - Add bounds checking before accessing driver collections

3. **Verify API endpoints exist**
   - Ensure `/api/drivers/nearby` is implemented and deployed

### 🟡 **High Priority**

4. **Implement HTTP connection pooling**
   - Use `HttpClientFactory` with proper lifetime management
   - Configure max connections per endpoint

5. **Add database connection pooling**
   - Configure connection pool size
   - Add connection timeout handling

6. **Optimize Redis operations**
   - Review GEO partitioning implementation
   - Add Redis connection pooling

### 🟢 **Medium Priority**

7. **Add circuit breakers and retry logic**
   - Implement Polly for resilience
   - Add exponential backoff

8. **Increase system limits**
   - Increase ephemeral port range
   - Tune TCP TIME_WAIT settings

9. **Add monitoring and alerting**
   - Track connection pool metrics
   - Monitor error rates in real-time

---

## Performance Degradation vs Phase 2 Baseline

| Workload | Metric | Phase 2 | Current | Degradation |
|----------|--------|---------|---------|-------------|
| **A: Trip Creation** | p50 Latency | 34.05ms | 4,390.91ms | **129x slower** |
| **A: Trip Creation** | Error Rate | 0% | 34% | **∞ worse** |
| **C: Location Updates** | p50 Latency | 8.13ms | 7,962.62ms | **979x slower** |
| **C: Location Updates** | Error Rate | 0% | 60% | **∞ worse** |

**Conclusion**: The current system is performing **100-1000x worse** than Phase 2 baseline, with catastrophic error rates. This indicates a **major regression** or **configuration issue** that must be resolved before any production deployment.

---

## Test Reports Location

All detailed reports are available in:
- **Workload A**: [2025-12-04_14.30.65_session_71ee452a](file:///c:/Users/tophu/source/repos/uit-go/E2E.PerformanceTests/reports/2025-12-04_14.30.65_session_71ee452a)
- **Workload B**: [2025-12-04_14.33.22_session_8141b713](file:///c:/Users/tophu/source/repos/uit-go/E2E.PerformanceTests/reports/2025-12-04_14.33.22_session_8141b713)
- **Workload C**: [2025-12-04_14.34.59_session_a4624437](file:///c:/Users/tophu/source/repos/uit-go/E2E.PerformanceTests/reports/2025-12-04_14.34.59_session_a4624437)
- **Workload D**: [2025-12-04_14.34.60_session_94632549](file:///c:/Users/tophu/source/repos/uit-go/E2E.PerformanceTests/reports/2025-12-04_14.34.60_session_94632549)
