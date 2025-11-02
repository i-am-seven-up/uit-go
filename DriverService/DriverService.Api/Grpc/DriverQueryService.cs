using Driver;
using Grpc.Core;
using StackExchange.Redis;

namespace DriverService.Api.Grpc
{
    public class DriverQueryService : DriverQuery.DriverQueryBase
    {
        private readonly IConnectionMultiplexer _redis;

        public DriverQueryService(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public override async Task<GetDriverInfoResponse> GetDriverInfo(GetDriverInfoRequest request, ServerCallContext context)
        {
            var db = _redis.GetDatabase();
            var key = $"driver:{request.DriverId}";
            var hash = await db.HashGetAllAsync(key);

            if (hash == null || hash.Length == 0)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Driver not found"));
            }

            var latStr = hash.FirstOrDefault(x => x.Name == "lat").Value;
            var lngStr = hash.FirstOrDefault(x => x.Name == "lng").Value;
            var availStr = hash.FirstOrDefault(x => x.Name == "available").Value;
            var nameStr = hash.FirstOrDefault(x => x.Name == "name").Value;

            return new GetDriverInfoResponse
            {
                DriverId = request.DriverId,
                Name = nameStr.HasValue ? nameStr.ToString() : "",
                Lat = latStr.HasValue ? double.Parse(latStr!) : 0,
                Lng = lngStr.HasValue ? double.Parse(lngStr!) : 0,
                Available = availStr == "1"
            };
        }

        public override async Task<MarkTripAssignedResponse> MarkTripAssigned(MarkTripAssignedRequest request, ServerCallContext context)
        {
            var db = _redis.GetDatabase();
            var key = $"driver:{request.DriverId}";

            // đánh dấu tài xế bận
            await db.HashSetAsync(key, new HashEntry[] {
                new HashEntry("available", "0"),
                new HashEntry("current_trip_id", request.TripId)
            });

            return new MarkTripAssignedResponse { Success = true };
        }
    }
}
