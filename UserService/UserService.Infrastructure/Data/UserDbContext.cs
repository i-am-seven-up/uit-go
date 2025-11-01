using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UserService.Domain.Entities;

namespace UserService.Infrastructure.Data
{
    public class UserDbContext : DbContext
    {
        public UserDbContext(DbContextOptions<UserDbContext> opt) : base(opt) { }
        public DbSet<User> Users { get; set; }

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
