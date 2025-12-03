# NBomber Load Testing - Complete Guide

## What is NBomber?

NBomber is a modern .NET load testing framework that simulates real-world user behavior and measures system performance under stress. Think of it as a way to simulate thousands of users hitting your API simultaneously.

---

## How NBomber Generates Bulk Requests

### 1. **Scenario Definition** (What to test)
```csharp
var scenario = Scenario.Create("trip_creation", async context =>
{
    // This function runs for EACH simulated user/request
    var passengerId = Guid.NewGuid();
    var token = JwtTokenHelper.GeneratePassengerToken(passengerId);

    // Random locations in HCMC
    var (pickupLat, pickupLng) = TestConfig.HCMCCoordinates.GetRandomLocation();
    var (dropoffLat, dropoffLng) = TestConfig.HCMCCoordinates.GetRandomLocation();

    // Make HTTP POST request
    var request = Http.CreateRequest("POST", $"{TestConfig.ApiGatewayUrl}/api/trips")
        .WithHeader("Authorization", $"Bearer {token}")
        .WithBody(new StringContent(jsonBody, Encoding.UTF8, "application/json"));

    return await Http.Send(httpFactory, request);
})
```

**This lambda function is executed thousands of times in parallel!**

### 2. **Load Simulation** (How many users, when)

NBomber supports multiple load patterns:

#### **Ramping Injection** (Gradual increase)
```csharp
Simulation.RampingInject(
    rate: 100,                           // Target: 100 requests/second
    interval: TimeSpan.FromSeconds(1),   // Inject every 1 second
    during: TimeSpan.FromSeconds(10)     // For 10 seconds
)
```

**What this does:**
- Second 0: 0 requests/sec
- Second 1: 10 requests/sec (ramp up)
- Second 2: 20 requests/sec
- Second 3: 30 requests/sec
- ...
- Second 10: 100 requests/sec ✓

#### **Constant Injection** (Sustained load)
```csharp
Simulation.Inject(
    rate: 100,                           // 100 requests/second
    interval: TimeSpan.FromSeconds(1),   // Every second
    during: TimeSpan.FromSeconds(50)     // For 50 seconds
)
```

**What this does:**
- Maintains exactly 100 requests/second for 50 seconds
- Total: 5000 requests during this phase

### 3. **Combined Load Pattern** (Workload A)

```csharp
.WithLoadSimulations(
    Simulation.RampingInject(100, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)),
    Simulation.Inject(100, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(50))
)
```

**Timeline:**
```
0s ─────────── 10s ────────────────────────────────────── 60s
│  Ramp Up     │        Sustained Load                    │
│  0→100 rps   │        100 rps constant                  │
│  ~550 reqs   │        ~5000 reqs                        │
└──────────────┴──────────────────────────────────────────┘
Total: ~5450 requests
```

---

## What Workload A Actually Does

### **Step-by-Step Execution:**

1. **Setup Phase (Before load test)**
   ```
   ✓ Clean Redis (delete old driver data)
   ✓ Seed 500 drivers into Redis with random HCMC locations
   ✓ Verify all drivers are online
   ```

2. **Load Test Phase (60 seconds)**
   ```
   For each request (5450 total):

   1. Generate new PassengerId (Guid.NewGuid())
   2. Create JWT token for that passenger
   3. Pick random pickup location in HCMC (lat/lng)
   4. Pick random dropoff location in HCMC (lat/lng)
   5. Create JSON request body
   6. POST to /api/trips via API Gateway
   7. Measure response time
   8. Record success/failure

   Repeat 5450 times with increasing concurrency:
     - First 10s: Ramp from 0 to 100 concurrent requests/sec
     - Next 50s: Maintain 100 requests/sec constant
   ```

3. **Analysis Phase (After load test)**
   ```
   ✓ Calculate latency percentiles (p50, p75, p90, p95, p99)
   ✓ Calculate throughput (requests/second)
   ✓ Count successes vs failures
   ✓ Measure Redis memory usage
   ✓ Check remaining available drivers
   ✓ Export results to JSON
   ```

---

## How to View Reports

NBomber generates **4 report formats** for each test run:

### **Report Location:**
```
E2E.PerformanceTests/reports/
└── 2025-12-03_04.37.75_session_54658288/
    ├── nbomber_report_2025-12-03--04-39-05.html  ← Best for viewing
    ├── nbomber_report_2025-12-03--04-39-05.md
    ├── nbomber_report_2025-12-03--04-39-05.txt
    ├── nbomber_report_2025-12-03--04-39-05.csv   ← Best for Excel
    └── nbomber-log-2025120311.txt
```

### **1. HTML Report** ⭐ **RECOMMENDED**

**Best for:** Interactive viewing with charts and graphs

**How to view:**
```bash
# Open in browser (Windows)
start E2E.PerformanceTests/reports/[SESSION_ID]/nbomber_report_*.html

# Or navigate to the file and double-click it
```

**Contains:**
- 📊 Beautiful charts showing latency distribution
- 📈 Request rate over time
- ✅ Success/failure breakdown
- 🎯 Percentile graphs (p50, p75, p95, p99)
- 🔍 Detailed statistics tables

### **2. CSV Report** 📊

**Best for:** Excel analysis, data processing, comparisons

**How to use:**
```bash
# Open in Excel
excel E2E.PerformanceTests/reports/[SESSION_ID]/nbomber_report_*.csv

# Or import into any data analysis tool
```

**CSV Columns:**
```
test_suite, test_name, scenario, duration, step_name,
request_count, ok, failed, ok_rps,
ok_min, ok_mean, ok_max,
ok_50_percent, ok_75_percent, ok_95_percent, ok_99_percent,
ok_std_dev, data_transfer_min_kb, data_transfer_mean_kb,
data_transfer_max_kb, data_transfer_all_mb, ...
```

### **3. Markdown Report** 📝

**Best for:** GitHub, documentation, quick review in IDE

**How to view:**
```bash
cat E2E.PerformanceTests/reports/[SESSION_ID]/nbomber_report_*.md
# Or open in any markdown viewer (VS Code, GitHub, etc.)
```

### **4. Text Report** 📄

**Best for:** Command line, quick terminal review

**How to view:**
```bash
cat E2E.PerformanceTests/reports/[SESSION_ID]/nbomber_report_*.txt
```

---

## Example Report Analysis

### **What the Numbers Mean:**

```
request count: all = 5450, ok = 5450, RPS = 90.8
```
- **all = 5450**: Total requests made
- **ok = 5450**: Successful requests (HTTP 200)
- **RPS = 90.8**: Average throughput (requests per second)

```
latency (ms): min = 15.75, mean = 36.95, max = 240.52, StdDev = 13.48
```
- **min = 15.75ms**: Fastest response
- **mean = 36.95ms**: Average response time
- **max = 240.52ms**: Slowest response
- **StdDev = 13.48**: Consistency (lower = more consistent)

```
latency percentile (ms): p50 = 33.92, p75 = 40.22, p95 = 57.28, p99 = 87.94
```
- **p50 (median)**: 50% of requests completed in ≤33.92ms
- **p75**: 75% of requests completed in ≤40.22ms
- **p95**: 95% of requests completed in ≤57.28ms ⭐ **Important SLA metric**
- **p99**: 99% of requests completed in ≤87.94ms ⭐ **Tail latency**

### **What's Good Performance?**

For a ride-hailing API:
- ✅ **p95 < 100ms**: Excellent (57.28ms in our test)
- ✅ **p99 < 200ms**: Good (87.94ms in our test)
- ✅ **Success rate = 100%**: Perfect
- ✅ **RPS > 80**: Acceptable for this load

---

## Quick View Commands

### **View Latest HTML Report:**
```bash
# Find latest session
LATEST=$(ls -t E2E.PerformanceTests/reports/ | head -1)

# Open HTML report
start "E2E.PerformanceTests/reports/$LATEST/*.html"
```

### **Compare Multiple Runs:**
```bash
# Export all CSV files to one directory
mkdir -p performance-history
cp E2E.PerformanceTests/reports/*/nbomber_report_*.csv performance-history/

# Now you can compare in Excel or create comparison charts
```

### **Quick Stats from Terminal:**
```bash
# View latest text report
cat E2E.PerformanceTests/reports/$(ls -t E2E.PerformanceTests/reports/ | head -1)/nbomber_report_*.txt
```

---

## Understanding Load Simulation Types

| Simulation Type | Use Case | Example |
|----------------|----------|---------|
| **Inject** | Constant load | Black Friday traffic |
| **RampingInject** | Gradual increase | Morning rush hour |
| **InjectRandom** | Unpredictable traffic | Real user behavior |
| **KeepConstant** | Fixed concurrency | Maintain 100 concurrent users |
| **Pause** | Cool-down period | Between test phases |

---

## Custom Metrics Export

Workload A also exports custom JSON with additional metrics:

```json
{
  "workload": "WorkloadA",
  "timestamp": "2025-12-03T04:39:05Z",
  "total_requests": 5450,
  "success_count": 5450,
  "success_rate": 100.0,
  "latency": {
    "p50": 33.92,
    "p75": 40.22,
    "p90": 57.28,
    "p99": 87.94,
    "mean": 36.95,
    "stddev": 13.48
  },
  "throughput_rps": 90.8,
  "additional_metrics": {
    "online_drivers": 500,
    "available_drivers": 500,
    "redis_memory_mb": 12.45
  }
}
```

This is saved in the test directory as `results_WorkloadA_*.json`

---

## Summary

**NBomber simulates load by:**
1. Defining what each "virtual user" does (scenario)
2. Running that scenario thousands of times in parallel
3. Controlling when/how many users hit the system (load simulation)
4. Measuring everything (latency, throughput, errors)
5. Generating detailed reports in multiple formats

**Workload A specifically:**
- Simulates rush hour trip creation
- Ramps up from 0 to 100 requests/sec
- Maintains 100 req/sec for 50 seconds
- Total: ~5450 trip creation requests
- Measures API performance under realistic load
- ✅ Currently achieving 100% success rate with <90ms p99 latency

**To view results:**
- **Best**: Open the HTML file in a browser for interactive charts
- **Excel**: Import the CSV for data analysis
- **Quick**: View MD or TXT files in terminal/editor
