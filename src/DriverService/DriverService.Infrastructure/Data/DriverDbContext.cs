using DriverService.Domain.Domain;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace DriverService.Infrastructure.Data
{
    public class DriverDbContext : DbContext
    {
        public DriverDbContext(DbContextOptions<DriverDbContext> opt) : base(opt) { }

        public DbSet<Driver> Drivers => Set<Driver>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Driver>(e =>
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
