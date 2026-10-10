namespace SlotMath.Api.Infrastructure;

/// <summary>
/// Hard limits enforced by the API (G29). Rejections carry clear user-facing messages.
/// </summary>
public static class ValidationLimits
{
    // ── Graph limits ────────────────────────────────────────────────
    public const int MaxNodes = 100;
    public const int MaxEdges = 200;
    public const int MaxSymbols = 50;
    public const int MaxReelStrips = 10;
    public const int MaxStripLength = 200;

    // ── Simulation limits ───────────────────────────────────────────
    public const int MaxSpinBudget = 10_000_000;
    /// <summary>Largest sampled run (<c>POST /api/runs</c>), in complete rounds.</summary>
    public const long MaxRunRounds = 10_000_000_000;
    /// <summary>Most sampling workers one run may use.</summary>
    public const int MaxRunWorkers = 8;
    public const int LightSampleMax = 50_000;
    public const int LightBranchMax = 100_000;

    // ── Expression limits ───────────────────────────────────────────
    public const int MaxExpressionLength = 1024;
    public const int MaxExpressionDepth = 20;

    // ── Plugin governance ───────────────────────────────────────────
    /// <summary>
    /// Only plugins with these IDs are approved to run. All others are rejected.
    /// </summary>
    public static readonly HashSet<string> ApprovedPluginIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "megaways-evaluator",
        "exotic-evaluator",
        "custom-cascade",
    };

    // ── Per-endpoint rate limits (requests per minute) ──────────────
    public const int RateLimitAnonymousValidate = 30;
    public const int RateLimitAnonymousEvaluate = 20;
    public const int RateLimitAnonymousRun = 5;
    public const int RateLimitAnonymousConfig = 10;

    /// <summary>
    /// Validates a graph config against size limits. Returns null if valid, or an error message.
    /// </summary>
    public static string? ValidateGraphSize(object config)
    {
        // Basic heuristic: check JSON size as proxy for complexity
        var json = System.Text.Json.JsonSerializer.Serialize(config);
        if (json.Length > 500_000)
            return $"Config JSON exceeds 500 KB limit ({json.Length} bytes). Reduce graph size.";

        return null;
    }

    /// <summary>
    /// Validates simulation budget against limits.
    /// </summary>
    public static string? ValidateSimBudget(int? sampleSize, int? maxBranches)
    {
        if (sampleSize > LightSampleMax)
            return $"Sample size {sampleSize:N0} exceeds light eval limit of {LightSampleMax:N0}. Use a full run instead.";
        if (maxBranches > LightBranchMax)
            return $"Branch budget {maxBranches:N0} exceeds limit of {LightBranchMax:N0}.";
        return null;
    }

    /// <summary>
    /// Validates an expression string against complexity limits.
    /// </summary>
    public static string? ValidateExpression(string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;
        if (expr.Length > MaxExpressionLength)
            return $"Expression exceeds maximum length of {MaxExpressionLength} characters ({expr.Length}).";
        return null;
    }

    /// <summary>
    /// Checks whether a plugin ID is approved for execution.
    /// </summary>
    public static bool IsPluginApproved(string pluginId) =>
        ApprovedPluginIds.Contains(pluginId);

    /// <summary>
    /// Returns the reason a plugin is not approved, or null if approved.
    /// </summary>
    public static string? PluginApprovalError(string pluginId) =>
        IsPluginApproved(pluginId)
            ? null
            : $"Plugin '{pluginId}' is not in the approved list. Only trusted plugins can execute.";
}
