using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TripService.Domain.Entities;

namespace TripService.Infrastructure.Data
{
    public class TripDbContext : DbContext
    {
        public TripDbContext(DbContextOptions<TripDbContext> opt) : base(opt) { }
        public DbSet<Trip> Trips { get; set; }
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Trip>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.RiderId).IsRequired();

                e.Property(x => x.Status)
                    .HasConversion<string>()
                    .IsRequired();

                // Phase 2 có thể refactor thành GeoPoint VO
                e.Property(x => x.StartLat).IsRequired();
                e.Property(x => x.StartLng).IsRequired();
                e.Property(x => x.EndLat).IsRequired();
                e.Property(x => x.EndLng).IsRequired();
            });
        }
    }
}
