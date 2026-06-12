using SlotMath.Api.Infrastructure;

namespace SlotMath.Api.Features.Auth;

/// <summary>
/// Dev-only authentication endpoint (G29).
/// POST /api/auth/token returns a signed JWT for a given userId + email.
/// No real credential check — this is the development / E2E-test token path.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/token", (TokenRequest req, IConfiguration config) =>
        {
            if (string.IsNullOrWhiteSpace(req.UserId))
                return Results.BadRequest(new { error = "userId is required." });

            var email = req.Email ?? $"{req.UserId}@slot-math-lab.local";
            var token = JwtAuth.CreateDevToken(req.UserId, email, config);

            return Results.Ok(new TokenResponse(token, req.UserId));
        });

        return app;
    }

    public record TokenRequest(string UserId, string? Email);
    public record TokenResponse(string Token, string UserId);
}
