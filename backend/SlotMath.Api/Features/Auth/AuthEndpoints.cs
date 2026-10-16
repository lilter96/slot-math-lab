using SlotMath.Api.Infrastructure;

namespace SlotMath.Api.Features.Auth;

/// <summary>
/// Private account sign-in. Production verifies the configured PBKDF2 credential;
/// local/test hosts also support development tokens.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/token", (TokenRequest req, IConfiguration config, IWebHostEnvironment environment, HttpContext http, AuthAccounts accounts) =>
        {
            var local = environment.IsDevelopment() || environment.IsEnvironment("CI") || environment.IsEnvironment("Testing");
            if (!local)
            {
                if (!accounts.Verify(req.UserId, req.Password ?? ""))
                    return Results.Unauthorized();
            }
            if (string.IsNullOrWhiteSpace(req.UserId))
                return Results.BadRequest(new { error = "userId is required." });

            var email = req.Email ?? $"{req.UserId}@slot-math-lab.local";
            var token = JwtAuth.CreateDevToken(req.UserId, email, config);

            http.Response.Cookies.Append("slotmath_session", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = !local,
                SameSite = SameSiteMode.Strict,
                MaxAge = TimeSpan.FromHours(24),
                Path = "/",
            });
            return Results.Ok(new TokenResponse(token, req.UserId));
        });

        group.MapGet("/status", (HttpContext http, IWebHostEnvironment environment) => Results.Ok(new
        {
            authenticated = JwtAuth.IsAuthenticated(http.User),
            userId = JwtAuth.IsAuthenticated(http.User) ? JwtAuth.GetUserId(http.User) : null,
            required = !(environment.IsDevelopment() || environment.IsEnvironment("CI") || environment.IsEnvironment("Testing")),
        }));
        group.MapPost("/logout", (HttpContext http) => { http.Response.Cookies.Delete("slotmath_session"); return Results.NoContent(); });
        return app;
    }

    public record TokenRequest(string UserId, string? Email, string? Password = null);
    public record TokenResponse(string Token, string UserId);
}
