# PowerShell script to run integration tests for uit-go
# This script runs all integration tests sequentially

param(
    [switch]$Verbose,
    [switch]$NoBuild,
    [string]$Filter = ""
)

Write-Host "=== uit-go Integration Test Runner ===" -ForegroundColor Cyan
Write-Host ""

# Set error action preference
$ErrorActionPreference = "Stop"

# Function to run tests for a project
function Run-TestProject {
    param(
        [string]$ProjectPath,
        [string]$ProjectName
    )

    Write-Host "[$ProjectName] Running tests..." -ForegroundColor Yellow

    $testArgs = @(
        "test"
        $ProjectPath
        "--logger", "console;verbosity=normal"
    )

    if ($NoBuild) {
        $testArgs += "--no-build"
    }

    if ($Verbose) {
        $testArgs += "--verbosity", "detailed"
    }

    if ($Filter) {
        $testArgs += "--filter", $Filter
    }

    try {
        & dotnet @testArgs

        if ($LASTEXITCODE -eq 0) {
            Write-Host "[$ProjectName] ✓ All tests passed!" -ForegroundColor Green
            return $true
        } else {
            Write-Host "[$ProjectName] ✗ Some tests failed!" -ForegroundColor Red
            return $false
        }
    } catch {
        Write-Host "[$ProjectName] ✗ Error running tests: $_" -ForegroundColor Red
        return $false
    } finally {
        # Optional: cleanup or final logging
    }
}

# Check if dotnet is installed
try {
    $dotnetVersion = dotnet --version
    Write-Host "Using .NET SDK version: $dotnetVersion" -ForegroundColor Gray
    Write-Host ""
} catch {
    Write-Host "Error: .NET SDK not found. Please install .NET 10.0 or higher." -ForegroundColor Red
    exit 1
}

# Check Docker is running (for Testcontainers)
try {
    docker version | Out-Null
    Write-Host "Docker is running ✓" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "Error: Docker is not running. Testcontainers require Docker to be running." -ForegroundColor Red
    Write-Host "Please start Docker Desktop and try again." -ForegroundColor Yellow
    exit 1
}

# Build solution first (unless --no-build is specified)
if (-not $NoBuild) {
    Write-Host "Building TripService solution..." -ForegroundColor Yellow
    dotnet build "TripService\TripService.sln" -c Release

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }

    Write-Host "Build succeeded ✓" -ForegroundColor Green
    Write-Host ""
}

# Track results
$results = @()

# Run TripService Integration Tests
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  TripService Integration Tests" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$tripServicePassed = Run-TestProject -ProjectPath "TripService\TripService.IntegrationTests\TripService.IntegrationTests.csproj" -ProjectName "TripService"

$results += @{
    Project = "TripService.IntegrationTests"
    Passed = $tripServicePassed
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan

# Summary
Write-Host ""
Write-Host "=== Test Summary ===" -ForegroundColor Cyan
Write-Host ""

$totalProjects = $results.Count
$passedProjects = ($results | Where-Object { $_.Passed }).Count
$failedProjects = $totalProjects - $passedProjects

foreach ($result in $results) {
    $status = if ($result.Passed) { "✓ PASSED" } else { "✗ FAILED" }
    $color = if ($result.Passed) { "Green" } else { "Red" }
    Write-Host "  $($result.Project): $status" -ForegroundColor $color
}

Write-Host ""
Write-Host "Total: $totalProjects | Passed: $passedProjects | Failed: $failedProjects" -ForegroundColor $(if ($failedProjects -eq 0) { "Green" } else { "Red" })
Write-Host ""

# Exit with appropriate code
if ($failedProjects -eq 0) {
    Write-Host "All tests passed! 🎉" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Some tests failed. Please review the output above." -ForegroundColor Red
    exit 1
}