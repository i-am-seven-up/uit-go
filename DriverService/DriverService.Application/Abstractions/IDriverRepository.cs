using DriverService.Domain.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriverService.Application.Abstractions
{
    public interface IDriverRepository
    {
        Task<Driver?> GetAsync(Guid id, CancellationToken ct = default);
        Task UpsertAsync(Driver driver, CancellationToken ct = default);
    }

}
