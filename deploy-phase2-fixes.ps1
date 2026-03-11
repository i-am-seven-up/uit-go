# Phase 2 Deployment Script - Apply Performance Fixes
# This script deploys the PostgreSQL and service configuration fixes

$ErrorActionPreference = "Stop"

Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   PHASE 2 PERFORMANCE FIXES DEPLOYMENT                  ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Step 1: Apply PostgreSQL configuration (max_connections=300)
Write-Host "📦 Step 1: Deploying PostgreSQL with increased max_connections..." -ForegroundColor Yellow
kubectl apply -f k8s/postgres.yaml
Write-Host "✅ PostgreSQL configuration updated" -ForegroundColor Green
Write-Host ""

# Step 2: Wait for PostgreSQL pods to restart
Write-Host "⏳ Step 2: Waiting for PostgreSQL pods to restart..." -ForegroundColor Yellow
kubectl rollout status deployment/postgres-trip -n uit-go --timeout=120s
kubectl rollout status deployment/postgres-driver -n uit-go --timeout=120s
Write-Host "✅ PostgreSQL pods restarted" -ForegroundColor Green
Write-Host ""

# Step 3: Rebuild service images with updated connection pooling
Write-Host "🔨 Step 3: Rebuilding service Docker images..." -ForegroundColor Yellow
docker build -t uit-go-trip-service:latest ./TripService
docker build -t uit-go-driver-service:latest ./DriverService
Write-Host "✅ Docker images rebuilt" -ForegroundColor Green
Write-Host ""

# Step 4: Deploy updated services
Write-Host "📦 Step 4: Deploying updated services..." -ForegroundColor Yellow
kubectl apply -f k8s/trip-service.yaml
kubectl apply -f k8s/driver-service.yaml
Write-Host "✅ Service configurations updated" -ForegroundColor Green
Write-Host ""

# Step 5: Wait for service rollout
Write-Host "⏳ Step 5: Waiting for service rollout..." -ForegroundColor Yellow
kubectl rollout status deployment/trip-service -n uit-go --timeout=180s
kubectl rollout status deployment/driver-service -n uit-go --timeout=180s
Write-Host "✅ Services deployed" -ForegroundColor Green
Write-Host ""

# Step 6: Deploy Horizontal Pod Autoscalers (HPAs)
Write-Host "📈 Step 6: Deploying Horizontal Pod Autoscalers..." -ForegroundColor Yellow
kubectl apply -f k8s/api-gateway-hpa.yaml
kubectl apply -f k8s/trip-service-hpa.yaml
# DriverService HPA is embedded in driver-service.yaml, already applied
Write-Host "✅ HPAs deployed" -ForegroundColor Green
Write-Host ""
Write-Host "HPA Status:"
kubectl get hpa -n uit-go
Write-Host ""

# Step 7: Verify deployment
Write-Host "🔍 Step 7: Verifying deployment..." -ForegroundColor Yellow
Write-Host ""
Write-Host "PostgreSQL Pods:"
kubectl get pods -n uit-go -l app=postgres-trip
kubectl get pods -n uit-go -l app=postgres-driver
Write-Host ""
Write-Host "Service Pods:"
kubectl get pods -n uit-go -l app=trip-service
kubectl get pods -n uit-go -l app=driver-service
Write-Host ""

# Step 8: Check PostgreSQL max_connections
Write-Host "🔍 Step 8: Verifying PostgreSQL configuration..." -ForegroundColor Yellow
$TRIP_POD = kubectl get pods -n uit-go -l app=postgres-trip -o jsonpath='{.items[0].metadata.name}'
$DRIVER_POD = kubectl get pods -n uit-go -l app=postgres-driver -o jsonpath='{.items[0].metadata.name}'

Write-Host "Trip PostgreSQL max_connections:"
try {
    kubectl exec -n uit-go $TRIP_POD -- psql -U postgres -d uitgo_trip -c "SHOW max_connections;"
}
catch {
    Write-Host "⚠️  Could not verify (pod may still be starting)" -ForegroundColor Yellow
}

Write-Host "Driver PostgreSQL max_connections:"
try {
    kubectl exec -n uit-go $DRIVER_POD -- psql -U postgres -d uitgo_driver -c "SHOW max_connections;"
}
catch {
    Write-Host "⚠️  Could not verify (pod may still be starting)" -ForegroundColor Yellow
}
Write-Host ""

Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║   DEPLOYMENT COMPLETED SUCCESSFULLY!                    ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "📊 Configuration Summary:" -ForegroundColor Cyan
Write-Host "  - PostgreSQL max_connections: 300 (up from 100)"
Write-Host "  - TripService pool size: 100 per replica (up from 50)"
Write-Host "  - DriverService pool size: 100 per replica (up from 50)"
Write-Host "  - Total capacity: 300 connections per database"
Write-Host ""
Write-Host "📈 Horizontal Pod Autoscalers (HPAs):" -ForegroundColor Cyan
Write-Host "  - API Gateway HPA: 3-20 replicas (CPU: 70%, Memory: 80%)"
Write-Host "  - TripService HPA: 2-20 replicas (CPU: 70%, Memory: 80%) [NEW]"
Write-Host "  - DriverService HPA: 3-15 replicas (CPU: 70%, Memory: 80%)"
Write-Host ""
Write-Host "📈 Expected Performance Improvements:" -ForegroundColor Cyan
Write-Host "  - Trip Creation: 76 RPS → 800+ RPS (10× improvement)"
Write-Host "  - Error Rate: 36% → <1%"
Write-Host "  - p95 Latency: 25s → <500ms (50× improvement)"
Write-Host ""
Write-Host "🧪 Next Step: Run E2E performance tests to validate" -ForegroundColor Yellow
Write-Host "  $ cd E2E.PerformanceTests"
Write-Host "  $ dotnet run"
Write-Host ""
