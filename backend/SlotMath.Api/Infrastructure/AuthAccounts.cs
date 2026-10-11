namespace SlotMath.Api.Infrastructure;

/// <summary>Configured private accounts; legacy single-operator settings remain supported.
/// Provisioning and password reset are deployment concerns, not public registration.</summary>
public sealed class AuthAccounts
{
    private readonly Dictionary<string, string> _credentials = new(StringComparer.Ordinal);
    public AuthAccounts(IConfiguration configuration)
    {
        foreach (var entry in configuration.GetSection("Auth:Users").GetChildren())
        {
            var id = entry["UserId"]; var hash = entry["PasswordHash"];
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(hash) || !_credentials.TryAdd(id, hash))
                throw new InvalidOperationException("Each Auth:Users entry requires a unique UserId and PasswordHash.");
        }
        var legacyUser = configuration["Auth:User"]; var legacyHash = configuration["Auth:PasswordHash"];
        if (!string.IsNullOrWhiteSpace(legacyUser) && !string.IsNullOrWhiteSpace(legacyHash))
        {
            if (_credentials.TryGetValue(legacyUser, out var existing) && existing != legacyHash)
                throw new InvalidOperationException("Legacy Auth:User conflicts with Auth:Users.");
            _credentials[legacyUser] = legacyHash;
        }
    }
    public bool HasAccounts => _credentials.Count > 0;
    public bool Verify(string userId, string password) => !string.IsNullOrWhiteSpace(userId) && _credentials.TryGetValue(userId, out var hash) && PasswordHash.Verify(password, hash);
}
