using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripService.Domain.Entities;

namespace TripService.Application.Abstractions
{
    public interface ITripRepository
    {
        Task<Trip?> GetAsync(Guid id, CancellationToken ct = default);
        Task AddAsync(Trip trip, CancellationToken ct = default);
        Task UpdateAsync(Trip trip, CancellationToken ct = default);
    }
}
