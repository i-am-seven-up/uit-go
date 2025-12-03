# Technical Decisions & Trade-offs: Performance Optimization

**Project**: UIT-Go Ride-Hailing Backend
**Date**: December 3, 2025

This document outlines the key technical decisions and architectural trade-offs made to achieve high-performance and scalability for the UIT-Go platform.

---

## 1. "Hot Data" Persistence Strategy (The Redis Trade-off)

### The Challenge
**Workload C (High-Frequency Location Updates)** simulation revealed a critical bottleneck:
- **Scenario**: 200 drivers updating GPS coordinates every 5 seconds.
- **Result**: 90+ "Internal Server Error" failures and p95 latency of **~740ms**.
- **Cause**: Synchronously writing every single GPS update to PostgreSQL exhausted the database connection pool and I/O throughput. Transient data (moving cars) was treating the database like a log stream.

### The Solution: Redis-Only "Hot Path"
We refactored the `UpdateLocationAsync` flow to **bypass PostgreSQL entirely** for high-frequency updates.

- **Old Flow (Slow)**:
  `API -> Redis GEO (Update) -> PostgreSQL (Select + Update) -> Return`
- **New Flow (Fast)**:
  `API -> Redis GEO (Update) -> Return`

### The Trade-off
*   **Gained**:
    *   **Zero Failures**: Error rate dropped from ~4% to 0%.
    *   **Extreme Speed**: Latency dropped from **740ms** to **~5ms** (99% improvement).
    *   **Scalability**: The system can now handle thousands of updates/sec without touching the disk-based DB.
*   **Sacrificed**:
    *   **Strict Persistence**: If Redis crashes/restarts, we lose the *current* location of drivers until their phones send the next update (within 5s).
    *   **Historical Data**: We are not logging every breadcrumb to SQL (which saves massive storage but limits "history replay" features unless we add a background worker later).

---

## 2. NBomber Performance Testing & Safety Checks

### The Challenge
Our custom load-testing framework (NBomber) crashed with `IndexOutOfRangeException` when certain workloads executed too quickly or returned empty statistics sets.

### The Solution: Robust Test Harness
We implemented defensive programming patterns in the test runner (`WorkloadA`, `WorkloadB`, `WorkloadC`).

*   **Null/Empty Checks**: Added logic to gracefully handle missing `StepStats` or `ScenarioStats`.
*   **Fallback Logic**: If detailed step stats are missing, the runner falls back to global scenario stats to ensure reporting continuity.
*   **Result**: The CI/CD pipeline (or local test run) never crashes, providing reliable reports even if a specific metric collector fails.

---

## 3. Asynchronous Event-Driven Architecture (Phase 1 Implementation)

### The Challenge
Coupling the "Trip Creation" (User) logic with "Driver Notification" (Driver) logic would mean that if the notification system is slow, the user waits.

### The Solution: RabbitMQ Decoupling
We utilized RabbitMQ to separate critical user flows from background processing.

*   **User Path (Sync)**: Create Trip -> Save to DB -> Return "Finding Driver". (Fast, <30ms)
*   **System Path (Async)**: Publish `TripCreated` event -> Queue -> DriverService consumes -> Notify Driver.
*   **Benefit**: "Trip Creation" latency remains low and constant (~25ms), regardless of how many drivers need to be notified or how busy the DriverService is.

---

## 4. Redis Geospatial Indexing

### The Challenge
Finding the "nearest N drivers" using standard SQL (`SELECT * FROM drivers WHERE ... calculation ...`) is computationally expensive (O(N)) and slow as the driver table grows.

### The Solution: Redis `GEORADIUS`
We leverage Redis's native Geospatial structures.

*   **O(log N) Efficiency**: Spatial queries are incredibly fast.
*   **Real-Time**: As soon as a driver updates their location (via the Redis-Only path above), they are immediately queryable.
*   **Efficiency**: Queries return only the `DriverId` and `Distance`, minimizing network payload.

---

## 5. Connection Pooling & Configuration Tuning

### The Challenge
NBomber performance tests failed initially due to RabbitMQ and Database connection limits or misconfiguration.

### The Solution
*   **Redis `allowAdmin=true`**: Enabled strictly for the Test Harness to perform `FLUSHALL` and memory monitoring, ensuring clean test states.
*   **Docker Networking**: Configured services to communicate via internal Docker network aliases (`rabbitmq`, `uit-go-redis`) while exposing ports to `localhost` for the Test Runner, ensuring seamless "External Test -> Internal Service" connectivity.

---

**Summary**:
We have transitioned from a "Naive CRUD" architecture to a **High-Performance, Event-Driven, In-Memory First** architecture for critical paths.
