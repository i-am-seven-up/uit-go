using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TripService.Infrastructure.Data
{
    public class TripDbContextFactory : IDesignTimeDbContextFactory<TripDbContext>
    {
        public TripDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<TripDbContext>();
            optionsBuilder.UseNpgsql("Host=localhost;Port=5434;Database=uitgo_trip;Username=postgres;Password=postgres");

            return new TripDbContext(optionsBuilder.Options);
        }
    }
}
