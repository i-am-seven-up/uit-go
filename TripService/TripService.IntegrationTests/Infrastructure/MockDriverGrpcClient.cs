using Driver;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace TripService.IntegrationTests.Infrastructure;

public class MockDriverGrpcClient : DriverQuery.DriverQueryClient
{
    private readonly IServiceProvider _serviceProvider;

    public MockDriverGrpcClient(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override AsyncUnaryCall<GetDriverInfoResponse> GetDriverInfoAsync(
        GetDriverInfoRequest request,
        CallOptions options)
    {
        var response = new GetDriverInfoResponse
        {
            DriverId = request.DriverId,
            Name = "Test Driver",
            Lat = 10.762622,
            Lng = 106.660172,
            Available = true // Always return available for tests
        };

        return new AsyncUnaryCall<GetDriverInfoResponse>(
            Task.FromResult(response),
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });
    }

    public override AsyncUnaryCall<MarkTripAssignedResponse> MarkTripAssignedAsync(
        MarkTripAssignedRequest request,
        CallOptions options)
    {
        // Mark driver as unavailable in Redis when trip is assigned
        var responseTask = Task.Run(async () =>
        {
            var redis = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
            var db = redis.GetDatabase();

            await db.HashSetAsync($"driver:{request.DriverId}", new HashEntry[]
            {
                new("available", "0"),  // Mark as unavailable
                new("current_trip_id", request.TripId)
            });

            return new MarkTripAssignedResponse
            {
                Success = true
            };
        });

        return new AsyncUnaryCall<MarkTripAssignedResponse>(
            responseTask,
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });
    }
}
