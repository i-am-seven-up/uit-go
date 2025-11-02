using DriverService.Application.Abstractions;


namespace DriverService.Application.Services
{
    public sealed class DriverService : IDriverService
    {
        private readonly IDriverRepository _repo;
        private readonly DriverLocationService _locationSvc;

        public DriverService(IDriverRepository repo, DriverLocationService locationSvc)
        {
            _repo = repo;
            _locationSvc = locationSvc;
        }

        public async Task SetOnlineAsync(Guid id, bool online, CancellationToken ct = default)
        {
            var d = await _repo.GetAsync(id, ct) ?? new Domain.Domain.Driver { Id = id };
            d.Online = online;
            d.UpdatedAt = DateTime.UtcNow;

            await _repo.UpsertAsync(d, ct);

            // cập nhật Redis trạng thái online/offline
            // online=true => online=1, false => online=0
            await _locationSvc.UpdateLocationAsync(id, d.Lat, d.Lng);
            // nếu offline thì có thể future: remove khỏi GEO, nhưng để đơn giản phase này cứ giữ nguyên
        }

        public async Task UpdateLocationAsync(Guid id, double lat, double lng, CancellationToken ct = default)
        {
            var d = await _repo.GetAsync(id, ct) ?? new Domain.Domain.Driver { Id = id };
            d.Lat = lat;
            d.Lng = lng;
            d.UpdatedAt = DateTime.UtcNow;

            await _repo.UpsertAsync(d, ct);

            // đẩy vị trí (lng,lat) vào Redis GEO
            await _locationSvc.UpdateLocationAsync(id, lat, lng);
        }
    }
}
