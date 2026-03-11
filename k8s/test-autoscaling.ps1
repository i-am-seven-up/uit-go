# Test Autoscaling Script

Write-Host "🧪 Testing K8s Autoscaling" -ForegroundColor Green
Write-Host ""

# 1. Check if port-forward is running
Write-Host "📡 Testing connection to API Gateway..." -ForegroundColor Cyan
try {
    $response = Invoke-WebRequest -Uri "http://localhost:8080/health" -UseBasicParsing -TimeoutSec 5
    Write-Host "✅ API Gateway is accessible: $($response.StatusCode)" -ForegroundColor Green
}
catch {
    Write-Host "❌ Cannot reach API Gateway on localhost:8080" -ForegroundColor Red
    Write-Host "   Make sure port-forward is running:" -ForegroundColor Yellow
    Write-Host "   kubectl port-forward -n uit-go service/api-gateway 8080:8080" -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# 2. Check HPA status
Write-Host "📊 Current HPA status:" -ForegroundColor Cyan
kubectl get hpa -n uit-go

Write-Host ""

# 3. Check current pod count
Write-Host "🔢 Current pod count:" -ForegroundColor Cyan
kubectl get pods -n uit-go -l app=api-gateway

Write-Host ""
Write-Host "🚀 Starting load generation..." -ForegroundColor Green
Write-Host "   Press Ctrl+C to stop" -ForegroundColor Yellow
Write-Host ""

# 4. Generate load
$count = 0
while ($true) {
    try {
        Invoke-WebRequest -Uri "http://localhost:8080/health" -UseBasicParsing -TimeoutSec 1 | Out-Null
        $count++
        if ($count % 100 -eq 0) {
            Write-Host "✓ Sent $count requests" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "⚠ Request failed" -ForegroundColor Yellow
    }
    Start-Sleep -Milliseconds 10
}
