using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Net;
using System.Net.Http.Json;
using TripService.Domain.Entities;
using TripService.IntegrationTests.Infrastructure;
using TripService.Infrastructure.Data;
using Xunit.Abstractions;

namespace TripService.IntegrationTests;

[Collection("Integration Tests")]
public class DriverResponseTests : IAsyncLifetime
{
    private readonly TripServiceWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;
    private readonly Guid _passengerId = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");
    private readonly Guid _driverId = Guid.Parse("550e8400-e29b-41d4-a716-446655440002");

    public DriverResponseTests(TripServiceWebApplicationFactory factory, ITestOutputHelper output)
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
    public async Task DriverAcceptTrip_ShouldUpdateStatusToDriverAccepted()
    {
        // Arrange - Create a trip with assigned driver
        var trip = await CreateTripWithAssignedDriver();

        // Give time for initial events to process
        await Task.Delay(500);

        // Act - Driver accepts the trip (simulates DriverService publishing event)
        await AcceptTrip(trip.Id, _driverId);

        // Wait for event processing
        await Task.Delay(500);

        // Assert - Verify trip status in database
        var dbContext = _factory.GetDbContext();
        var updatedTrip = await dbContext.Trips.FirstOrDefaultAsync(t => t.Id == trip.Id);

        updatedTrip.Should().NotBeNull();
        updatedTrip!.Status.Should().Be(TripStatus.DriverAccepted);
        updatedTrip.DriverAcceptedAt.Should().NotBeNull();
        updatedTrip.AssignedDriverId.Should().Be(_driverId);

        _output.WriteLine($"Trip {trip.Id} accepted by driver {_driverId}");
    }

    [Fact]
    public async Task DriverDeclineTrip_WithOtherDriversAvailable_ShouldRetryWithNextDriver()
    {
        // Arrange - Set up two drivers
        var driver1Id = Guid.NewGuid();
        var driver2Id = Guid.NewGuid();

        var redis = _factory.GetRedisConnection();
        var db = redis.GetDatabase();

        // Driver 1 - closer
        await db.GeoAddAsync("drivers:online", 106.660172, 10.762622, driver1Id.ToString());
        await db.HashSetAsync($"driver:{driver1Id}", new[]
        {
            new HashEntry("available", "1"),
            new HashEntry("current_trip_id", "")
        });

        // Driver 2 - further
        await db.GeoAddAsync("drivers:online", 106.665000, 10.765000, driver2Id.ToString());
        await db.HashSetAsync($"driver:{driver2Id}", new[]
        {
            new HashEntry("available", "1"),
            new HashEntry("current_trip_id", "")
        });

        // Create trip (should be assigned to driver1)
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var createResponse = await client.PostAsJsonAsync("/api/trips", new
        {
            pickupLat = 10.762622,
            pickupLng = 106.660172,
            dropoffLat = 10.773996,
            dropoffLng = 106.697214
        });

        var trip = await createResponse.Content.ReadFromJsonAsync<TripResponse>();
        trip!.AssignedDriverId.Should().Be(driver1Id);

        await Task.Delay(500);

        // Act - Driver 1 declines (simulates DriverService publishing events)
        await DeclineTrip(trip.Id, driver1Id);

        // Wait for retry logic to process
        await Task.Delay(1000);

        // Verify trip was reassigned to driver2
        var dbContext = _factory.GetDbContext();
        var updatedTrip = await dbContext.Trips.FirstOrDefaultAsync(t => t.Id == trip.Id);

        updatedTrip.Should().NotBeNull();
        updatedTrip!.Status.Should().Be(TripStatus.DriverAssigned);
        updatedTrip.AssignedDriverId.Should().Be(driver2Id, "should retry with next driver");
        updatedTrip.DriverRetryCount.Should().Be(2);

        _output.WriteLine($"Trip {trip.Id} declined by {driver1Id}, reassigned to {driver2Id}");
    }

    [Fact]
    public async Task DriverDeclineTrip_ThreeTimes_ShouldMarkAsNoDriverAvailable()
    {
        // Arrange - Set up three drivers
        var driver1Id = Guid.NewGuid();
        var driver2Id = Guid.NewGuid();
        var driver3Id = Guid.NewGuid();

        var redis = _factory.GetRedisConnection();
        var db = redis.GetDatabase();

        foreach (var driverId in new[] { driver1Id, driver2Id, driver3Id })
        {
            await db.GeoAddAsync("drivers:online", 106.660172, 10.762622, driverId.ToString());
            await db.HashSetAsync($"driver:{driverId}", new[]
            {
                new HashEntry("available", "1"),
                new HashEntry("current_trip_id", "")
            });
        }

        // Create trip
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var createResponse = await client.PostAsJsonAsync("/api/trips", new
        {
            pickupLat = 10.762622,
            pickupLng = 106.660172,
            dropoffLat = 10.773996,
            dropoffLng = 106.697214
        });

        var trip = await createResponse.Content.ReadFromJsonAsync<TripResponse>();

        // Act - All three drivers decline
        await Task.Delay(1000);
        await DeclineTrip(trip!.Id, trip.AssignedDriverId!.Value);

        await Task.Delay(2000);
        var dbContext = _factory.GetDbContext();
        var trip2 = await dbContext.Trips.AsNoTracking().FirstAsync(t => t.Id == trip.Id);
        await DeclineTrip(trip.Id, trip2.AssignedDriverId!.Value);

        await Task.Delay(2000);
        var trip3 = await dbContext.Trips.AsNoTracking().FirstAsync(t => t.Id == trip.Id);
        await DeclineTrip(trip.Id, trip3.AssignedDriverId!.Value);

        // Assert
        await Task.Delay(2000);

        var finalTrip = await dbContext.Trips.FirstOrDefaultAsync(t => t.Id == trip.Id);

        finalTrip.Should().NotBeNull();
        finalTrip!.Status.Should().Be(TripStatus.NoDriverAvailable);
        finalTrip.AssignedDriverId.Should().BeNull();
        finalTrip.DriverRetryCount.Should().Be(3);

        _output.WriteLine($"Trip {trip.Id} marked as NoDriverAvailable after 3 declines");
    }

    [Fact]
    public async Task AcceptTrip_WhenAlreadyAccepted_ShouldNotChangeState()
    {
        // Arrange - Create trip and accept it
        var trip = await CreateTripWithAssignedDriver();
        await Task.Delay(1000);
        await AcceptTrip(trip.Id, _driverId);
        await Task.Delay(2000);

        var dbContext = _factory.GetDbContext();
        var acceptedTrip = await dbContext.Trips.FirstAsync(t => t.Id == trip.Id);
        acceptedTrip.Status.Should().Be(TripStatus.DriverAccepted);
        var firstAcceptedAt = acceptedTrip.DriverAcceptedAt;

        // Act - Try to accept again
        await Task.Delay(500);
        await AcceptTrip(trip.Id, _driverId);
        await Task.Delay(2000);

        // Assert - Should remain in same state
        var finalTrip = await dbContext.Trips.FirstAsync(t => t.Id == trip.Id);
        finalTrip.Status.Should().Be(TripStatus.DriverAccepted);
        finalTrip.DriverAcceptedAt.Should().Be(firstAcceptedAt);

        _output.WriteLine($"Trip {trip.Id} remained in DriverAccepted state");
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

    private async Task AcceptTrip(Guid tripId, Guid driverId)
    {
        // Simulate DriverService publishing DriverAcceptedTrip event
        var simulator = _factory.GetDriverSimulator();
        await simulator.AcceptTripAsync(tripId, driverId);
    }

    private async Task DeclineTrip(Guid tripId, Guid driverId)
    {
        // Simulate DriverService publishing TripOfferDeclined + DriverDeclinedTrip events
        var simulator = _factory.GetDriverSimulator();
        await simulator.DeclineTripAsync(tripId, driverId);
    }

    private class TripResponse
    {
        public Guid Id { get; set; }
        public Guid PassengerId { get; set; }
        public Guid? AssignedDriverId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
