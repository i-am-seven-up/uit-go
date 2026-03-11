# Complete UIT-Go Kubernetes Deployment
# Replaces Docker Compose entirely

Write-Host "🚀 Deploying Complete UIT-Go Stack to Kubernetes" -ForegroundColor Green
Write-Host ""

# Check kubectl
if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    Write-Host "❌ kubectl not found" -ForegroundColor Red
    exit 1
}

# 1. Create namespace
Write-Host "📦 Creating namespace..." -ForegroundColor Cyan
kubectl apply -f namespace.yaml

# 2. Deploy infrastructure (Redis, RabbitMQ, PostgreSQL)
Write-Host "🗄️  Deploying infrastructure..." -ForegroundColor Cyan
kubectl apply -f redis.yaml
kubectl apply -f rabbitmq.yaml
kubectl apply -f postgres.yaml

Write-Host "⏳ Waiting for infrastructure to be ready..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# Wait for Redis
kubectl wait --for=condition=ready pod -l app=redis -n uit-go --timeout=120s
Write-Host "✅ Redis ready" -ForegroundColor Green

# Wait for RabbitMQ
kubectl wait --for=condition=ready pod -l app=rabbitmq -n uit-go --timeout=120s
Write-Host "✅ RabbitMQ ready" -ForegroundColor Green

# Wait for PostgreSQL
kubectl wait --for=condition=ready pod -l app=postgres-user -n uit-go --timeout=120s
kubectl wait --for=condition=ready pod -l app=postgres-trip -n uit-go --timeout=120s
kubectl wait --for=condition=ready pod -l app=postgres-driver -n uit-go --timeout=120s
Write-Host "✅ PostgreSQL ready" -ForegroundColor Green

# 3. Deploy microservices
Write-Host "🔧 Deploying microservices..." -ForegroundColor Cyan
kubectl apply -f user-service.yaml
kubectl apply -f driver-service.yaml
kubectl apply -f trip-service.yaml
kubectl apply -f api-gateway-deployment.yaml
kubectl apply -f api-gateway-service.yaml

# 4. Deploy autoscaling
Write-Host "📈 Deploying autoscaling..." -ForegroundColor Cyan
kubectl apply -f api-gateway-hpa.yaml
kubectl apply -f driver-service.yaml  # Already has HPA

# 5. Deploy ingress
Write-Host "🔀 Deploying ingress..." -ForegroundColor Cyan
kubectl apply -f ingress.yaml

Write-Host ""
Write-Host "✅ Deployment complete!" -ForegroundColor Green
Write-Host ""

# Show status
Write-Host "📊 Current status:" -ForegroundColor Yellow
kubectl get pods -n uit-go
Write-Host ""
kubectl get svc -n uit-go
Write-Host ""
kubectl get hpa -n uit-go

Write-Host ""
Write-Host "🎉 UIT-Go is now running on Kubernetes!" -ForegroundColor Green
Write-Host ""
Write-Host "Access API Gateway:" -ForegroundColor Yellow
Write-Host "  kubectl port-forward -n uit-go service/api-gateway 8080:8080"
Write-Host ""
Write-Host "Monitor autoscaling:" -ForegroundColor Yellow
Write-Host "  kubectl get hpa -n uit-go -w"
Write-Host ""
Write-Host "View logs:" -ForegroundColor Yellow
Write-Host "  kubectl logs -n uit-go -l app=api-gateway --tail=50 -f"
