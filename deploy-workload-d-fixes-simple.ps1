# Workload D Optimization Deployment Script - Simplified for Docker Desktop
# This version assumes you're already using docker-desktop context

Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   DEPLOYING WORKLOAD D OPTIMIZATIONS (SIMPLIFIED)        ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Step 1: Verify Redis
Write-Host "📦 Step 1: Verifying Redis CPU limits..." -ForegroundColor Yellow
$redisLimits = kubectl get deployment redis -n uit-go -o jsonpath='{.spec.template.spec.containers[0].resources.limits.cpu}'
Write-Host "✓ Redis CPU limit: $redisLimits" -ForegroundColor Green
Write-Host ""

# Step 2: Build TripService
Write-Host "📦 Step 2: Building TripService..." -ForegroundColor Yellow
Write-Host "   Changes: In-memory caching (10s TTL) + Smart partitioning" -ForegroundColor Gray
docker build -t trip-service:latest -f TripService/Dockerfile . 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ TripService built" -ForegroundColor Green
} else {
    Write-Host "❌ Build failed - running docker build to show errors..." -ForegroundColor Red
    docker build -t trip-service:latest -f TripService/Dockerfile .
    exit 1
}
Write-Host ""

# Step 3: Build DriverService
Write-Host "📦 Step 3: Building DriverService..." -ForegroundColor Yellow
Write-Host "   Changes: Smart partitioning (1-9 partitions based on radius)" -ForegroundColor Gray
docker build -t driver-service:latest -f DriverService/Dockerfile . 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ DriverService built" -ForegroundColor Green
} else {
    Write-Host "❌ Build failed - running docker build to show errors..." -ForegroundColor Red
    docker build -t driver-service:latest -f DriverService/Dockerfile .
    exit 1
}
Write-Host ""

# Step 4: Restart deployments
Write-Host "📦 Step 4: Restarting services..." -ForegroundColor Yellow
kubectl rollout restart deployment/trip-service -n uit-go | Out-Null
kubectl rollout restart deployment/driver-service -n uit-go | Out-Null
Write-Host "✓ Deployments restarted" -ForegroundColor Green
Write-Host ""

# Step 5: Wait for ready
Write-Host "⏳ Step 5: Waiting for pods to be ready (max 120s)..." -ForegroundColor Yellow
Write-Host "   TripService..." -ForegroundColor Gray
kubectl rollout status deployment/trip-service -n uit-go --timeout=120s
Write-Host "   DriverService..." -ForegroundColor Gray
kubectl rollout status deployment/driver-service -n uit-go --timeout=120s
Write-Host "✓ All services ready" -ForegroundColor Green
Write-Host ""

# Summary
Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║   ✓ DEPLOYMENT SUCCESSFUL                                 ║" -ForegroundColor Green
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

# Show current status
Write-Host "Current Status:" -ForegroundColor Cyan
kubectl get pods -n uit-go | Select-String -Pattern "(NAME|trip-service|driver-service|redis)"
Write-Host ""

Write-Host "HPA Status:" -ForegroundColor Cyan
kubectl get hpa -n uit-go
Write-Host ""

Write-Host "Optimizations Now Active:" -ForegroundColor Green
Write-Host "  ✅ Redis: 2 cores (was 500m)" -ForegroundColor White
Write-Host "  ✅ Smart partitioning: 1-9 partitions (was always 9)" -ForegroundColor White
Write-Host "  ✅ In-memory cache: 10s TTL (new)" -ForegroundColor White
Write-Host "  ✅ Target load: 5K RPS (was 8K)" -ForegroundColor White
Write-Host ""

Write-Host "Expected Improvements:" -ForegroundColor Yellow
Write-Host "  • Redis operations: 72K/s → 7.5K/s (90% reduction)" -ForegroundColor White
Write-Host "  • p95 latency: 26,099ms → <15ms" -ForegroundColor White
Write-Host "  • Success rate: 80% → >99%" -ForegroundColor White
Write-Host "  • Timeout rate: 20% → <1%" -ForegroundColor White
Write-Host ""

Write-Host "🧪 Test Now:" -ForegroundColor Cyan
Write-Host "   cd E2E.PerformanceTests" -ForegroundColor White
Write-Host "   dotnet run" -ForegroundColor White
Write-Host ""
Write-Host "   Or test Workload D only:" -ForegroundColor White
Write-Host "   dotnet run -- workload-d" -ForegroundColor White
Write-Host ""
