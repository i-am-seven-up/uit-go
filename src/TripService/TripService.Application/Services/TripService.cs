using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripService.Application.Abstractions;
using TripService.Domain.Entities;

namespace TripService.Application.Services
{
    public sealed class TripService : ITripService
    {
        private readonly ITripRepository _repo;
        public TripService(ITripRepository repo) => _repo = repo;

        public async Task<Trip> CreateAsync(Trip trip, CancellationToken ct = default)
        {
            trip.Status = TripStatus.Searching; // demo: chuyển sang tìm tài xế
            await _repo.AddAsync(trip, ct);
            return trip;
        }

        public Task<Trip?> GetAsync(Guid id, CancellationToken ct = default) => _repo.GetAsync(id, ct);

        public async Task CancelAsync(Guid id, CancellationToken ct = default)
        {
            var t = await _repo.GetAsync(id, ct) ?? throw new KeyNotFoundException();
            t.Status = TripStatus.Canceled;
            await _repo.UpdateAsync(t, ct);
        }
    }
}
