# Integration Test Instructions

This document provides instructions for running the automated integration tests for the uit-go ride-hailing system.

## Overview

The integration tests are built with:
- **xUnit** - Testing framework
- **Testcontainers** - Provides isolated PostgreSQL, Redis, and RabbitMQ instances
- **FluentAssertions** - Expressive assertion library
- **WebApplicationFactory** - ASP.NET Core integration testing

## Prerequisites

### Required Software

1. **.NET 10.0 SDK or higher**
   - Check version: `dotnet --version`
   - Download: https://dotnet.microsoft.com/download

2. **Docker Desktop**
   - Must be running before executing tests
   - Testcontainers will automatically start PostgreSQL, Redis, and RabbitMQ containers
   - Download: https://www.docker.com/products/docker-desktop

### Verify Prerequisites

```powershell
# Check .NET SDK
dotnet --version

# Check Docker
docker version
```

## Running Tests

### Option 1: Using the Test Runner Scripts (Recommended)

#### Windows (PowerShell)

```powershell
# Run all tests
.\run-tests.ps1

# Run with verbose output
.\run-tests.ps1 -Verbose

# Run without rebuilding (faster for subsequent runs)
.\run-tests.ps1 -NoBuild

# Run specific test class or method
.\run-tests.ps1 -Filter "TripCreationTests"
.\run-tests.ps1 -Filter "CreateTrip_WithDriverOnline_ShouldAssignDriver"
```

#### Linux/macOS (Bash)

```bash
# Make script executable (first time only)
chmod +x run-tests.sh

# Run all tests
./run-tests.sh

# Run with verbose output
./run-tests.sh --verbose

# Run without rebuilding
./run-tests.sh --no-build

# Run specific test
./run-tests.sh --filter "TripCreationTests"
```

### Option 2: Using dotnet CLI

```powershell
# Run TripService tests
dotnet test TripService\TripService.IntegrationTests\TripService.IntegrationTests.csproj

# Run with detailed output
dotnet test TripService\TripService.IntegrationTests\TripService.IntegrationTests.csproj --verbosity detailed

# Run specific test class
dotnet test TripService\TripService.IntegrationTests\TripService.IntegrationTests.csproj --filter "FullyQualifiedName~TripCreationTests"

# Run specific test method
dotnet test TripService\TripService.IntegrationTests\TripService.IntegrationTests.csproj --filter "FullyQualifiedName~CreateTrip_WithDriverOnline_ShouldAssignDriver"
```

### Option 3: Using Visual Studio

1. Open `uit-go.sln` in Visual Studio
2. Open **Test Explorer** (Test → Test Explorer)
3. Click "Run All Tests" or right-click specific tests to run

### Option 4: Using Visual Studio Code

1. Install the "C# Dev Kit" extension
2. Open the Testing panel (beaker icon in sidebar)
3. Click "Run All Tests" or run individual tests

## Test Structure

### TripService.IntegrationTests

```
TripService.IntegrationTests/
├── Infrastructure/
│   ├── TripServiceWebApplicationFactory.cs  # Test server setup
│   ├── TestAuthHandler.cs                    # Authentication for tests
│   └── JwtTokenHelper.cs                     # JWT token generation
├── TripCreationTests.cs                      # Trip creation scenarios
├── DriverResponseTests.cs                    # Driver accept/decline flows
└── TripCancellationTests.cs                  # Cancellation logic
```

## Test Coverage

### TripCreationTests
- ✅ Create trip with no drivers available
- ✅ Create trip with driver online → assigns driver
- ✅ Create trip with multiple drivers → assigns nearest
- ✅ Concurrent trip creation → prevents double-booking

### DriverResponseTests
- ✅ Driver accepts trip → status changes to DriverAccepted
- ✅ Driver declines trip → retries with next driver
- ✅ Three drivers decline → status changes to NoDriverAvailable
- ✅ Accept already accepted trip → no state change

### TripCancellationTests
- ✅ Passenger cancels own trip → succeeds
- ✅ Cancellation releases assigned driver
- ✅ Non-owner cannot cancel trip
- ✅ Cannot cancel completed trip
- ✅ Can cancel after driver acceptance

## Understanding Test Output

### Successful Test Run
```
[TripService] Running tests...
  Passed TripCreationTests.CreateTrip_WithNoDriversOnline_ShouldReturnNoDriverAvailable
  Passed TripCreationTests.CreateTrip_WithDriverOnline_ShouldAssignDriver
  ...
[TripService] ✓ All tests passed!

=== Test Summary ===
  TripService.IntegrationTests: ✓ PASSED

Total: 1 | Passed: 1 | Failed: 0

All tests passed! 🎉
```

### Failed Test Run
```
[TripService] Running tests...
  Passed TripCreationTests.CreateTrip_WithNoDriversOnline_ShouldReturnNoDriverAvailable
  Failed TripCreationTests.CreateTrip_WithDriverOnline_ShouldAssignDriver
    Error Message:
     Expected trip.Status to be "DriverAssigned", but found "NoDriverAvailable"
    Stack Trace:
     ...
[TripService] ✗ Some tests failed!
```

## Troubleshooting

### Issue: Docker not running

**Error**: `Error: Docker is not running. Testcontainers require Docker to be running.`

**Solution**: Start Docker Desktop and ensure it's fully running before executing tests.

### Issue: Tests are slow

**Reason**: Testcontainers need to pull Docker images (postgres, redis, rabbitmq) on first run.

**Solution**:
- First run will be slower as images are downloaded
- Subsequent runs will be faster as images are cached
- Use `-NoBuild` / `--no-build` flag to skip rebuilding

### Issue: Port conflicts

**Error**: Container fails to start due to port already in use

**Solution**:
- Testcontainers automatically assigns random ports
- If issues persist, ensure no containers are running: `docker ps`
- Stop all containers: `docker stop $(docker ps -aq)`

### Issue: Test timeout

**Error**: Test times out after 30 seconds

**Reason**: Event-driven architecture requires time for message processing

**Solution**: Tests include appropriate delays (`await Task.Delay(...)`) for event processing. If tests consistently timeout, check:
- RabbitMQ container is running properly
- Message consumers are registered
- Database migrations completed successfully

### Issue: Authentication failures

**Error**: HTTP 401 Unauthorized

**Solution**: Tests use `JwtTokenHelper` to generate valid tokens. Ensure:
- JWT configuration matches `appsettings.json`
- Token includes correct claims (NameIdentifier, Role)

### Issue: Database state issues

**Error**: Tests fail due to existing data

**Solution**: Each test class implements `IAsyncLifetime`:
- `InitializeAsync()` - Cleans database and Redis before each test
- Tests should be isolated and not depend on each other

## Running Tests in CI/CD

### GitHub Actions Example

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'

    - name: Run Integration Tests
      run: |
        chmod +x run-tests.sh
        ./run-tests.sh
```

### Azure DevOps Example

```yaml
trigger:
- main

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: UseDotNet@2
  inputs:
    version: '10.0.x'

- script: |
    chmod +x run-tests.sh
    ./run-tests.sh
  displayName: 'Run Integration Tests'
```

## Performance Considerations

### Test Execution Time

Average execution time per test class:
- `TripCreationTests`: ~30-40 seconds
- `DriverResponseTests`: ~50-60 seconds (includes event processing delays)
- `TripCancellationTests`: ~40-50 seconds

Total: ~2-3 minutes for all tests

### Optimization Tips

1. **Use `--no-build` flag** for subsequent test runs
2. **Run specific test classes** during development using `--filter`
3. **Parallel execution**: xUnit runs tests in parallel by default within a class
4. **Keep Docker images cached**: Don't delete testcontainer images

## Best Practices

### When Adding New Tests

1. **Use descriptive test names**: `MethodName_Scenario_ExpectedResult`
2. **Follow AAA pattern**: Arrange, Act, Assert
3. **Clean up state**: Use `IAsyncLifetime` to clean database/Redis
4. **Use FluentAssertions**: More readable than traditional assertions
5. **Include delays for async operations**: Event processing needs time
6. **Log important information**: Use `ITestOutputHelper` for debugging

### Example Test Structure

```csharp
[Fact]
public async Task CreateTrip_WithDriverOnline_ShouldAssignDriver()
{
    // Arrange - Set up test data
    var driverId = Guid.NewGuid();
    await SetDriverOnline(driverId);

    // Act - Execute the operation
    var response = await CreateTrip();

    // Assert - Verify the result
    var trip = await response.Content.ReadFromJsonAsync<TripResponse>();
    trip.Should().NotBeNull();
    trip!.Status.Should().Be("DriverAssigned");
    trip.AssignedDriverId.Should().Be(driverId);

    _output.WriteLine($"Trip {trip.Id} assigned to driver {driverId}");
}
```

## Next Steps

1. **Run the tests** to verify your environment is set up correctly
2. **Review test output** to understand what's being tested
3. **Add new tests** as you implement new features
4. **Integrate into CI/CD** for automated testing on every commit

## Support

If you encounter issues:
1. Check the troubleshooting section above
2. Review test output for specific error messages
3. Check Docker and .NET SDK versions
4. Ensure all prerequisites are installed and running

---

**Remember**: Integration tests require Docker to be running. Always start Docker Desktop before running tests!
