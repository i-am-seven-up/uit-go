using DriverService.Application.Abstractions;
using DriverService.Domain.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriverService.Application.Services
{
    public sealed class DriverService : IDriverService
    {
        private readonly IDriverRepository _repo;
        public DriverService(IDriverRepository repo) => _repo = repo;

        public async Task SetOnlineAsync(Guid id, bool online, CancellationToken ct = default)
        {
            var d = await _repo.GetAsync(id, ct) ?? new Driver { Id = id };
            d.Online = online; d.UpdatedAt = DateTime.UtcNow;
            await _repo.UpsertAsync(d, ct);
        }

        public async Task UpdateLocationAsync(Guid id, double lat, double lng, CancellationToken ct = default)
        {
            var d = await _repo.GetAsync(id, ct) ?? new Driver { Id = id };
            d.Lat = lat; d.Lng = lng; d.UpdatedAt = DateTime.UtcNow;
            await _repo.UpsertAsync(d, ct);
        }
    }
}
