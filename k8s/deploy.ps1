# UIT-Go Kubernetes Deployment Script (PowerShell)
# Deploys autoscaling and load distribution

Write-Host "🚀 Deploying UIT-Go with Autoscaling & Load Distribution" -ForegroundColor Green
Write-Host ""

# Check if kubectl is installed
if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    Write-Host "❌ kubectl not found. Please install kubectl first." -ForegroundColor Red
    exit 1
}

# Check if connected to cluster
try {
    kubectl cluster-info | Out-Null
    Write-Host "✅ Connected to cluster" -ForegroundColor Green
} catch {
    Write-Host "❌ Not connected to Kubernetes cluster. Please configure kubectl." -ForegroundColor Red
    exit 1
}

Write-Host ""

# 1. Create namespace
Write-Host "📦 Creating namespace..." -ForegroundColor Cyan
kubectl apply -f namespace.yaml

# 2. Deploy API Gateway
Write-Host "🌐 Deploying API Gateway..." -ForegroundColor Cyan
kubectl apply -f api-gateway-deployment.yaml
kubectl apply -f api-gateway-service.yaml
kubectl apply -f api-gateway-hpa.yaml

# 3. Deploy Driver Service
Write-Host "🚗 Deploying Driver Service..." -ForegroundColor Cyan
kubectl apply -f driver-service.yaml

# 4. Deploy Ingress
Write-Host "🔀 Deploying Ingress..." -ForegroundColor Cyan
kubectl apply -f ingress.yaml

Write-Host ""
Write-Host "✅ Deployment complete!" -ForegroundColor Green
Write-Host ""
Write-Host "📊 Checking status..." -ForegroundColor Yellow
kubectl get pods -n uit-go
Write-Host ""
kubectl get hpa -n uit-go
Write-Host ""
Write-Host "🎉 Done! Your services will autoscale based on traffic." -ForegroundColor Green
Write-Host ""
Write-Host "Monitor autoscaling with:" -ForegroundColor Yellow
Write-Host "  kubectl get hpa -n uit-go -w"
Write-Host ""
Write-Host "Watch pods scale:" -ForegroundColor Yellow
Write-Host "  kubectl get pods -n uit-go -w"
