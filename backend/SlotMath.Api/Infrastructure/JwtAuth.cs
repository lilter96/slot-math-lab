using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SlotMath.Api.Infrastructure;

/// <summary>
/// JWT authentication helpers for G29.
///
/// Keys are read from environment variables at startup:
///   JWT__Secret  (default: "dev-secret-key-32-chars-minimum!!")
///   JWT__Issuer  (default: "slot-math-lab")
///
/// The standard library registers AddJwtBearer in Program.cs; this class
/// provides the dev-token factory and shared parameter helpers.
/// </summary>
public static class JwtAuth
{
    public const string DefaultSecret = "dev-secret-key-32-chars-minimum!!";
    public const string DefaultIssuer = "slot-math-lab";

    // ── Read config helpers ──────────────────────────────────────────
    public static string GetSecret(IConfiguration config)
        => config["JWT:Secret"] ?? DefaultSecret;

    public static string GetIssuer(IConfiguration config)
        => config["JWT:Issuer"] ?? DefaultIssuer;

    public static TokenValidationParameters BuildValidationParameters(IConfiguration config)
    {
        var secret = GetSecret(config);
        var issuer = GetIssuer(config);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };
    }

    // ── Dev-token factory (POST /api/auth/token) ────────────────────
    /// <summary>
    /// Creates a signed JWT for a given user id + email.
    /// Lifetime = 24 h.  Used only by the dev token endpoint and E2E tests.
    /// </summary>
    public static string CreateDevToken(string userId, string email, IConfiguration config)
    {
        var secret = GetSecret(config);
        var issuer = GetIssuer(config);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Email, email),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── Claim helpers ────────────────────────────────────────────────
    /// <summary>Returns the authenticated user id from the claim, or null.</summary>
    public static string? GetUserId(ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Returns true when the principal carries a valid user id.</summary>
    public static bool IsAuthenticated(ClaimsPrincipal principal)
        => principal.Identity?.IsAuthenticated == true
           && !string.IsNullOrWhiteSpace(GetUserId(principal));
}
