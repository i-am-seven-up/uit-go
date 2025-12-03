using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TripService.Domain.Entities;
using TripService.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace TripService.IntegrationTests;

public class TripCreationTests : IClassFixture<TripServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TripServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;
    private readonly Guid _passengerId = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");
    private readonly Guid _driverId = Guid.Parse("550e8400-e29b-41d4-a716-446655440002");

    public TripCreationTests(TripServiceWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Clean up database before each test
        var dbContext = _factory.GetDbContext();
        var trips = await dbContext.Trips.ToListAsync();
        dbContext.Trips.RemoveRange(trips);
        await dbContext.SaveChangesAsync();

        // Clean up Redis
        var redis = _factory.GetRedisConnection();
        var server = redis.GetServer(redis.GetEndPoints().First());
        await server.FlushDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateTrip_WithNoDriversOnline_ShouldReturnNoDriverAvailable()
    {
        // Arrange
        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new
        {
            pickupLat = 10.762622,
            pickupLng = 106.660172,
            dropoffLat = 10.773996,
            dropoffLng = 106.697214
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/trips", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var trip = await response.Content.ReadFromJsonAsync<TripResponse>();
        trip.Should().NotBeNull();
        trip!.PassengerId.Should().Be(_passengerId);
        trip.Status.Should().Be("NoDriverAvailable");
        trip.AssignedDriverId.Should().BeNull();

        _output.WriteLine($"Trip created with status: {trip.Status}");
    }

    [Fact]
    public async Task CreateTrip_WithDriverOnline_ShouldAssignDriver()
    {
        // Arrange - Set driver online in Redis
        var redis = _factory.GetRedisConnection();
        var db = redis.GetDatabase();

        // Add driver to geospatial index
        await db.GeoAddAsync("drivers:online", 106.660172, 10.762622, _driverId.ToString());

        // Set driver as available
        await db.HashSetAsync($"driver:{_driverId}", new[]
        {
            new HashEntry("available", "1"),
            new HashEntry("current_trip_id", "")
        });

        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new
        {
            pickupLat = 10.762622,
            pickupLng = 106.660172,
            dropoffLat = 10.773996,
            dropoffLng = 106.697214
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/trips", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var trip = await response.Content.ReadFromJsonAsync<TripResponse>();
        trip.Should().NotBeNull();
        trip!.Status.Should().Be("DriverAssigned");
        trip.AssignedDriverId.Should().Be(_driverId);
        trip.DriverAssignedAt.Should().NotBeNull();
        trip.DriverRetryCount.Should().Be(1);

        _output.WriteLine($"Trip {trip.Id} assigned to driver {trip.AssignedDriverId}");

        // Verify driver is locked in Redis
        var driverAvailable = await db.HashGetAsync($"driver:{_driverId}", "available");
        driverAvailable.ToString().Should().Be("0");

        var currentTripId = await db.HashGetAsync($"driver:{_driverId}", "current_trip_id");
        currentTripId.ToString().Should().Be(trip.Id.ToString());
    }

    [Fact]
    public async Task CreateTrip_WithMultipleDrivers_ShouldAssignNearestDriver()
    {
        // Arrange - Set multiple drivers online
        var redis = _factory.GetRedisConnection();
        var db = redis.GetDatabase();

        var driver1Id = Guid.NewGuid();
        var driver2Id = Guid.NewGuid();

        // Driver 1 - closer to pickup (10.762622, 106.660172)
        await db.GeoAddAsync("drivers:online", 106.660172, 10.762622, driver1Id.ToString());
        await db.HashSetAsync($"driver:{driver1Id}", new[]
        {
            new HashEntry("available", "1"),
            new HashEntry("current_trip_id", "")
        });

        // Driver 2 - further from pickup
        await db.GeoAddAsync("drivers:online", 106.670000, 10.770000, driver2Id.ToString());
        await db.HashSetAsync($"driver:{driver2Id}", new[]
        {
            new HashEntry("available", "1"),
            new HashEntry("current_trip_id", "")
        });

        var token = JwtTokenHelper.GenerateToken(_passengerId, "passenger");
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var request = new
        {
            pickupLat = 10.762622,
            pickupLng = 106.660172,
            dropoffLat = 10.773996,
            dropoffLng = 106.697214
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/trips", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var trip = await response.Content.ReadFromJsonAsync<TripResponse>();
        trip.Should().NotBeNull();
        trip!.AssignedDriverId.Should().Be(driver1Id, "the nearest driver should be assigned");

        _output.WriteLine($"Nearest driver {driver1Id} was assigned out of {driver1Id} and {driver2Id}");
    }

    [Fact]
    public async Task CreateTrip_Concurrently_ShouldNotAssignSameDriverToMultipleTrips()
    {
        // Arrange - Set one driver online
        var redis = _factory.GetRedisConnection();
        var db = redis.GetDatabase();

        await db.GeoAddAsync("drivers:online", 106.660172, 10.762622, _driverId.ToString());
        await db.HashSetAsync($"driver:{_driverId}", new[]
        {
            new HashEntry("available", "1"),
            new HashEntry("current_trip_id", "")
        });

        var passenger1Id = Guid.NewGuid();
        var passenger2Id = Guid.NewGuid();

        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();

        var token1 = JwtTokenHelper.GenerateToken(passenger1Id, "passenger");
        var token2 = JwtTokenHelper.GenerateToken(passenger2Id, "passenger");

        client1.DefaultRequestHeaders.Add("Authorization", $"Bearer {token1}");
        client2.DefaultRequestHeaders.Add("Authorization", $"Bearer {token2}");

        var request = new
        {
            pickupLat = 10.762622,
            pickupLng = 106.660172,
            dropoffLat = 10.773996,
            dropoffLng = 106.697214
        };

        // Act - Create two trips simultaneously
        var task1 = client1.PostAsJsonAsync("/api/trips", request);
        var task2 = client2.PostAsJsonAsync("/api/trips", request);

        await Task.WhenAll(task1, task2);

        var response1 = await task1;
        var response2 = await task2;

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var trip1 = await response1.Content.ReadFromJsonAsync<TripResponse>();
        var trip2 = await response2.Content.ReadFromJsonAsync<TripResponse>();

        // One should get driver assigned, the other should not
        var assignedTrips = new[] { trip1, trip2 }.Where(t => t?.Status == "DriverAssigned").ToList();
        var noDriverTrips = new[] { trip1, trip2 }.Where(t => t?.Status == "NoDriverAvailable").ToList();

        assignedTrips.Should().HaveCount(1, "only one trip should get the driver");
        noDriverTrips.Should().HaveCount(1, "the other trip should have no driver available");

        _output.WriteLine($"Trip {assignedTrips[0]!.Id} got driver, Trip {noDriverTrips[0]!.Id} did not");
    }

    private class TripResponse
    {
        public Guid Id { get; set; }
        public Guid PassengerId { get; set; }
        public Guid? AssignedDriverId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? DriverAssignedAt { get; set; }
        public int DriverRetryCount { get; set; }
    }
}
