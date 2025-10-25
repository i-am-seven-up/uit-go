using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripService.Application.Abstractions;
using TripService.Domain.Entities;
using TripService.Infrastructure.Data;

namespace TripService.Infrastructure.Repositories
{
    public sealed class EfTripRepository : ITripRepository
    {
        private readonly TripDbContext _db;

        public EfTripRepository(TripDbContext db) => _db = db;

        public async Task<Trip?> GetAsync(Guid id, CancellationToken ct = default)
        {
            return await _db.Trips
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
        }

        public async Task AddAsync(Trip trip, CancellationToken ct = default)
        {
            _db.Trips.Add(trip);
            await _db.SaveChangesAsync(ct);
        }

        public async Task UpdateAsync(Trip trip, CancellationToken ct = default)
        {
            _db.Trips.Update(trip);
            await _db.SaveChangesAsync(ct);
        }
    }
}
