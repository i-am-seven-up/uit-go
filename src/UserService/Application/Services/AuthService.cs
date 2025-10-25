using UserService.Application.Abstractions;
using UserService.Domain.Entities;
namespace UserService.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _repo;
        private readonly IJwtTokenProvider _jwt;

        public AuthService(IUserRepository repo, IJwtTokenProvider jwt)
        {
            _repo = repo;
            _jwt = jwt;
        }

        public async Task<User> RegisterAsync(string email, string password, string fullName, CancellationToken ct = default)
        {
            if (await _repo.GetByEmailAsync(email, ct) is not null)
                throw new InvalidOperationException("Email already registered");

            var hash = BCrypt.Net.BCrypt.HashPassword(password);
            var user = new User { Email = email, PasswordHash = hash, FullName = fullName };
            await _repo.AddAsync(user, ct);
            return user;
        }

        public async Task<string> LoginAsync(string email, string password, CancellationToken ct = default)
        {
            var user = await _repo.GetByEmailAsync(email, ct) ?? throw new UnauthorizedAccessException("Invalid credentials");
            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid credentials");

            return _jwt.CreateToken(user.Id.ToString(), user.Email);
        }
    }
}
