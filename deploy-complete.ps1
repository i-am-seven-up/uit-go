# UIT-GO Quick Deploy Script
# Automated deployment for fresh machine setup

$ErrorActionPreference = "Stop"

Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║        UIT-GO AUTOMATED DEPLOYMENT SCRIPT               ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Function to check prerequisites
function Test-Prerequisites {
    Write-Host "🔍 Checking prerequisites..." -ForegroundColor Yellow
    
    # Check Docker
    try {
        docker --version | Out-Null
        Write-Host "✅ Docker installed" -ForegroundColor Green
    }
    catch {
        Write-Host "❌ Docker not found. Please install Docker Desktop first." -ForegroundColor Red
        exit 1
    }
    
    # Check Kubernetes
    try {
        kubectl cluster-info | Out-Null
        Write-Host "✅ Kubernetes enabled" -ForegroundColor Green
    }
    catch {
        Write-Host "❌ Kubernetes not enabled. Please enable in Docker Desktop settings." -ForegroundColor Red
        exit 1
    }
    
    # Check .NET
    try {
        dotnet --version | Out-Null
        Write-Host "✅ .NET SDK installed" -ForegroundColor Green
    }
    catch {
        Write-Host "❌ .NET SDK not found. Please install .NET 8 SDK." -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
}

# Function to install Kubernetes add-ons
function Install-KubernetesAddons {
    Write-Host "📦 Installing Kubernetes add-ons..." -ForegroundColor Yellow
    
    # Check if Ingress NGINX already installed
    $ingressPods = kubectl get pods -n ingress-nginx --no-headers 2>$null
    if ($ingressPods) {
        Write-Host "  ✅ Ingress NGINX already installed" -ForegroundColor Green
    }
    else {
        Write-Host "  Installing Ingress NGINX Controller..." -ForegroundColor Gray
        kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.8.1/deploy/static/provider/cloud/deploy.yaml | Out-Null
        
        Write-Host "  Waiting for Ingress NGINX to be ready..." -ForegroundColor Gray
        kubectl wait --namespace ingress-nginx `
            --for=condition=ready pod `
            --selector=app.kubernetes.io/component=controller `
            --timeout=120s | Out-Null
        Write-Host "  ✅ Ingress NGINX installed" -ForegroundColor Green
    }
    
    # Check if Metrics Server already installed
    $metricsDeployment = kubectl get deployment -n kube-system metrics-server --no-headers 2>$null
    if ($metricsDeployment) {
        Write-Host "  ✅ Metrics Server already installed" -ForegroundColor Green
    }
    else {
        Write-Host "  Installing Metrics Server..." -ForegroundColor Gray
        
        # Download and patch metrics server manifest
        Invoke-WebRequest -Uri "https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml" -OutFile "metrics-server.yaml" | Out-Null
        
        # Patch for Docker Desktop (add --kubelet-insecure-tls)
        (Get-Content metrics-server.yaml) -replace '        - --cert-dir=/tmp', "        - --cert-dir=/tmp`n        - --kubelet-insecure-tls" | Set-Content metrics-server.yaml
        
        kubectl apply -f metrics-server.yaml | Out-Null
        
        Write-Host "  Waiting for Metrics Server to be ready..." -ForegroundColor Gray
        kubectl wait --namespace kube-system `
            --for=condition=ready pod `
            --selector=k8s-app=metrics-server `
            --timeout=120s | Out-Null
        
        # Clean up temp file
        Remove-Item metrics-server.yaml -ErrorAction SilentlyContinue
        
        Write-Host "  ✅ Metrics Server installed" -ForegroundColor Green
    }
    
    Write-Host ""
}

# Function to build Docker images
function Build-DockerImages {
    Write-Host "🔨 Building Docker images..." -ForegroundColor Yellow
    Write-Host "This will take 5-10 minutes..." -ForegroundColor Gray
    
    $images = @(
        @{Name = "uit-go-user-service"; Dockerfile = "deploy/docker/UserService.Dockerfile" },
        @{Name = "uit-go-trip-service"; Dockerfile = "deploy/docker/TripService.Dockerfile" },
        @{Name = "uit-go-driver-service"; Dockerfile = "deploy/docker/DriverService.Dockerfile" },
        @{Name = "uit-go-api-gateway"; Dockerfile = "deploy/docker/ApiGateway.Dockerfile" }
    )
    
    foreach ($img in $images) {
        Write-Host "  Building $($img.Name)..." -ForegroundColor Gray
        docker build -t "$($img.Name):latest" -f $img.Dockerfile . 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✅ $($img.Name) built" -ForegroundColor Green
        }
        else {
            Write-Host "  ❌ Failed to build $($img.Name)" -ForegroundColor Red
            exit 1
        }
    }
    Write-Host ""
}

# Function to deploy infrastructure
function Deploy-Infrastructure {
    Write-Host "📦 Deploying infrastructure..." -ForegroundColor Yellow
    
    # Create namespace
    kubectl apply -f k8s/namespace.yaml | Out-Null
    Write-Host "  ✅ Namespace created" -ForegroundColor Green
    
    # Deploy databases
    kubectl apply -f k8s/postgres.yaml | Out-Null
    Write-Host "  ✅ PostgreSQL deployed" -ForegroundColor Green
    
    # Deploy Redis
    kubectl apply -f k8s/redis.yaml | Out-Null
    Write-Host "  ✅ Redis deployed" -ForegroundColor Green
    
    # Deploy RabbitMQ
    kubectl apply -f k8s/rabbitmq.yaml | Out-Null
    Write-Host "  ✅ RabbitMQ deployed" -ForegroundColor Green
    
    Write-Host ""
    Write-Host "⏳ Waiting for infrastructure pods to be ready..." -ForegroundColor Yellow
    kubectl wait --for=condition=ready pod -l app=postgres-trip -n uit-go --timeout=180s 2>&1 | Out-Null
    kubectl wait --for=condition=ready pod -l app=postgres-driver -n uit-go --timeout=180s 2>&1 | Out-Null
    kubectl wait --for=condition=ready pod -l app=postgres-user -n uit-go --timeout=180s 2>&1 | Out-Null
    kubectl wait --for=condition=ready pod -l app=redis -n uit-go --timeout=180s 2>&1 | Out-Null
    kubectl wait --for=condition=ready pod -l app=rabbitmq -n uit-go --timeout=180s 2>&1 | Out-Null
    Write-Host "  ✅ All infrastructure pods ready" -ForegroundColor Green
    Write-Host ""
}

# Function to deploy services
function Deploy-Services {
    Write-Host "🚀 Deploying application services..." -ForegroundColor Yellow
    
    # Deploy services
    kubectl apply -f k8s/user-service.yaml | Out-Null
    Write-Host "  ✅ UserService deployed" -ForegroundColor Green
    
    kubectl apply -f k8s/trip-service.yaml | Out-Null
    Write-Host "  ✅ TripService deployed" -ForegroundColor Green
    
    kubectl apply -f k8s/driver-service.yaml | Out-Null
    Write-Host "  ✅ DriverService deployed" -ForegroundColor Green
    
    kubectl apply -f k8s/api-gateway-deployment.yaml | Out-Null
    kubectl apply -f k8s/api-gateway-service.yaml | Out-Null
    Write-Host "  ✅ API Gateway deployed" -ForegroundColor Green
    
    # Deploy HPAs
    kubectl apply -f k8s/api-gateway-hpa.yaml | Out-Null
    kubectl apply -f k8s/trip-service-hpa.yaml | Out-Null
    Write-Host "  ✅ HPAs deployed" -ForegroundColor Green
    
    # Deploy Ingress
    kubectl apply -f k8s/ingress.yaml | Out-Null
    Write-Host "  ✅ Ingress deployed" -ForegroundColor Green
    
    Write-Host ""
    Write-Host "⏳ Waiting for service pods to be ready..." -ForegroundColor Yellow
    kubectl wait --for=condition=ready pod -l app=user-service -n uit-go --timeout=180s 2>&1 | Out-Null
    kubectl wait --for=condition=ready pod -l app=trip-service -n uit-go --timeout=180s 2>&1 | Out-Null
    kubectl wait --for=condition=ready pod -l app=driver-service -n uit-go --timeout=180s 2>&1 | Out-Null
    kubectl wait --for=condition=ready pod -l app=api-gateway -n uit-go --timeout=180s 2>&1 | Out-Null
    Write-Host "  ✅ All service pods ready" -ForegroundColor Green
    Write-Host ""
}

# Function to verify deployment
function Test-Deployment {
    Write-Host "🔍 Verifying deployment..." -ForegroundColor Yellow
    
    # Check pods
    $pods = kubectl get pods -n uit-go --no-headers
    $runningPods = ($pods | Select-String "Running").Count
    $totalPods = ($pods | Measure-Object).Count
    
    Write-Host "  Pods: $runningPods/$totalPods running" -ForegroundColor $(if ($runningPods -eq $totalPods) { "Green" } else { "Yellow" })
    
    # Check HPAs
    $hpas = kubectl get hpa -n uit-go --no-headers
    $hpaCount = ($hpas | Measure-Object).Count
    Write-Host "  HPAs: $hpaCount deployed" -ForegroundColor Green
    
    # Check Metrics Server
    Write-Host "  Checking Metrics Server..." -ForegroundColor Gray
    try {
        kubectl top nodes 2>&1 | Out-Null
        Write-Host "  ✅ Metrics Server working" -ForegroundColor Green
    }
    catch {
        Write-Host "  ⚠️  Metrics Server may need more time" -ForegroundColor Yellow
    }
    
    # Test health endpoint
    Write-Host ""
    Write-Host "  Testing health endpoint..." -ForegroundColor Gray
    Start-Sleep -Seconds 5  # Give services a moment
    
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:8080/health" -UseBasicParsing -TimeoutSec 10
        if ($response.StatusCode -eq 200) {
            Write-Host "  ✅ Health check passed" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "  ⚠️  Health check failed (may need port-forward)" -ForegroundColor Yellow
        Write-Host "  Run: kubectl port-forward -n uit-go svc/api-gateway 8080:8080" -ForegroundColor Gray
    }
    
    Write-Host ""
}

# Function to display summary
function Show-Summary {
    Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "║           DEPLOYMENT COMPLETED SUCCESSFULLY!            ║" -ForegroundColor Green
    Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Green
    Write-Host ""
    
    Write-Host "📊 Deployment Summary:" -ForegroundColor Cyan
    Write-Host ""
    
    # Show all resources
    kubectl get all -n uit-go
    
    Write-Host ""
    Write-Host "📈 Kubernetes Add-ons:" -ForegroundColor Cyan
    Write-Host "  - Ingress NGINX Controller: ✅ Installed" -ForegroundColor Gray
    Write-Host "  - Metrics Server: ✅ Installed (for HPA)" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "🎯 Next Steps:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Access API Gateway:" -ForegroundColor White
    Write-Host "   kubectl port-forward -n uit-go svc/api-gateway 8080:8080" -ForegroundColor Gray
    Write-Host "   curl http://localhost:8080/health" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Run E2E Performance Tests:" -ForegroundColor White
    Write-Host "   cd E2E.PerformanceTests" -ForegroundColor Gray
    Write-Host "   dotnet run" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Monitor system:" -ForegroundColor White
    Write-Host "   kubectl get pods -n uit-go --watch" -ForegroundColor Gray
    Write-Host "   kubectl get hpa -n uit-go --watch" -ForegroundColor Gray
    Write-Host "   kubectl top nodes" -ForegroundColor Gray
    Write-Host ""
    Write-Host "📚 Documentation:" -ForegroundColor Cyan
    Write-Host "   - DEPLOYMENT_GUIDE.md - Complete deployment guide" -ForegroundColor Gray
    Write-Host "   - GROWTH_JOURNAL_PERFORMANCE_OPTIMIZATION.md - Project journey" -ForegroundColor Gray
    Write-Host "   - HPA_ARCHITECTURE_DECISION.md - HPA strategy" -ForegroundColor Gray
    Write-Host ""
}

# Main execution
try {
    $startTime = Get-Date
    
    Test-Prerequisites
    Install-KubernetesAddons  # NEW: Install Ingress NGINX & Metrics Server
    Build-DockerImages
    Deploy-Infrastructure
    Deploy-Services
    Test-Deployment
    
    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalMinutes
    
    Show-Summary
    
    Write-Host "⏱️  Total deployment time: $([math]::Round($duration, 1)) minutes" -ForegroundColor Cyan
    Write-Host ""
    
}
catch {
    Write-Host ""
    Write-Host "❌ Deployment failed: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "💡 Troubleshooting:" -ForegroundColor Yellow
    Write-Host "   1. Check DEPLOYMENT_GUIDE.md for detailed instructions" -ForegroundColor Gray
    Write-Host "   2. Verify all prerequisites are installed" -ForegroundColor Gray
    Write-Host "   3. Check Docker Desktop has enough resources (8GB RAM, 4 CPUs)" -ForegroundColor Gray
    Write-Host "   4. View logs: kubectl logs -n uit-go <pod-name>" -ForegroundColor Gray
    Write-Host ""
    exit 1
}
