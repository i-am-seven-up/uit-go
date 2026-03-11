using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Net;
using System.Net.Http.Json;
using TripService.Domain.Entities;
using TripService.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace TripService.IntegrationTests;

[Collection("Integration Tests")]
public class TripCancellationTests : IAsyncLifetime
{
    private readonly TripServiceWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;
    private readonly Guid _passengerId = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");
    private readonly Guid _driverId = Guid.Parse("550e8400-e29b-41d4-a716-446655440002");

    public TripCancellationTests(TripServiceWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var dbContext = _factory.GetDbContext();
        var trips = await dbContext.Trips.ToListAsync();
        dbContext.Trips.RemoveRange(trips);
        await dbContext.SaveChangesAsync();

        var redis = _factory.GetRedisConnection();
        var server = redis.GetServer(redis.GetEndPoints().First());
        await server.FlushDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CancelTrip_ByOwner_ShouldSucceed()
    {
        // Arrange - Create a trip
        var trip = await CreateTripWithAssignedDriver();
        await Task.Delay(1000);

        // Act - Cancel trip
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var cancelResponse = await client.PostAsJsonAsync($"/api/trips/{trip.Id}/cancel", new
        {
            reason = "Changed my mind"
        });

        // Assert
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Wait for event processing
        await Task.Delay(2000);

        // Verify trip status
        var dbContext = _factory.GetDbContext();
        var cancelledTrip = await dbContext.Trips.FirstOrDefaultAsync(t => t.Id == trip.Id);

        cancelledTrip.Should().NotBeNull();
        cancelledTrip!.Status.Should().Be(TripStatus.Cancelled);
        cancelledTrip.CancelledAt.Should().NotBeNull();
        cancelledTrip.CancellationReason.Should().Be("Changed my mind");

        _output.WriteLine($"Trip {trip.Id} cancelled successfully");
    }

    [Fact]
    public async Task CancelTrip_ShouldReleaseDriver()
    {
        // Arrange - Create a trip with assigned driver
        var trip = await CreateTripWithAssignedDriver();
        await Task.Delay(1000);

        // Verify driver is locked
        var redis = _factory.GetRedisConnection();
        var db = redis.GetDatabase();
        var driverAvailable = await db.HashGetAsync($"driver:{_driverId}", "available");
        driverAvailable.ToString().Should().Be("0", "driver should be locked initially");

        // Act - Cancel trip
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        await client.PostAsJsonAsync($"/api/trips/{trip.Id}/cancel", new
        {
            reason = "Test cancellation"
        });

        // Wait for MockTripCancelledConsumer to process the event and release driver
        await Task.Delay(3000);

        // Assert - Driver should be released
        var driverAvailableAfter = await db.HashGetAsync($"driver:{_driverId}", "available");
        driverAvailableAfter.ToString().Should().Be("1", "driver should be available after cancellation");

        var currentTripId = await db.HashGetAsync($"driver:{_driverId}", "current_trip_id");
        currentTripId.ToString().Should().BeEmpty("driver should have no current trip");

        _output.WriteLine($"Driver {_driverId} released after trip cancellation");
    }

    [Fact]
    public async Task CancelTrip_ByNonOwner_ShouldFail()
    {
        // Arrange - Create a trip
        var trip = await CreateTripWithAssignedDriver();
        await Task.Delay(1000);

        var otherPassengerId = Guid.NewGuid();

        // Act - Try to cancel with different passenger
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateToken(otherPassengerId, "passenger");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var cancelResponse = await client.PostAsJsonAsync($"/api/trips/{trip.Id}/cancel", new
        {
            reason = "Unauthorized attempt"
        });

        // Assert
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Verify trip is not cancelled
        var dbContext = _factory.GetDbContext();
        var unchangedTrip = await dbContext.Trips.FirstOrDefaultAsync(t => t.Id == trip.Id);

        unchangedTrip.Should().NotBeNull();
        unchangedTrip!.Status.Should().NotBe(TripStatus.Cancelled);
        unchangedTrip.CancelledAt.Should().BeNull();

        _output.WriteLine($"Cancellation rejected for non-owner");
    }

    [Fact]
    public async Task CancelTrip_WhenAlreadyCompleted_ShouldFail()
    {
        // Arrange - Create a trip and manually set to completed
        var trip = await CreateTripWithAssignedDriver();
        await Task.Delay(1000);

        var dbContext = _factory.GetDbContext();
        var tripEntity = await dbContext.Trips.FirstAsync(t => t.Id == trip.Id);

        // Manually transition to completed (simulating completed trip)
        tripEntity.TransitionTo(TripStatus.DriverAccepted);
        tripEntity.TransitionTo(TripStatus.DriverOnTheWay);
        tripEntity.TransitionTo(TripStatus.DriverArrived);
        tripEntity.TransitionTo(TripStatus.InProgress);
        tripEntity.TransitionTo(TripStatus.Completed);
        tripEntity.TripCompletedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        // Act - Try to cancel completed trip
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var cancelResponse = await client.PostAsJsonAsync($"/api/trips/{trip.Id}/cancel", new
        {
            reason = "Trying to cancel completed trip"
        });

        // Assert
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify trip remains completed
        await dbContext.Entry(tripEntity).ReloadAsync();
        tripEntity.Status.Should().Be(TripStatus.Completed);

        _output.WriteLine($"Completed trip cannot be cancelled");
    }

    [Fact]
    public async Task CancelTrip_InDriverAcceptedState_ShouldSucceed()
    {
        // Arrange - Create trip and have driver accept it
        var trip = await CreateTripWithAssignedDriver();
        await Task.Delay(1000);

        // Driver accepts using simulator (simulates DriverService publishing DriverAcceptedTrip event)
        var driverSimulator = new DriverSimulator(_factory);
        await driverSimulator.AcceptTripAsync(trip.Id, _driverId);

        await Task.Delay(2000);

        // Verify trip is in DriverAccepted state
        var dbContext = _factory.GetDbContext();
        var acceptedTrip = await dbContext.Trips.FirstAsync(t => t.Id == trip.Id);
        acceptedTrip.Status.Should().Be(TripStatus.DriverAccepted);

        // Act - Cancel the trip
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var cancelResponse = await client.PostAsJsonAsync($"/api/trips/{trip.Id}/cancel", new
        {
            reason = "Emergency cancellation"
        });

        // Assert
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await Task.Delay(2000);

        await dbContext.Entry(acceptedTrip).ReloadAsync();
        acceptedTrip.Status.Should().Be(TripStatus.Cancelled);
        acceptedTrip.CancellationReason.Should().Be("Emergency cancellation");

        _output.WriteLine($"Trip cancelled successfully even after driver acceptance");
    }

    private async Task<TripResponse> CreateTripWithAssignedDriver()
    {
        // Set driver online
        var redis = _factory.GetRedisConnection();
        var db = redis.GetDatabase();

        await db.GeoAddAsync("drivers:online", 106.660172, 10.762622, _driverId.ToString());
        await db.HashSetAsync($"driver:{_driverId}", new[]
        {
            new HashEntry("available", "1"),
            new HashEntry("current_trip_id", "")
        });

        // Create trip
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.PostAsJsonAsync("/api/trips", new
        {
            pickupLat = 10.762622,
            pickupLng = 106.660172,
            dropoffLat = 10.773996,
            dropoffLng = 106.697214
        });

        var trip = await response.Content.ReadFromJsonAsync<TripResponse>();
        return trip!;
    }

    private class TripResponse
    {
        public Guid Id { get; set; }
        public Guid PassengerId { get; set; }
        public Guid? AssignedDriverId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
