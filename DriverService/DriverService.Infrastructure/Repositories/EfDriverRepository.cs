using DriverService.Application.Abstractions;
using DriverService.Domain.Domain;
using DriverService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriverService.Infrastructure.Repositories
{
    public sealed class EfDriverRepository : IDriverRepository
    {
        private readonly DriverDbContext _db;
        public EfDriverRepository(DriverDbContext db) => _db = db;

        public Task<DriverService.Domain.Domain.Driver?> GetAsync(Guid id, CancellationToken ct = default) =>
            _db.Drivers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        public async Task UpsertAsync(DriverService.Domain.Domain.Driver driver, CancellationToken ct = default)
        {
            var exist = await _db.Drivers.FirstOrDefaultAsync(x => x.Id == driver.Id, ct);
            if (exist is null)
                _db.Drivers.Add(driver);
            else
                _db.Entry(exist).CurrentValues.SetValues(driver);

            await _db.SaveChangesAsync(ct);
        }
    }
}
