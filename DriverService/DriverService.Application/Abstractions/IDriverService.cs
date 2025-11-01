using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriverService.Application.Abstractions
{
    public interface IDriverService
    {
        Task SetOnlineAsync(Guid id, bool online, CancellationToken ct = default);
        Task UpdateLocationAsync(Guid id, double lat, double lng, CancellationToken ct = default);
    }
}
