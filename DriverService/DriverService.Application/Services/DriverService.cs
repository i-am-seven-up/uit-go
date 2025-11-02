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

            if (online)
            {
                // Lấy tên + toạ độ hiện có trong DB để seed vào Redis
                await _locationSvc.SetOnlineAsync(id, d.FullName ?? "", d.Lat, d.Lng);
            }
            else
            {
                await _locationSvc.SetOfflineAsync(id);
            }
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
