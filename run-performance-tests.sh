#!/bin/bash
# Bash script to run E2E performance tests for UIT-GO
# Supports running all workloads or specific workloads

WORKLOAD="all"
SKIP_BUILD=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -w|--workload)
            WORKLOAD="$2"
            shift 2
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        -h|--help)
            echo "UIT-GO E2E Performance Test Runner"
            echo ""
            echo "Usage: ./run-performance-tests.sh [-w <workload>] [--skip-build]"
            echo ""
            echo "Parameters:"
            echo "  -w, --workload <name>    Specify which workload to run:"
            echo "                           - all (default): Run all workloads sequentially"
            echo "                           - a: Workload A (Trip Creation)"
            echo "                           - b: Workload B (Driver Responses)"
            echo "                           - c: Workload C (Location Updates)"
            echo "  --skip-build             Skip building the project"
            echo "  -h, --help               Show this help message"
            echo ""
            echo "Examples:"
            echo "  ./run-performance-tests.sh                  # Run all workloads"
            echo "  ./run-performance-tests.sh -w a             # Run only Workload A"
            echo "  ./run-performance-tests.sh --skip-build     # Run tests without rebuilding"
            echo ""
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

set -e

echo "╔═══════════════════════════════════════════════════════════╗"
echo "║   UIT-GO E2E PERFORMANCE TEST RUNNER                     ║"
echo "╚═══════════════════════════════════════════════════════════╝"
echo ""

# Check prerequisites
echo "Checking prerequisites..."
echo ""

# Check .NET SDK
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    echo "✓ .NET SDK version: $DOTNET_VERSION"
else
    echo "✗ .NET SDK not found. Please install .NET 8.0 or higher."
    exit 1
fi

# Check Docker
if command -v docker &> /dev/null; then
    if docker version &> /dev/null; then
        echo "✓ Docker is running"
    else
        echo "✗ Docker is not running. Please start Docker."
        exit 1
    fi
else
    echo "✗ Docker not found. Please install Docker."
    exit 1
fi

# Check if services are running
echo ""
echo "Checking if services are running..."

if curl -s -f -o /dev/null http://localhost:8080; then
    echo "✓ API Gateway is running (http://localhost:8080)"
else
    echo "⚠  API Gateway not responding. Please ensure docker-compose is running:"
    echo "   docker-compose up -d"
    echo ""
    read -p "Continue anyway? (y/n) " -n 1 -r
    echo ""
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        exit 1
    fi
fi

echo ""

# Build the test project
if [ "$SKIP_BUILD" = false ]; then
    echo "Building E2E.PerformanceTests..."
    dotnet build E2E.PerformanceTests/E2E.PerformanceTests.csproj -c Release

    if [ $? -ne 0 ]; then
        echo "✗ Build failed!"
        exit 1
    fi

    echo "✓ Build succeeded"
    echo ""
fi

# Run the tests
echo "Starting performance tests..."
echo "Workload: $WORKLOAD"
echo ""

START_TIME=$(date +%s)

dotnet run --project E2E.PerformanceTests/E2E.PerformanceTests.csproj -c Release -- "$WORKLOAD"

if [ $? -eq 0 ]; then
    END_TIME=$(date +%s)
    DURATION=$((END_TIME - START_TIME))
    HOURS=$((DURATION / 3600))
    MINUTES=$(((DURATION % 3600) / 60))
    SECONDS=$((DURATION % 60))

    echo ""
    echo "╔═══════════════════════════════════════════════════════════╗"
    echo "║   PERFORMANCE TESTS COMPLETED                            ║"
    echo "╚═══════════════════════════════════════════════════════════╝"
    echo ""
    printf "Total duration: %02d:%02d:%02d\n" $HOURS $MINUTES $SECONDS
    echo ""
    echo "Results have been exported to JSON files in the current directory."
    echo "Look for files named: results_Workload*_*.json"
    echo ""
else
    echo ""
    echo "✗ Tests failed with exit code: $?"
    exit 1
fi

echo "═══════════════════════════════════════════════════════════"
echo ""
echo "Next steps:"
echo "1. Review the JSON result files for detailed metrics"
echo "2. Compare with Phase 2 results after implementing Module A"
echo "3. Document any bottlenecks or performance issues"
echo ""
