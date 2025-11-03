using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TripService.Application.Abstractions
{
    public interface IOfferStore
    {
        Task SetPendingAsync(Guid tripId, Guid driverId, TimeSpan ttl, CancellationToken ct = default);
        Task<bool> ExistsAsync(Guid tripId, Guid driverId, CancellationToken ct = default);
        Task MarkDeclinedAsync(Guid tripId, Guid driverId, CancellationToken ct = default);
        Task<bool> IsDeclinedAsync(Guid tripId, Guid driverId, CancellationToken ct = default);
    }
}
