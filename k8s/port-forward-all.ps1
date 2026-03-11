# Port Forward All Services for E2E Tests

Write-Host "🔌 Setting up port forwards for E2E tests..." -ForegroundColor Green
Write-Host ""

# Kill any existing port-forwards
Get-Process -Name kubectl -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like "*port-forward*" } | Stop-Process -Force

Write-Host "Starting port forwards..." -ForegroundColor Cyan

# API Gateway - 8080
Start-Process powershell -ArgumentList "-NoExit", "-Command", "kubectl port-forward -n uit-go service/api-gateway 8080:8080" -WindowStyle Minimized

# Trip Service - 5002  
Start-Process powershell -ArgumentList "-NoExit", "-Command", "kubectl port-forward -n uit-go service/trip-service 5002:8080" -WindowStyle Minimized

# Driver Service - 5003
Start-Process powershell -ArgumentList "-NoExit", "-Command", "kubectl port-forward -n uit-go service/driver-service 5003:5000" -WindowStyle Minimized

# User Service - 5001
Start-Process powershell -ArgumentList "-NoExit", "-Command", "kubectl port-forward -n uit-go service/user-service 5001:8080" -WindowStyle Minimized

Start-Sleep -Seconds 3

Write-Host ""
Write-Host "✅ Port forwards started!" -ForegroundColor Green
Write-Host ""
Write-Host "Services accessible at:" -ForegroundColor Yellow
Write-Host "  API Gateway:    http://localhost:8080"
Write-Host "  User Service:   http://localhost:5001"
Write-Host "  Trip Service:   http://localhost:5002"
Write-Host "  Driver Service: http://localhost:5003"
Write-Host ""
Write-Host "⚠️  Keep this window open during tests!" -ForegroundColor Yellow
Write-Host ""
Write-Host "To stop all port forwards, close this window or run:" -ForegroundColor Cyan
Write-Host "  Get-Process kubectl | Stop-Process"
Write-Host ""
Write-Host "Press Enter to test connections..."
Read-Host

# Test connections
Write-Host "Testing connections..." -ForegroundColor Cyan
try { $r = Invoke-WebRequest http://localhost:8080/health -UseBasicParsing -TimeoutSec 2; Write-Host "✓ API Gateway: $($r.StatusCode)" -ForegroundColor Green } catch { Write-Host "✗ API Gateway failed" -ForegroundColor Red }
try { $r = Invoke-WebRequest http://localhost:5001/health -UseBasicParsing -TimeoutSec 2; Write-Host "✓ User Service: $($r.StatusCode)" -ForegroundColor Green } catch { Write-Host "✗ User Service failed" -ForegroundColor Red }
try { $r = Invoke-WebRequest http://localhost:5002/health -UseBasicParsing -TimeoutSec 2; Write-Host "✓ Trip Service: $($r.StatusCode)" -ForegroundColor Green } catch { Write-Host "✗ Trip Service failed" -ForegroundColor Red }

Write-Host ""
Write-Host "Ready to run E2E tests!" -ForegroundColor Green
Write-Host "Run: .\run-performance-tests.ps1 -Workload C" -ForegroundColor Yellow
Write-Host ""
Write-Host "Press Ctrl+C to stop port forwards..."
while ($true) { Start-Sleep -Seconds 10 }
