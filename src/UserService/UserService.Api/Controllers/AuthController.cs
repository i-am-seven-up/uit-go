using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UserService.Application.Abstractions;
using UserService.Application.Dtos;

namespace UserService.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        public AuthController(IAuthService authService) => _authService = authService;

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto, CancellationToken ct)
        {
            var u = await _authService.RegisterAsync(dto.Email, dto.Password, dto.FullName, ct);
            return Ok(new { u.Id, u.Email, u.FullName });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto, CancellationToken ct)
        {
            var token = await _authService.LoginAsync(dto.Email, dto.Password, ct);
            return Ok(new { token });
        }
    }
}
