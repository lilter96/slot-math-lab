using System.Security.Claims;

namespace SlotMath.Api.Infrastructure;

/// <summary>HTTP reads and mutations are scoped to a validated identity. Hosted workers
/// have no HTTP context and may process pinned records across owners. Hub invocations
/// must use their connection principal explicitly, rather than the HTTP accessor.</summary>
public sealed class RequestOwnership(IHttpContextAccessor accessor, IConfiguration configuration, IWebHostEnvironment environment)
{
    private bool Local => environment.IsDevelopment() || environment.IsEnvironment("CI") || environment.IsEnvironment("Testing");
    public string LegacyOwner => configuration["Auth:User"] is { Length: > 0 } user ? "user:" + user : Local ? "anonymous" : "legacy-unassigned";
    public string CurrentOwner => accessor.HttpContext is { } http ? Identity(http.User) : LegacyOwner;
    public bool CanAccess(string owner) => accessor.HttpContext is null || owner == CurrentOwner;
    public static string Identity(ClaimsPrincipal? principal) => principal is not null && JwtAuth.IsAuthenticated(principal)
        ? "user:" + JwtAuth.GetUserId(principal) : "anonymous";
}
