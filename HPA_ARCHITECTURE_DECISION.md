# Decision #6: Horizontal Pod Autoscaler (HPA) Strategy

## Problem Statement

Under high load, fixed replica counts cannot adapt to traffic spikes, leading to:
- Resource waste during low traffic (over-provisioning)
- Performance degradation during high traffic (under-provisioning)
- Manual intervention required for scaling

## Solution: Implement HPAs for Stateless Application Services

### Services Selected for HPA (3 total):

#### 1. **API Gateway HPA** ✅
```yaml
minReplicas: 3
maxReplicas: 20
CPU threshold: 70%
Memory threshold: 80%
Scale up: Aggressive (100% or +4 pods every 15s)
Scale down: Conservative (50% per minute, wait 5 min)
```

**Rationale**:
- Entry point for all HTTP traffic
- Needs to handle traffic bursts
- Stateless → easy to scale
- Critical for system availability

#### 2. **TripService HPA** ✅ [NEWLY ADDED]
```yaml
minReplicas: 2
maxReplicas: 20
CPU threshold: 70%
Memory threshold: 80%
Scale up: Aggressive (100% or +5 pods every 15s)
Scale down: Conservative (50% per minute, wait 5 min)
```

**Rationale**:
- **Orchestrator role** - handles core business logic
- **Highest DB load** - most writes and state transitions
- **Current bottleneck** - 36% failure rate in Workload A
- **Event processing** - consumes driver response events
- **Critical path** - directly impacts user experience
- More aggressive scale up (+5 pods) than gateway due to criticality

#### 3. **DriverService HPA** ✅
```yaml
minReplicas: 3
maxReplicas: 15
CPU threshold: 70%
Memory threshold: 80%
Scale up: Aggressive (100% every 15s)
Scale down: Conservative (50% per minute, wait 5 min)
```

**Rationale**:
- High volume location updates (2,000/sec in Workload C)
- Redis GEO writes under heavy load
- Stateless → easy to scale
- Performance critical for real-time tracking

---

### Services NOT Selected for HPA:

#### ❌ **UserService** - No HPA Needed
**Why not**:
- Low traffic (only login/register)
- Not a bottleneck
- Stateless but minimal load
- **Decision**: Keep static 1-2 replicas

#### ❌ **RabbitMQ** - Should NOT Use HPA
**Why not**:
- Message broker requires stable topology
- Clustering is complex (needs StatefulSet)
- Auto-scaling can cause message loss
- **Decision**: Use StatefulSet with fixed 3 nodes for HA

#### ❌ **Redis** - Should NOT Use HPA
**Why not**:
- In-memory database requires data consistency
- Scaling needs Redis Cluster or Sentinel
- Cannot auto-scale single instance
- **Decision**: Use Redis Cluster (3-6 nodes) or single instance with resource limits

#### ❌ **PostgreSQL** - Should NOT Use HPA
**Why not**:
- Stateful database with persistent storage
- Scaling requires replication (master-slave)
- Cannot auto-scale database like applications
- **Decision**: Use StatefulSet with read replicas if needed

---

## Architecture Decision Matrix

| Service | Stateless? | Bottleneck? | HPA | Scaling Strategy |
|---------|------------|-------------|-----|------------------|
| **API Gateway** | ✅ Yes | ✅ Yes (HTTP) | ✅ Yes | Auto-scale 3-20 |
| **TripService** | ✅ Yes | ✅ Yes (Orchestrator) | ✅ Yes | Auto-scale 2-20 |
| **DriverService** | ✅ Yes | ✅ Yes (Location) | ✅ Yes | Auto-scale 3-15 |
| UserService | ✅ Yes | ❌ No | ❌ No | Static 1-2 replicas |
| RabbitMQ | ❌ No | ❌ No | ❌ No | StatefulSet 3 nodes |
| Redis | ❌ No | 🟡 Maybe | ❌ No | Cluster or single |
| PostgreSQL | ❌ No | 🟡 Maybe | ❌ No | StatefulSet + replicas |

---

## Request Flow Coverage

```
Client Request
    ↓
[API Gateway] ← HPA ✅ (Distribute HTTP traffic)
    ↓
[TripService] ← HPA ✅ (Process business logic)
    ↓
[DriverService] ← HPA ✅ (Handle driver operations)
    ↓
[Redis/PostgreSQL] ← No HPA (Stateful, use replication)
```

**Coverage**: 100% of application layer is auto-scalable

---

## HPA Behavior Configuration

### Scale Up Policy (Aggressive):
- **Why**: Respond quickly to traffic spikes
- **How**: Double pods or add 4-5 pods every 15 seconds
- **Impact**: Prevents performance degradation

### Scale Down Policy (Conservative):
- **Why**: Avoid thrashing and maintain stability
- **How**: Wait 5 minutes, then scale down max 50% per minute
- **Impact**: Smooth scale-down, prevents rapid oscillation

---

## Expected Impact

### Before HPAs:
- Fixed replicas: API Gateway (3), TripService (2), DriverService (3)
- Total: 8 pods
- Cannot handle traffic spikes
- TripService bottleneck: 36% failure rate

### After HPAs:
- Dynamic replicas: 8-55 pods (3+2+3 min → 20+20+15 max)
- Auto-respond to load
- TripService can scale to handle 800+ RPS
- Expected failure rate: <1%

---

## Monitoring Metrics

**HPA Metrics to Monitor**:
```bash
kubectl get hpa -n uit-go --watch
```

**Key Indicators**:
- Current replicas vs desired replicas
- CPU/Memory utilization
- Scale up/down events
- Time to scale

**Alerts**:
- HPA at max replicas (need to increase maxReplicas)
- Frequent scale up/down (thrashing - adjust thresholds)
- HPA unable to scale (resource quota exceeded)

---

## Conclusion

**3 HPAs is the optimal configuration** because:
1. ✅ Covers all stateless application services
2. ✅ Addresses all identified bottlenecks
3. ✅ Follows Kubernetes best practices (only scale stateless)
4. ✅ Provides 100% application layer auto-scaling
5. ✅ Leaves stateful services (DB, cache, queue) with appropriate scaling strategies

**TripService HPA** is the most critical addition because it's the orchestrator handling the core trip creation pipeline that currently has 36% failure rate.
