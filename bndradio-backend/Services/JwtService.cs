// Generates and validates JWT tokens used for admin authentication.
// Tokens are signed with HMAC-SHA256 and expire after 24 hours.
// The secret is read from JWT_SECRET env var or Admin:JwtSecret config key.
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BndRadio.Services;

public class JwtService
{
    private readonly string _secret;
    private readonly TokenValidationParameters _validationParams;

    public JwtService(IConfiguration configuration)
    {
        _secret = Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? configuration["Admin:JwtSecret"]
            ?? "";

        if (string.IsNullOrWhiteSpace(_secret) || _secret.Length < 32)
            throw new InvalidOperationException("JWT_SECRET must be at least 32 characters.");

        _validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = GetKey(),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero,
        };
    }

    private SymmetricSecurityKey GetKey() =>
        new(Encoding.UTF8.GetBytes(_secret));

    // Creates a signed JWT with sub=admin, valid for 24 hours.
    public string GenerateToken()
    {
        var now = DateTimeOffset.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, "admin")]),
            IssuedAt = now.UtcDateTime,
            Expires = now.AddHours(24).UtcDateTime,
            SigningCredentials = new SigningCredentials(GetKey(), SecurityAlgorithms.HmacSha256),
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    // Returns true if the token signature and expiry are valid.
    public bool ValidateToken(string token)
    {
        try
        {
            new JwtSecurityTokenHandler().ValidateToken(token, _validationParams, out _);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
