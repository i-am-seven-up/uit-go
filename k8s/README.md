# Complete Kubernetes Replacement for Docker Compose

This directory contains complete Kubernetes manifests to replace `docker-compose.yml`.

## What's Included

### Infrastructure
- **redis.yaml** - Redis cache
- **rabbitmq.yaml** - RabbitMQ message broker
- **postgres.yaml** - 3 PostgreSQL databases (user, trip, driver)

### Microservices
- **user-service.yaml** - User Service
- **driver-service.yaml** - Driver Service (with HPA)
- **trip-service.yaml** - Trip Service
- **api-gateway-deployment.yaml** - API Gateway
- **api-gateway-service.yaml** - API Gateway Service
- **api-gateway-hpa.yaml** - API Gateway Autoscaling

### Configuration
- **namespace.yaml** - uit-go namespace
- **ingress.yaml** - Ingress for external access

### Scripts
- **deploy-complete.ps1** - Deploy everything
- **cleanup.ps1** - Remove everything

---

## Quick Start

### 1. Stop Docker Compose

```powershell
# Stop and remove Docker Compose
docker-compose down
```

### 2. Deploy to Kubernetes

```powershell
cd k8s
.\deploy-complete.ps1
```

This will:
1. Create namespace
2. Deploy Redis, RabbitMQ, PostgreSQL
3. Wait for infrastructure to be ready
4. Deploy all microservices
5. Configure autoscaling
6. Set up ingress

### 3. Access Services

**API Gateway:**
```powershell
kubectl port-forward -n uit-go service/api-gateway 8080:8080
```

Then access: `http://localhost:8080`

**RabbitMQ Management:**
```powershell
kubectl port-forward -n uit-go service/rabbitmq 15672:15672
```

Then access: `http://localhost:15672` (guest/guest)

---

## Monitoring

### Watch Pods
```powershell
kubectl get pods -n uit-go -w
```

### Watch Autoscaling
```powershell
kubectl get hpa -n uit-go -w
```

### View Logs
```powershell
# API Gateway logs
kubectl logs -n uit-go -l app=api-gateway --tail=50 -f

# Driver Service logs
kubectl logs -n uit-go -l app=driver-service --tail=50 -f

# All services
kubectl logs -n uit-go --all-containers=true --tail=50 -f
```

### Check Resources
```powershell
# CPU and Memory usage
kubectl top pods -n uit-go

# Detailed pod info
kubectl describe pod -n uit-go <pod-name>
```

---

## Comparison: Docker Compose vs Kubernetes

| Feature | Docker Compose | Kubernetes |
|---------|---------------|------------|
| **Deployment** | `docker-compose up` | `.\deploy-complete.ps1` |
| **Access** | `localhost:8080` | Port-forward or Ingress |
| **Scaling** | Manual | Automatic (HPA) |
| **High Availability** | Single host | Multi-pod, self-healing |
| **Resource Limits** | None | CPU/Memory limits |
| **Health Checks** | Basic | Liveness + Readiness |
| **Load Balancing** | None | Built-in Service |
| **Cleanup** | `docker-compose down` | `.\cleanup.ps1` |

---

## Resource Allocation

### Infrastructure
- **Redis**: 100m CPU, 128Mi RAM
- **RabbitMQ**: 200m CPU, 256Mi RAM
- **PostgreSQL** (each): 100m CPU, 256Mi RAM

### Services
- **API Gateway**: 500m-2000m CPU, 512Mi-2Gi RAM (3-20 replicas)
- **Driver Service**: 1000m-4000m CPU, 1Gi-4Gi RAM (3-15 replicas)
- **Trip Service**: 200m-2000m CPU, 512Mi-2Gi RAM (2 replicas)
- **User Service**: 100m-1000m CPU, 256Mi-1Gi RAM (2 replicas)

**Total (minimum)**: ~3 CPU cores, 4GB RAM  
**Total (scaled)**: ~50 CPU cores, 50GB RAM

---

## Autoscaling

**API Gateway** scales based on:
- CPU > 70% → Scale up
- Memory > 80% → Scale up
- Min: 3 pods, Max: 20 pods

**Driver Service** scales based on:
- CPU > 70% → Scale up
- Memory > 80% → Scale up
- Min: 3 pods, Max: 15 pods

---

## Troubleshooting

### Pods not starting?
```powershell
kubectl describe pod -n uit-go <pod-name>
kubectl logs -n uit-go <pod-name>
```

### Can't connect to services?
```powershell
# Check services
kubectl get svc -n uit-go

# Check endpoints
kubectl get endpoints -n uit-go
```

### Database connection issues?
```powershell
# Check PostgreSQL pods
kubectl get pods -n uit-go -l app=postgres-user
kubectl logs -n uit-go -l app=postgres-user
```

### Redis connection issues?
```powershell
# Test Redis connection
kubectl exec -it -n uit-go deployment/redis -- redis-cli ping
```

---

## Migration from Docker Compose

### Before (Docker Compose)
```powershell
docker-compose up -d
curl http://localhost:8080/health
```

### After (Kubernetes)
```powershell
.\deploy-complete.ps1
kubectl port-forward -n uit-go service/api-gateway 8080:8080
curl http://localhost:8080/health
```

### Environment Variables

All environment variables from `docker-compose.yml` are now in the K8s manifests:
- Connection strings → ConfigMaps/Env vars
- Service URLs → K8s Service DNS
- Secrets → K8s Secrets (future improvement)

---

## Next Steps

1. ✅ Deploy to K8s
2. ✅ Test all services
3. ✅ Verify autoscaling
4. 🔄 Run performance tests
5. 🔄 Monitor metrics
6. 🔄 Optimize resource limits

---

**Created**: December 4, 2025  
**Purpose**: Complete Docker Compose replacement with Kubernetes
