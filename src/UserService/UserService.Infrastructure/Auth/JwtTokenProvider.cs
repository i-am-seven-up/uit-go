using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using UserService.Application.Abstractions;

namespace UserService.Infrastructure.Auth
{
    public class JwtTokenProvider : IJwtTokenProvider
    {
        private readonly string _secret;
        public JwtTokenProvider(IOptions<JwtSettings> options) => _secret = options.Value.Secret ?? throw new("Missing Jwt:Secret");

        public string CreateToken(string userId, string email)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[] { new Claim("sub", userId), new Claim("email", email) };
            var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddDays(1), signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
