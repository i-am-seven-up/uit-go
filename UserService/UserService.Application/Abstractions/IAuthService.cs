using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserService.Domain.Entities;

namespace UserService.Application.Abstractions
{
    public interface IAuthService
    {
        Task<User> RegisterAsync(string email, string password, string fullName, CancellationToken ct = default);
        Task<string> LoginAsync(string email, string password, CancellationToken ct = default);
    }
}
