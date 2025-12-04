# 🚀 UIT-GO Deployment Guide - Complete Setup

Hướng dẫn deploy toàn bộ hệ thống UIT-GO lên máy mới từ đầu đến cuối.

---

## 📋 Prerequisites (Yêu Cầu Hệ Thống)

### 1. **Phần Mềm Cần Cài**

#### Windows:
```powershell
# 1. Docker Desktop (bắt buộc)
# Download: https://www.docker.com/products/docker-desktop/
# - Enable Kubernetes trong Docker Desktop settings
# - Allocate resources: 8GB RAM, 4 CPUs minimum

# 2. .NET 8 SDK
# Download: https://dotnet.microsoft.com/download/dotnet/8.0
dotnet --version  # Verify: 8.0.x

# 3. Git
# Download: https://git-scm.com/downloads
git --version

# 4. PowerShell 7+ (recommended)
# Download: https://github.com/PowerShell/PowerShell/releases
$PSVersionTable.PSVersion  # Verify: 7.x
```

#### Linux/Mac:
```bash
# 1. Docker Desktop hoặc Docker Engine + kubectl
docker --version
kubectl version --client

# 2. .NET 8 SDK
dotnet --version

# 3. Git
git --version
```

### 2. **Hardware Requirements**

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| RAM | 8 GB | 16 GB |
| CPU | 4 cores | 8 cores |
| Disk | 20 GB free | 50 GB free |
| Network | Stable internet | High-speed |

---

## 🔧 Step-by-Step Deployment

### Step 1: Clone Repository

```powershell
# Clone project
git clone https://github.com/i-am-seven-up/uit-go.git
cd uit-go

# Checkout production-ready branch
git checkout new-module-A-phase-2-upgradation
```

### Step 2: Verify Docker Desktop & Kubernetes

```powershell
# Check Docker is running
docker ps

# Check Kubernetes is enabled
kubectl cluster-info

# Expected output:
# Kubernetes control plane is running at https://kubernetes.docker.internal:6443
```

**Nếu Kubernetes chưa enable**:
1. Mở Docker Desktop
2. Settings → Kubernetes
3. ✅ Enable Kubernetes
4. Click "Apply & Restart"
5. Đợi 2-3 phút

### Step 3: Setup Kubernetes Add-ons (CRITICAL)

**Ingress NGINX Controller** và **Metrics Server** phải cài TRƯỚC khi deploy application!

#### 3.1: Install Ingress NGINX Controller

```powershell
# Apply Ingress NGINX Controller
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.8.1/deploy/static/provider/cloud/deploy.yaml

# Wait for ingress controller to be ready (~1-2 minutes)
kubectl wait --namespace ingress-nginx `
  --for=condition=ready pod `
  --selector=app.kubernetes.io/component=controller `
  --timeout=120s

# Verify
kubectl get pods -n ingress-nginx
```

**Expected output**:
```
NAME                                       READY   STATUS    RESTARTS   AGE
ingress-nginx-controller-xxx               1/1     Running   0          2m
```

#### 3.2: Install Metrics Server (for HPA)

```powershell
# Download metrics server manifest
Invoke-WebRequest -Uri "https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml" -OutFile "metrics-server.yaml"

# Edit for Docker Desktop (add --kubelet-insecure-tls flag)
# Open metrics-server.yaml and find the Deployment section
# Add to container args:
#   - --kubelet-insecure-tls

# Or use this one-liner to patch:
(Get-Content metrics-server.yaml) -replace '        - --cert-dir=/tmp', "        - --cert-dir=/tmp`n        - --kubelet-insecure-tls" | Set-Content metrics-server.yaml

# Apply
kubectl apply -f metrics-server.yaml

# Wait for metrics server to be ready
kubectl wait --namespace kube-system `
  --for=condition=ready pod `
  --selector=k8s-app=metrics-server `
  --timeout=120s

# Verify (may take 1-2 minutes to start collecting metrics)
kubectl top nodes
```

**Expected output** (after 1-2 minutes):
```
NAME             CPU(cores)   CPU%   MEMORY(bytes)   MEMORY%
docker-desktop   500m         6%     3000Mi          40%
```

**Nếu lỗi "Metrics API not available"**:
```powershell
# Wait a bit longer
Start-Sleep -Seconds 60
kubectl top nodes

# Check metrics server logs
kubectl logs -n kube-system -l k8s-app=metrics-server
```

#### 3.3: Verify Add-ons

```powershell
# Check Ingress NGINX
kubectl get pods -n ingress-nginx
kubectl get svc -n ingress-nginx

# Check Metrics Server
kubectl get deployment -n kube-system metrics-server
kubectl top nodes

# Both should be Running/Working
```

**⚠️ QUAN TRỌNG**: Nếu không cài 2 add-ons này:
- Ingress sẽ không hoạt động (không access được qua domain)
- HPAs sẽ không scale (metrics không available)

---

### Step 4: Build Docker Images

```powershell
# Build all service images (mất ~5-10 phút)
docker build -t uit-go-user-service:latest -f deploy/docker/UserService.Dockerfile .
docker build -t uit-go-trip-service:latest -f deploy/docker/TripService.Dockerfile .
docker build -t uit-go-driver-service:latest -f deploy/docker/DriverService.Dockerfile .
docker build -t uit-go-api-gateway:latest -f deploy/docker/ApiGateway.Dockerfile .

# Verify images
docker images | grep uit-go
```

**Expected output**:
```
uit-go-api-gateway      latest    xxx    2 minutes ago    250MB
uit-go-driver-service   latest    xxx    3 minutes ago    280MB
uit-go-trip-service     latest    xxx    4 minutes ago    290MB
uit-go-user-service     latest    xxx    5 minutes ago    270MB
```

### Step 5: Deploy Infrastructure to Kubernetes

```powershell
# Navigate to k8s directory
cd k8s

# 1. Create namespace
kubectl apply -f namespace.yaml

# 2. Deploy databases (PostgreSQL x3)
kubectl apply -f postgres.yaml

# 3. Deploy Redis
kubectl apply -f redis.yaml

# 4. Deploy RabbitMQ
kubectl apply -f rabbitmq.yaml

# Wait for infrastructure to be ready (~2-3 minutes)
kubectl get pods -n uit-go --watch
# Press Ctrl+C when all pods are Running
```

**Verify infrastructure**:
```powershell
kubectl get pods -n uit-go

# Expected output (all Running):
# NAME                              READY   STATUS    RESTARTS   AGE
# postgres-driver-xxx               1/1     Running   0          2m
# postgres-trip-xxx                 1/1     Running   0          2m
# postgres-user-xxx                 1/1     Running   0          2m
# rabbitmq-xxx                      1/1     Running   0          2m
# redis-xxx                         1/1     Running   0          2m
```

### Step 6: Deploy Application Services

```powershell
# Still in k8s/ directory

# 1. Deploy UserService
kubectl apply -f user-service.yaml

# 2. Deploy TripService
kubectl apply -f trip-service.yaml

# 3. Deploy DriverService
kubectl apply -f driver-service.yaml

# 4. Deploy API Gateway
kubectl apply -f api-gateway-deployment.yaml
kubectl apply -f api-gateway-service.yaml

# 5. Deploy HPAs (Horizontal Pod Autoscalers)
kubectl apply -f api-gateway-hpa.yaml
kubectl apply -f trip-service-hpa.yaml
# DriverService HPA is embedded in driver-service.yaml

# 6. Deploy Ingress
kubectl apply -f ingress.yaml

# Wait for services to be ready (~2-3 minutes)
kubectl get pods -n uit-go --watch
```

**Verify services**:
```powershell
kubectl get pods -n uit-go
kubectl get svc -n uit-go
kubectl get hpa -n uit-go

# All pods should be Running
# HPAs should show TARGETS (may take 1-2 min to populate metrics)
```

### Step 7: Verify Deployment

```powershell
# Check all resources
kubectl get all -n uit-go

# Check HPAs
kubectl get hpa -n uit-go
# Expected: 3 HPAs (api-gateway, trip-service, driver-service)

# Check ingress
kubectl get ingress -n uit-go
```

### Step 8: Access the Application

#### Option A: Via Ingress (Recommended)
```powershell
# Add to hosts file (Windows: C:\Windows\System32\drivers\etc\hosts)
# Add line:
127.0.0.1 api.uit-go.com

# Test
curl http://api.uit-go.com/health
```

#### Option B: Via Port Forward
```powershell
# Forward API Gateway port
kubectl port-forward -n uit-go svc/api-gateway 8080:8080

# Test
curl http://localhost:8080/health
```

---

## 🧪 Running Tests

### Integration Tests

```powershell
# Navigate to integration tests
cd TripService/TripService.IntegrationTests

# Setup port forwarding (in separate terminal)
.\setup-test-ports.ps1

# Run tests (in another terminal)
dotnet test --logger "console;verbosity=normal"
```

**Expected**: Some tests may fail due to missing mock infrastructure (known issue)

### E2E Performance Tests

```powershell
# Navigate to E2E tests
cd E2E.PerformanceTests

# Run all workloads
dotnet run

# Or run specific workload
dotnet run -- --workload A  # Trip Creation
dotnet run -- --workload B  # Driver Responses
dotnet run -- --workload C  # Location Updates
dotnet run -- --workload D  # GEO Search Stress
```

**Expected Results** (với máy đủ mạnh):
- Workload A: ~64% success (waiting for Redis scheduler deployment)
- Workload B: 100% success ✅
- Workload C: 99.996% success ✅
- Workload D: 100% success ✅

---

## 📊 Monitoring & Verification

### Check System Health

```powershell
# All pods status
kubectl get pods -n uit-go

# HPA status
kubectl get hpa -n uit-go

# Resource usage
kubectl top pods -n uit-go
kubectl top nodes

# Logs
kubectl logs -n uit-go -l app=trip-service --tail=50
kubectl logs -n uit-go -l app=driver-service --tail=50
```

### Test API Endpoints

```powershell
# Health check
curl http://localhost:8080/health

# User registration
curl -X POST http://localhost:8080/api/users/register `
  -H "Content-Type: application/json" `
  -d '{"email":"test@test.com","password":"Test123!","role":"passenger"}'

# User login
curl -X POST http://localhost:8080/api/users/login `
  -H "Content-Type: application/json" `
  -d '{"email":"test@test.com","password":"Test123!"}'
```

---

## 🔄 Update/Redeploy

### Update Service Code

```powershell
# 1. Rebuild image
docker build -t uit-go-trip-service:latest -f deploy/docker/TripService.Dockerfile .

# 2. Restart deployment
kubectl rollout restart deployment/trip-service -n uit-go

# 3. Wait for rollout
kubectl rollout status deployment/trip-service -n uit-go
```

### Apply Configuration Changes

```powershell
# Edit config file (e.g., k8s/trip-service.yaml)
# Then apply:
kubectl apply -f k8s/trip-service.yaml

# Verify
kubectl get pods -n uit-go
```

---

## 🧹 Cleanup

### Remove All Resources

```powershell
# Delete namespace (removes everything)
kubectl delete namespace uit-go

# Verify
kubectl get all -n uit-go
# Should return: No resources found
```

### Remove Docker Images

```powershell
# Remove UIT-GO images
docker rmi uit-go-user-service:latest
docker rmi uit-go-trip-service:latest
docker rmi uit-go-driver-service:latest
docker rmi uit-go-api-gateway:latest

# Clean up unused images
docker system prune -a
```

---

## 🐛 Troubleshooting

### Issue: Pods stuck in "Pending"

**Cause**: Insufficient resources

**Solution**:
```powershell
# Check resource allocation
kubectl describe node docker-desktop

# Increase Docker Desktop resources:
# Docker Desktop → Settings → Resources
# - Memory: 8GB minimum
# - CPUs: 4 minimum
```

### Issue: Pods stuck in "CrashLoopBackOff"

**Cause**: Application error or missing dependencies

**Solution**:
```powershell
# Check logs
kubectl logs -n uit-go <pod-name>

# Describe pod for events
kubectl describe pod -n uit-go <pod-name>

# Common fixes:
# 1. Verify database is ready
kubectl get pods -n uit-go -l app=postgres-trip

# 2. Check connection strings in deployment yaml
# 3. Verify image was built correctly
docker images | grep uit-go
```

### Issue: "ImagePullBackOff"

**Cause**: Image not found locally

**Solution**:
```powershell
# Verify images exist
docker images | grep uit-go

# If missing, rebuild:
docker build -t uit-go-trip-service:latest -f deploy/docker/TripService.Dockerfile .

# Verify imagePullPolicy in yaml is "Never" for local images
```

### Issue: Cannot access via localhost:8080

**Cause**: Port forwarding not setup or ingress not working

**Solution**:
```powershell
# Option 1: Use port-forward
kubectl port-forward -n uit-go svc/api-gateway 8080:8080

# Option 2: Check ingress
kubectl get ingress -n uit-go
kubectl describe ingress -n uit-go uit-go-ingress

# Option 3: Access via NodePort
kubectl get svc -n uit-go api-gateway
# Use the NodePort shown (e.g., 30080)
curl http://localhost:30080/health
```

### Issue: HPAs not scaling

**Cause**: Metrics server not available or no load

**Solution**:
```powershell
# Check HPA status
kubectl get hpa -n uit-go
kubectl describe hpa -n uit-go trip-service-hpa

# Metrics server may not be available in Docker Desktop
# HPAs will work but metrics may show <unknown>
# This is OK for local development

# To test scaling, generate load:
cd E2E.PerformanceTests
dotnet run -- --workload A
```

---

## 📚 Additional Resources

### Project Documentation
- `README.md` - Project overview
- `PHASE_1_COMPLETED.md` - Phase 1 features
- `PHASE2_TECHNICAL_DECISIONS.md` - Phase 2 optimizations
- `GROWTH_JOURNAL_PERFORMANCE_OPTIMIZATION.md` - Complete journey
- `HPA_ARCHITECTURE_DECISION.md` - HPA strategy

### Kubernetes Resources
- [Kubernetes Documentation](https://kubernetes.io/docs/)
- [Docker Desktop Kubernetes](https://docs.docker.com/desktop/kubernetes/)
- [kubectl Cheat Sheet](https://kubernetes.io/docs/reference/kubectl/cheatsheet/)

### Performance Testing
- [NBomber Documentation](https://nbomber.com/docs/)
- Performance test reports in `E2E.PerformanceTests/reports/`

---

## ✅ Deployment Checklist

- [ ] Docker Desktop installed and running
- [ ] Kubernetes enabled in Docker Desktop
- [ ] .NET 8 SDK installed
- [ ] Repository cloned
- [ ] Docker images built (4 images)
- [ ] Infrastructure deployed (PostgreSQL, Redis, RabbitMQ)
- [ ] Services deployed (UserService, TripService, DriverService, API Gateway)
- [ ] HPAs deployed (3 HPAs)
- [ ] Ingress deployed
- [ ] Health check passes (`curl http://localhost:8080/health`)
- [ ] E2E tests can run
- [ ] All pods in Running state

---

**Estimated Total Setup Time**: 20-30 minutes (first time)

**Subsequent Deployments**: 5-10 minutes

**Questions?** Check troubleshooting section or review project documentation.
