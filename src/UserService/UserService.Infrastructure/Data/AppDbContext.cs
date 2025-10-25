using Microsoft.EntityFrameworkCore;
using UserService.Domain.Entities;
namespace UserService.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> opt) : base(opt) { }
        public DbSet<User> Users{ get; set; }

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<User>(e =>
            {
                e.HasIndex(x => x.Email).IsUnique();
                e.Property(x => x.Username).HasMaxLength(320).IsRequired();
                e.Property(x => x.PasswordHash).IsRequired();
                e.Property(x => x.FullName).HasMaxLength(150);
            });
        }
    }
}
