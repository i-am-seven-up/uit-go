# PowerShell script to run E2E performance tests for UIT-GO
# Supports running all workloads or specific workloads

param(
    [string]$Workload = "all",
    [switch]$SkipBuild,
    [switch]$Help
)

if ($Help) {
    Write-Host "UIT-GO E2E Performance Test Runner" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage: .\run-performance-tests.ps1 [-Workload <workload>] [-SkipBuild]" -ForegroundColor White
    Write-Host ""
    Write-Host "Parameters:" -ForegroundColor Yellow
    Write-Host "  -Workload <name>    Specify which workload to run:" -ForegroundColor White
    Write-Host "                      - all (default): Run all workloads sequentially" -ForegroundColor Gray
    Write-Host "                      - a: Workload A (Trip Creation)" -ForegroundColor Gray
    Write-Host "                      - b: Workload B (Driver Responses)" -ForegroundColor Gray
    Write-Host "                      - c: Workload C (Location Updates)" -ForegroundColor Gray
    Write-Host "  -SkipBuild          Skip building the project" -ForegroundColor White
    Write-Host "  -Help               Show this help message" -ForegroundColor White
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor Yellow
    Write-Host "  .\run-performance-tests.ps1                  # Run all workloads" -ForegroundColor Gray
    Write-Host "  .\run-performance-tests.ps1 -Workload a      # Run only Workload A" -ForegroundColor Gray
    Write-Host "  .\run-performance-tests.ps1 -SkipBuild       # Run tests without rebuilding" -ForegroundColor Gray
    Write-Host ""
    exit 0
}

$ErrorActionPreference = "Stop"

Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   UIT-GO E2E PERFORMANCE TEST RUNNER                     ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow
Write-Host ""

# Check .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Host "✓ .NET SDK version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "✗ .NET SDK not found. Please install .NET 8.0 or higher." -ForegroundColor Red
    exit 1
}

# Check Docker
try {
    docker version | Out-Null
    Write-Host "✓ Docker is running" -ForegroundColor Green
} catch {
    Write-Host "✗ Docker is not running. Please start Docker Desktop." -ForegroundColor Red
    exit 1
}

# Check if services are running
Write-Host ""
Write-Host "Checking if services are running..." -ForegroundColor Yellow

try {
    $response = Invoke-WebRequest -Uri "http://localhost:8080" -Method Get -TimeoutSec 5 -ErrorAction SilentlyContinue
    Write-Host "✓ API Gateway is running (http://localhost:8080)" -ForegroundColor Green
} catch {
    Write-Host "⚠  API Gateway not responding. Please ensure docker-compose is running:" -ForegroundColor Yellow
    Write-Host "   docker-compose up -d" -ForegroundColor Gray
    Write-Host ""
    $continue = Read-Host "Continue anyway? (y/n)"
    if ($continue -ne "y") {
        exit 1
    }
}

Write-Host ""

# Build the test project
if (-not $SkipBuild) {
    Write-Host "Building E2E.PerformanceTests..." -ForegroundColor Yellow
    dotnet build E2E.PerformanceTests\E2E.PerformanceTests.csproj -c Release

    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed!" -ForegroundColor Red
        exit 1
    }

    Write-Host "✓ Build succeeded" -ForegroundColor Green
    Write-Host ""
}

# Run the tests
Write-Host "Starting performance tests..." -ForegroundColor Yellow
Write-Host "Workload: $Workload" -ForegroundColor Gray
Write-Host ""

$startTime = Get-Date

try {
    dotnet run --project E2E.PerformanceTests\E2E.PerformanceTests.csproj -c Release -- $Workload

    if ($LASTEXITCODE -eq 0) {
        $endTime = Get-Date
        $duration = $endTime - $startTime

        Write-Host ""
        Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Green
        Write-Host "║   PERFORMANCE TESTS COMPLETED                            ║" -ForegroundColor Green
        Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Green
        Write-Host ""
        Write-Host "Total duration: $($duration.ToString('hh\:mm\:ss'))" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Results have been exported to JSON files in the current directory." -ForegroundColor Gray
        Write-Host "Look for files named: results_Workload*_*.json" -ForegroundColor Gray
        Write-Host ""
    } else {
        Write-Host ""
        Write-Host "✗ Tests failed with exit code: $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host ""
    Write-Host "✗ Error running tests: $_" -ForegroundColor Red
    exit 1
}

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Review the JSON result files for detailed metrics" -ForegroundColor White
Write-Host "2. Compare with Phase 2 results after implementing Module A" -ForegroundColor White
Write-Host "3. Document any bottlenecks or performance issues" -ForegroundColor White
Write-Host ""
