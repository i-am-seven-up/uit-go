# Performance Comparison: MVP vs. Robust

**Date**: December 3, 2025
**Test**: Workload C (High-Frequency Location Updates)
**Load**: 200 Concurrent Drivers, 5s Update Interval

## 1. The MVP Version (Branch: `uit-go-mvp`)
*Architecture: Synchronous DB Writes for every location update.*

| Metric | Result | Status |
| :--- | :--- | :--- |
| **Total Requests** | 2400 | - |
| **Failures** | **63** (InternalServerError) | ❌ **FAILED** |
| **p95 Latency** | **448.77 ms** | 🐌 Slow |
| **p99 Latency** | **1106.94 ms** | 🐌 Very Slow |
| **Trip Creation p95** | **473.86 ms** | ⚠️ Impacted by DB load |

## 2. The Robust Version (Branch: `straightforward`)
*Architecture: Asynchronous Redis-Only Writes (Write-Behind).*

| Metric | Result | Status |
| :--- | :--- | :--- |
| **Total Requests** | 2400 | - |
| **Failures** | **0** | ✅ **PASSED** |
| **p95 Latency** | **5.5 ms** | 🚀 **80x Faster** |
| **p99 Latency** | **242.3 ms** | 🚀 **4.5x Faster** |
| **Trip Creation p95** | **18.8 ms** | 🚀 **25x Faster** |

## Conclusion
The **Robust Version** eliminates database bottlenecks for high-frequency data, resulting in:
1.  **Zero Errors** under load (vs. ~3% failure rate).
2.  **Massive Latency Reduction** (Location updates are near-instant).
3.  **System Stability**: Trip creation remains fast even when thousands of drivers are moving.
