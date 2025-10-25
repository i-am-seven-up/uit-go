using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripService.Domain.Entities;

namespace TripService.Application.Abstractions
{
    public interface ITripService
    {
        Task<Trip> CreateAsync(Trip trip, CancellationToken ct = default);
        Task<Trip?> GetAsync(Guid id, CancellationToken ct = default);
        Task CancelAsync(Guid id, CancellationToken ct = default);
    }
}
