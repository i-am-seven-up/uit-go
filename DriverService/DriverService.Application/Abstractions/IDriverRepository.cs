namespace DriverService.Application.Abstractions
{
    public interface IDriverRepository
    {
        Task<DriverService.Domain.Domain.Driver?> GetAsync(Guid id, CancellationToken ct = default);
        Task UpsertAsync(DriverService.Domain.Domain.Driver driver, CancellationToken ct = default);
    }

}
