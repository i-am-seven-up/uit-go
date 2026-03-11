#!/bin/bash
# Bash script to run integration tests for uit-go
# This script runs all integration tests sequentially

set -e

VERBOSE=false
NO_BUILD=false
FILTER=""

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --verbose)
            VERBOSE=true
            shift
            ;;
        --no-build)
            NO_BUILD=true
            shift
            ;;
        --filter)
            FILTER="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--verbose] [--no-build] [--filter <pattern>]"
            exit 1
            ;;
    esac
done

echo "=== uit-go Integration Test Runner ==="
echo ""

# Function to run tests for a project
run_test_project() {
    local project_path=$1
    local project_name=$2

    echo "[$project_name] Running tests..."

    test_args="test $project_path --logger console;verbosity=normal"

    if [ "$NO_BUILD" = true ]; then
        test_args="$test_args --no-build"
    fi

    if [ "$VERBOSE" = true ]; then
        test_args="$test_args --verbosity detailed"
    fi

    if [ -n "$FILTER" ]; then
        test_args="$test_args --filter $FILTER"
    fi

    if dotnet $test_args; then
        echo "[$project_name] ✓ All tests passed!"
        return 0
    else
        echo "[$project_name] ✗ Some tests failed!"
        return 1
    fi
}

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK not found. Please install .NET 10.0 or higher."
    exit 1
fi

dotnet_version=$(dotnet --version)
echo "Using .NET SDK version: $dotnet_version"
echo ""

# Check Docker is running
if ! docker version &> /dev/null; then
    echo "Error: Docker is not running. Testcontainers require Docker to be running."
    echo "Please start Docker and try again."
    exit 1
fi

echo "Docker is running ✓"
echo ""

# Build solution first (unless --no-build is specified)
if [ "$NO_BUILD" = false ]; then
    echo "Building TripService solution..."
    dotnet build TripService/TripService.sln -c Release
    echo "Build succeeded ✓"
    echo ""
fi

# Track results
declare -a results
passed_count=0
failed_count=0

# Run TripService Integration Tests
echo "========================================"
echo "  TripService Integration Tests"
echo "========================================"
echo ""

if run_test_project \
    "/app/TripService/TripService.IntegrationTests/TripService.IntegrationTests.csproj" \
    "TripService"; then
    results+=("TripService.IntegrationTests:PASSED")
    ((passed_count++))
else
    results+=("TripService.IntegrationTests:FAILED")
    ((failed_count++))
fi

echo ""
echo "========================================"

# Summary
echo ""
echo "=== Test Summary ==="
echo ""

for result in "${results[@]}"; do
    project="${result%%:*}"
    status="${result##*:}"
    if [ "$status" = "PASSED" ]; then
        echo "  $project: ✓ PASSED"
    else
        echo "  $project: ✗ FAILED"
    fi
done

total=$((passed_count + failed_count))
echo ""
echo "Total: $total | Passed: $passed_count | Failed: $failed_count"
echo ""

# Exit with appropriate code
if [ $failed_count -eq 0 ]; then
    echo "All tests passed! 🎉"
    exit 0
else
    echo "Some tests failed. Please review the output above."
    exit 1
fi
