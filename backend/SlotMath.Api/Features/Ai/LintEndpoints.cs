using System.Text.Json;
using SlotMath.Api.Features.Configs;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Ai;

// ══════════════════════════════════════════════════════════════════════════════
//  G28 — Compliance lint rules
//
//  POST /api/ai/lint
//  Pure rule-based checks — no AI API key required.
// ══════════════════════════════════════════════════════════════════════════════

public static class LintEndpoints
{
    public static void MapLint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai");

        group.MapPost("/lint", (LintRequest request) =>
        {
            // Deserialise
            GraphConfig config;
            try
            {
                config = ConfigsEndpoints.DeserializeConfig(request.Config);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = $"Invalid config JSON: {ex.Message}" });
            }

            var issues = new List<LintIssue>();

            // ── Rule 1: rtp-out-of-band ─────────────────────────────────────
            if (request.Rtp.HasValue)
            {
                var rtp = request.Rtp.Value;
                if (rtp < 0.80 || rtp > 1.0)
                {
                    var sinkId = config.Nodes.OfType<MetricsSinkNode>().FirstOrDefault()?.Id;
                    issues.Add(new LintIssue
                    {
                        RuleId = "rtp-out-of-band",
                        Severity = "error",
                        Message = $"RTP {rtp:F4} is outside the expected range [0.80, 1.00]. " +
                                  "Review symbol weights or paytable entries.",
                        NodeId = sinkId,
                    });
                }
            }

            // ── Rule 2: missing-sink ────────────────────────────────────────
            var sinkNodes = config.Nodes.OfType<MetricsSinkNode>().ToList();
            if (sinkNodes.Count == 0)
            {
                issues.Add(new LintIssue
                {
                    RuleId = "missing-sink",
                    Severity = "error",
                    Message = "Graph has no MetricsSink node. " +
                              "Add a Sink node so the engine knows where to collect wins.",
                });
            }

            // ── Rule 3: dead-symbol ─────────────────────────────────────────
            // A symbol is "dead" if it appears in any paytable but never in any draw weight.
            var symbolsInDrawWeights = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in config.Nodes.OfType<DrawNode>())
            {
                if (node.DrawWeights is null) continue;
                foreach (var dw in node.DrawWeights)
                    symbolsInDrawWeights.Add(dw.OutcomeId);
            }
            // Also collect symbols referenced in reel strips
            foreach (var strip in config.ReelStrips)
                foreach (var sym in strip.Symbols)
                    symbolsInDrawWeights.Add(sym);

            var symbolsInPaytable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pt in config.Paytables)
                foreach (var entry in pt.Entries)
                    symbolsInPaytable.Add(entry.SymbolId);

            foreach (var sym in config.Symbols)
            {
                if (symbolsInPaytable.Contains(sym.Id) && !symbolsInDrawWeights.Contains(sym.Id))
                {
                    issues.Add(new LintIssue
                    {
                        RuleId = "dead-symbol",
                        Severity = "warning",
                        Message = $"Symbol '{sym.Id}' ({sym.Name}) appears in the paytable " +
                                  "but is never referenced in any DrawNode weight or ReelStrip. " +
                                  "It will never award a win.",
                    });
                }
            }

            // ── Rule 4: paytable-anomaly ────────────────────────────────────
            // Any paytable entry where all payouts parse to zero.
            foreach (var pt in config.Paytables)
            {
                foreach (var entry in pt.Entries)
                {
                    if (entry.Payouts.Length == 0) continue;

                    var allZero = entry.Payouts.All(p =>
                    {
                        // Payout is a rational string e.g. "10", "5/2", "0"
                        if (p == "0") return true;
                        if (double.TryParse(p, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var d))
                            return d == 0;
                        // Rational "n/d"
                        var slash = p.IndexOf('/');
                        if (slash > 0)
                        {
                            var num = p[..slash].Trim();
                            return num == "0" || (double.TryParse(num,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var n) && n == 0);
                        }
                        return false;
                    });

                    if (allZero)
                    {
                        issues.Add(new LintIssue
                        {
                            RuleId = "paytable-anomaly",
                            Severity = "warning",
                            Message = $"Paytable '{pt.Id}': symbol '{entry.SymbolId}' has " +
                                      "payout = 0 for all counts. It will never award a win.",
                        });
                    }
                }
            }

            // ── Rule 5: max-win-too-low ─────────────────────────────────────
            // If the graph contains multiplier nodes but no draw weight value exceeds 100,
            // emit a warning. This is a heuristic for games that probably intend a
            // high max win but have their multiplier wired incorrectly.
            var hasMultiplierNode = config.Nodes.Any(n =>
                n is MapNode or ModifyStateNode);   // proxy: any transform node
            if (hasMultiplierNode)
            {
                var maxDrawValue = config.Nodes
                    .OfType<DrawNode>()
                    .SelectMany(n => n.DrawWeights ?? Array.Empty<DrawWeight>())
                    .Select(w => w.Value)
                    .DefaultIfEmpty(0)
                    .Max();

                if (maxDrawValue <= 100 && maxDrawValue > 0)
                {
                    issues.Add(new LintIssue
                    {
                        RuleId = "max-win-too-low",
                        Severity = "warning",
                        Message = $"The highest draw value is {maxDrawValue}. " +
                                  "The game has transform/multiplier nodes — consider whether " +
                                  "the max-win ceiling is intentionally low.",
                    });
                }
            }

            return Results.Ok(new LintResponse { Issues = issues });
        });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record LintRequest
{
    public required object Config { get; init; }
    /// <summary>RTP from a prior /evaluate/light call (optional; required for rtp-out-of-band rule).</summary>
    public double? Rtp { get; init; }
}

public sealed record LintIssue
{
    public required string RuleId { get; init; }
    /// <summary>"error" | "warning" | "info"</summary>
    public required string Severity { get; init; }
    public required string Message { get; init; }
    /// <summary>Optional: ID of the offending node.</summary>
    public string? NodeId { get; init; }
}

public sealed record LintResponse
{
    public required List<LintIssue> Issues { get; init; }
}
