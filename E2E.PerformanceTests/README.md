# UIT-GO E2E Performance Tests

End-to-end performance testing suite for Phase 1 baseline and Phase 2 Module A comparison.

## Overview

This test suite measures system performance across three critical workloads:

- **Workload A**: High-volume trip creation (rush hour simulation)
- **Workload B**: Burst of driver accept/decline events
- **Workload C**: High-frequency driver location updates

## Prerequisites

### Required Software

1. **.NET 8.0 SDK or higher**
   ```bash
   dotnet --version  # Should be 8.0.x or higher
   ```

2. **Docker Desktop** (running)
   ```bash
   docker version
   ```

3. **UIT-GO services running**
   ```bash
   docker-compose up -d
   ```

### Verify Services

All services should be healthy before running tests:
- API Gateway: http://localhost:8080
- TripService: http://localhost:5001
- DriverService: http://localhost:5002
- Redis: localhost:6379
- PostgreSQL: localhost:5432
- RabbitMQ: http://localhost:15672

## Quick Start

### Run All Workloads

**Windows (PowerShell)**:
```powershell
.\run-performance-tests.ps1
```

**Linux/macOS**:
```bash
chmod +x run-performance-tests.sh
./run-performance-tests.sh
```

### Run Specific Workload

**Windows**:
```powershell
.\run-performance-tests.ps1 -Workload a    # Trip creation only
.\run-performance-tests.ps1 -Workload b    # Driver responses only
.\run-performance-tests.ps1 -Workload c    # Location updates only
```

**Linux/macOS**:
```bash
./run-performance-tests.sh -w a    # Trip creation only
./run-performance-tests.sh -w b    # Driver responses only
./run-performance-tests.sh -w c    # Location updates only
```

### Manual Execution

```bash
# Build the project
dotnet build E2E.PerformanceTests.csproj -c Release

# Run all workloads
dotnet run -c Release

# Run specific workload
dotnet run -c Release -- a      # Workload A
dotnet run -c Release -- b      # Workload B
dotnet run -c Release -- c      # Workload C
```

## Workload Details

### Workload A: High-Volume Trip Creation

**Scenario**: Simulates rush hour with many simultaneous trip requests

**Default Parameters**:
- Concurrent Users: 100 passengers
- Duration: 60 seconds
- Ramp-up: 10 seconds
- Seeded Drivers: 500

**Customization** (via environment variables):
```bash
export WORKLOAD_A_USERS=200
export WORKLOAD_A_DURATION=120
export WORKLOAD_A_RAMPUP=20
export WORKLOAD_A_DRIVERS=1000
```

**Key Metrics**:
- p50/p90/p99 latency for trip creation
- Success rate (target: >95%)
- Throughput (requests per second)
- Redis CPU and memory usage
- PostgreSQL connection count

---

### Workload B: Burst of Driver Accept/Decline Events

**Scenario**: Many drivers responding to trip assignments simultaneously

**Default Parameters**:
- Concurrent Drivers: 50
- Duration: 30 seconds
- Accept Rate: 70%
- Decline Rate: 30%

**Customization**:
```bash
export WORKLOAD_B_DRIVERS=100
export WORKLOAD_B_DURATION=60
export WORKLOAD_B_ACCEPT_RATE=0.8
```

**Key Metrics**:
- p50/p90/p99 event processing time
- RabbitMQ message throughput
- RabbitMQ peak queue depth
- State consistency after test
- Service CPU usage

---

### Workload C: High-Frequency Driver Location Updates

**Scenario**: Real-world driver movement with frequent GPS updates

**Default Parameters**:
- Concurrent Drivers: 200
- Update Interval: 5 seconds
- Duration: 60 seconds
- Concurrent Trip Creation: 20 trips

**Customization**:
```bash
export WORKLOAD_C_DRIVERS=500
export WORKLOAD_C_INTERVAL=3
export WORKLOAD_C_DURATION=120
export WORKLOAD_C_TRIPS=50
```

**Key Metrics**:
- p50/p90/p99 location update latency
- Update throughput (updates per second)
- Redis memory growth
- Trip creation performance under load
- Redis GEORADIUS query time

## Understanding Results

### Console Output

Each workload prints:
1. **Test configuration** - Parameters and setup info
2. **Real-time progress** - Updates during execution
3. **Results summary** - Key metrics and performance data
4. **Resource utilization** - CPU, memory, queue depths
5. **Export location** - Path to JSON results file

### JSON Result Files

Results are exported to JSON files with timestamp:
```
results_WorkloadA_20251201_143022.json
results_WorkloadB_20251201_143456.json
results_WorkloadC_20251201_144012.json
```

**JSON Structure**:
```json
{
  "workload": "WorkloadA",
  "timestamp": "2025-12-01T14:30:22Z",
  "total_requests": 5000,
  "success_count": 4850,
  "success_rate": 97.0,
  "latency": {
    "p50": 85,
    "p90": 180,
    "p99": 420,
    "mean": 125
  },
  "throughput_rps": 83.33,
  "additional_metrics": { ... }
}
```

### Filling Out the Baseline Template

1. Open `PERFORMANCE_BASELINE_TEMPLATE.md`
2. Run Phase 1 tests
3. Transfer JSON metrics to template
4. Document bottlenecks observed
5. Implement Phase 2 Module A improvements
6. Re-run tests
7. Compare and analyze improvements

## Performance Targets

### Phase 1 Baseline Targets

| Workload | Metric | Target |
|----------|--------|--------|
| **A** | p99 Latency | <500ms |
| **A** | Success Rate | >95% |
| **A** | Throughput | ~50-100 req/s |
| **B** | p99 Processing | <300ms |
| **B** | Message Rate | >500 msg/s |
| **B** | Queue Depth | <100 |
| **C** | p99 Update | <100ms |
| **C** | Throughput | >1000 updates/s |
| **C** | Memory Growth | <50MB |

### Phase 2 Improvement Goals

| Workload | Metric | Goal | Improvement |
|----------|--------|------|-------------|
| **A** | p99 Latency | <300ms | 40% faster |
| **A** | Throughput | ~100/s | 2x |
| **B** | p99 Processing | <200ms | 33% faster |
| **B** | Message Rate | >1000 msg/s | 2x |
| **C** | p99 Update | <80ms | 20% faster |
| **C** | Throughput | >2000/s | 2x |

## Troubleshooting

### Services Not Running

**Error**: `API Gateway not responding`

**Solution**:
```bash
docker-compose up -d
docker ps  # Verify all containers running
```

### Connection Refused

**Error**: `Connection refused to localhost:8080`

**Solution**:
Check if services are using different ports:
```bash
docker ps | grep uit-go
```

Update `TestConfig.cs` with actual ports.

### Out of Memory

**Error**: Redis or services run out of memory during Workload C

**Solution**:
Reduce concurrent drivers:
```bash
export WORKLOAD_C_DRIVERS=100
```

### Timeout Errors

**Error**: Requests timing out during high load

**Solution**:
1. Ensure enough system resources (8GB+ RAM recommended)
2. Close other applications
3. Reduce concurrent load parameters
4. Check `docker stats` for resource bottlenecks

### RabbitMQ Queue Buildup

**Error**: Queue depth keeps growing, tests fail

**Solution**:
1. Check consumer is running: `docker logs uit-go-tripservice-1`
2. Verify RabbitMQ connection in service logs
3. Restart services: `docker-compose restart`

## Advanced Configuration

### Custom API Gateway URL

```bash
export API_GATEWAY_URL=http://192.168.1.100:8080
dotnet run -c Release
```

### Custom Redis Connection

```bash
export REDIS_CONNECTION=redis-cluster.example.com:6379
dotnet run -c Release
```

### Run Tests Against Staging Environment

```bash
export API_GATEWAY_URL=https://staging.uitgo.com
export REDIS_CONNECTION=staging-redis:6379
export POSTGRES_CONNECTION="Host=staging-db;..."
dotnet run -c Release
```

## Monitoring During Tests

### Docker Stats

Monitor resource usage in real-time:
```bash
docker stats
```

### Redis Monitor

Watch Redis commands:
```bash
docker exec -it uit-go-redis-1 redis-cli MONITOR
```

### RabbitMQ Management UI

View queues and throughput:
```
http://localhost:15672
Username: guest
Password: guest
```

### PostgreSQL Connections

```bash
docker exec -it uit-go-postgres-1 psql -U postgres -d tripservice -c "SELECT count(*) FROM pg_stat_activity;"
```

## Best Practices

1. **Clean State Between Runs**
   - Redis is automatically flushed before each workload
   - For database cleanup, restart services:
     ```bash
     docker-compose down -v
     docker-compose up -d
     ```

2. **Stable Environment**
   - Close heavy applications (browsers, IDEs)
   - Disable background updates
   - Use wired network (not WiFi)

3. **Multiple Test Runs**
   - Run each workload 3-5 times
   - Take median results to account for variability
   - Warm up services before official baseline

4. **Document Everything**
   - System specs (CPU, RAM, disk)
   - Docker resource limits
   - Any errors or anomalies observed
   - Environmental factors (other load on system)

## Contributing

When adding new workloads:

1. Create new file in `Workloads/` directory
2. Follow existing naming pattern: `Workload[Letter]_[Name].cs`
3. Export results to JSON with timestamp
4. Update `Program.cs` to register new workload
5. Document parameters in this README
6. Add metrics to baseline template

## Architecture

```
E2E.PerformanceTests/
├── Infrastructure/
│   ├── JwtTokenHelper.cs      # Generate auth tokens
│   ├── RedisHelper.cs         # Redis operations & seeding
│   └── TestConfig.cs          # Configuration & parameters
├── Workloads/
│   ├── WorkloadA_TripCreation.cs
│   ├── WorkloadB_DriverResponses.cs
│   └── WorkloadC_LocationUpdates.cs
├── Program.cs                 # Main entry point
└── E2E.PerformanceTests.csproj

Dependencies:
- NBomber: Load testing framework
- NBomber.Http: HTTP client plugin
- StackExchange.Redis: Redis client
- Npgsql: PostgreSQL client
- RabbitMQ.Client: RabbitMQ client
```

## License

Same as UIT-GO project.

## Support

For issues or questions:
1. Check [PHASE2_SCALABILITY_ANALYSIS.md](../PHASE2_SCALABILITY_ANALYSIS.md) for architectural context
2. Review [TEST_INSTRUCTIONS.md](../TEST_INSTRUCTIONS.md) for general testing guidance
3. Check `docker-compose` logs for service errors
4. Ensure all prerequisites are met

---

**Ready to test?** Run `.\run-performance-tests.ps1` and establish your Phase 1 baseline! 🚀
