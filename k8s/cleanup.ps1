# Cleanup script - removes everything from Kubernetes

Write-Host "🧹 Cleaning up UIT-Go from Kubernetes..." -ForegroundColor Yellow
Write-Host ""

# Delete all resources in uit-go namespace
kubectl delete namespace uit-go

Write-Host ""
Write-Host "✅ Cleanup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "To redeploy, run:" -ForegroundColor Cyan
Write-Host "  .\deploy-complete.ps1"
