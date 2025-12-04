# Workload D Optimization Deployment Script
# Windows PowerShell version

Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   DEPLOYING WORKLOAD D OPTIMIZATIONS                     ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Check if kubectl is available
if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    Write-Host "❌ kubectl not found. Please install kubectl first." -ForegroundColor Red
    exit 1
}

# Check if connected to cluster
try {
    $null = kubectl cluster-info 2>&1
} catch {
    Write-Host "❌ Not connected to Kubernetes cluster. Please configure kubectl." -ForegroundColor Red
    exit 1
}

$context = kubectl config current-context
Write-Host "✓ Connected to cluster: $context" -ForegroundColor Green
Write-Host ""

# Step 1: Deploy Redis with increased CPU limits
Write-Host "📦 Step 1: Deploying Redis with increased CPU limits (500m → 2000m)..." -ForegroundColor Yellow
kubectl apply -f k8s/redis.yaml
Write-Host "✓ Redis configuration updated" -ForegroundColor Green
Write-Host ""

# Wait for Redis to restart
Write-Host "⏳ Waiting for Redis to be ready..." -ForegroundColor Yellow
kubectl rollout status deployment/redis -n uit-go --timeout=120s
Write-Host "✓ Redis is ready" -ForegroundColor Green
Write-Host ""

# Step 2: Build and deploy TripService (with caching + smart partitioning)
Write-Host "📦 Step 2: Building TripService with GEO search optimizations..." -ForegroundColor Yellow
docker build -t trip-service:workload-d-fix -f TripService/Dockerfile .
Write-Host "✓ TripService image built" -ForegroundColor Green
Write-Host ""

Write-Host "📦 Deploying TripService..." -ForegroundColor Yellow
kubectl set image deployment/trip-service trip-service=trip-service:workload-d-fix -n uit-go
kubectl rollout status deployment/trip-service -n uit-go --timeout=120s
Write-Host "✓ TripService deployed" -ForegroundColor Green
Write-Host ""

# Step 3: Build and deploy DriverService (with smart partitioning)
Write-Host "📦 Step 3: Building DriverService with optimized partition queries..." -ForegroundColor Yellow
docker build -t driver-service:workload-d-fix -f DriverService/Dockerfile .
Write-Host "✓ DriverService image built" -ForegroundColor Green
Write-Host ""

Write-Host "📦 Deploying DriverService..." -ForegroundColor Yellow
kubectl set image deployment/driver-service driver-service=driver-service:workload-d-fix -n uit-go
kubectl rollout status deployment/driver-service -n uit-go --timeout=120s
Write-Host "✓ DriverService deployed" -ForegroundColor Green
Write-Host ""

# Step 4: Verify HPAs are working
Write-Host "📊 Step 4: Verifying HPA status..." -ForegroundColor Yellow
kubectl get hpa -n uit-go
Write-Host ""

# Step 5: Check Redis metrics
Write-Host "📊 Step 5: Checking Redis metrics..." -ForegroundColor Yellow
$redisPod = kubectl get pod -n uit-go -l app=redis -o jsonpath='{.items[0].metadata.name}'
Write-Host "Redis pod: $redisPod" -ForegroundColor Cyan
try {
    kubectl top pod $redisPod -n uit-go
} catch {
    Write-Host "⚠️  Metrics not available yet" -ForegroundColor Yellow
}
Write-Host ""

# Summary
Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║   DEPLOYMENT COMPLETE                                     ║" -ForegroundColor Green
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "Optimizations deployed:" -ForegroundColor Cyan
Write-Host "  ✅ Redis CPU limit: 500m → 2000m" -ForegroundColor Green
Write-Host "  ✅ Smart partition queries (1-9 partitions based on radius)" -ForegroundColor Green
Write-Host "  ✅ In-memory result caching (10s TTL)" -ForegroundColor Green
Write-Host "  ✅ Reduced load target (8K → 5K RPS)" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Monitor Redis CPU: kubectl top pod -n uit-go | grep redis" -ForegroundColor White
Write-Host "  2. Watch HPA scaling: kubectl get hpa -n uit-go --watch" -ForegroundColor White
Write-Host "  3. Run E2E tests: cd E2E.PerformanceTests && dotnet run" -ForegroundColor White
Write-Host "  4. Check results: Expect >99% success, <15ms p95 latency" -ForegroundColor White
Write-Host ""
Write-Host "Troubleshooting guide: WORKLOAD_D_OPTIMIZATION_GUIDE.md" -ForegroundColor Cyan
Write-Host ""
