using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace E2E.PerformanceTests.Infrastructure;

public static class JwtTokenHelper
{
    // Must match the settings in appsettings.json (from TripService/DriverService)
    private const string SecretKey = "UltimateSuperSecretKeyForJwtTokenGeneration123";
    private const string Issuer = "UserService";
    private const string Audience = "UserServiceClients";

    public static string GenerateToken(Guid userId, string role, int expiryMinutes = 60)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GeneratePassengerToken(Guid? passengerId = null)
    {
        return GenerateToken(passengerId ?? Guid.NewGuid(), "passenger");
    }

    public static string GenerateDriverToken(Guid? driverId = null)
    {
        return GenerateToken(driverId ?? Guid.NewGuid(), "driver");
    }
}
