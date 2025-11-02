using Microsoft.EntityFrameworkCore;


namespace DriverService.Infrastructure.Data
{
    public class DriverDbContext : DbContext
    {
        public DriverDbContext(DbContextOptions<DriverDbContext> opt) : base(opt) { }

        public DbSet<DriverService.Domain.Domain.Driver> Drivers => Set<DriverService.Domain.Domain.Driver>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<DriverService.Domain.Domain.Driver>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.FullName).HasMaxLength(150);
                e.Property(x => x.Online);
                e.Property(x => x.Lat);
                e.Property(x => x.Lng);
            });
        }
    }
}
