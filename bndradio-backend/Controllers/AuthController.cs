// POST /auth/login — validates admin credentials and returns a signed JWT.
// The token is used as a Bearer header for all admin-only endpoints.
using Microsoft.AspNetCore.Mvc;
using BndRadio.Services;

namespace BndRadio.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly JwtService _jwtService;
    private readonly string _adminLogin;
    private readonly string _adminPassword;

    public AuthController(JwtService jwtService, IConfiguration configuration)
    {
        _jwtService = jwtService;
        _adminLogin = Environment.GetEnvironmentVariable("ADMIN_LOGIN")
            ?? configuration["Admin:Login"];
        if (string.IsNullOrWhiteSpace(_adminLogin))
            throw new InvalidOperationException("ADMIN_LOGIN is not configured.");

        _adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? configuration["Admin:Password"];
        if (string.IsNullOrWhiteSpace(_adminPassword))
            throw new InvalidOperationException("ADMIN_PASSWORD is not configured.");
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (request.Login != _adminLogin || request.Password != _adminPassword)
            return Unauthorized(new { error = "Invalid credentials" });
        var token = _jwtService.GenerateToken();
        return Ok(new { token });
    }
}

public record LoginRequest(string Login, string Password);
