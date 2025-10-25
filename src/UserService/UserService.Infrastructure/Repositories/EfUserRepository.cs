using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserService.Application.Abstractions;
using UserService.Domain.Entities;
using UserService.Infrastructure.Data;

namespace UserService.Infrastructure.Repositories
{
    public sealed class EfUserRepository : IUserRepository
    {
        private readonly AppDbContext _db;
        public EfUserRepository(AppDbContext db) => _db = db;

        public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        {
            return _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email, ct);
        }

        public async Task AddAsync(User user, CancellationToken ct = default)
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }
    }
}
