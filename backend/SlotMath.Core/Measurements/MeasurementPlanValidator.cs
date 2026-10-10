using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Measurements;

public static class MeasurementPlanValidator
{
    public static IEnumerable<string> Validate(MeasurementDefinition definition, IReadOnlySet<string> points, TypeCheckContext context)
    {
        var options = definition.Options;
        if (options is null) yield break;
        if (options.Subject is not ("observation" or "round" or "episode" or "transition")) yield return "Unknown observation subject.";
        if (options.Source is not ("value" or "event" or "count" or "rawPayout" or "capDeduction" or "turnover" or "net")) yield return "Unknown value source.";
        if (options.Assertion is not ("none" or "zero")) yield return "Choose no assertion or exact zero / false.";
        if (options.Assertion == "zero" && options.Subject != "observation") yield return "Exact assertions require individual observations; author a completed-subject residual at its boundary instead of reducing away failures.";
        if (options.PairRole is not ("value" or "wager") || options.PairRole == "wager" && (options.Subject == "episode" || options.Subject == "observation" && definition.NodeId is not null)) yield return "External wager pairing requires a complete paid-round subject.";
        if (options.ReferenceStatistic is not ("mean" or "ratio" or "probability")) yield return "Unknown reference statistic.";
        if (options.ReferenceStatistic == "ratio" && options.Pair is null && options.PairRole != "wager") yield return "Ratio verification requires a paired denominator.";
        if (options.Source is "rawPayout" or "capDeduction" or "turnover" or "net" && definition.NodeId is not null) yield return "Settlement sources require paid-round completion.";
        if (options.Reduction is not ("sum" or "count" or "any" or "all" or "first" or "last" or "min" or "max" or "average" or "delta")) yield return "Unknown within-subject reduction.";
        if (options.Subject is "episode" or "transition" && (options.EntryNodeId is null || options.ExitNodeId is null || options.EntryNodeId == options.ExitNodeId || !points.Contains(options.EntryNodeId) || !points.Contains(options.ExitNodeId))) yield return "Lifecycle observations require distinct, known entry and exit points.";
        if (options.Subject == "transition" && (definition.Value is null || options.Source != "value" || options.Weight is not null || options.Pair is not null || options.PairRole != "value")) yield return "Transitions require one numeric state expression, evaluated at entry and exit; pair and weight are derived from the lifecycle.";
        if (options.Subject == "episode" && definition.NodeId is null) yield return "Episode observations require a graph point.";
        if (options.Lags.Length > 16 || options.Lags.Any(l => l is < 1 or > 32) || options.Lags.Distinct().Count() != options.Lags.Length) yield return "Select up to 16 unique sequence lags between 1 and 32.";
        if (options.SupportLimit is < 1 or > 1024 || options.GroupLimit is < 1 or > 64 || (long)options.SupportLimit * (options.Group is null ? 1 : options.GroupLimit) > 8192) yield return "Support/group storage exceeds the bounded measurement budget (8192 support cells).";
        if (options.BinEdges.Length is < 1 or > 64 || options.BinEdges.Any(v => !double.IsFinite(v)) || !options.BinEdges.SequenceEqual(options.BinEdges.Distinct().Order())) yield return "Histogram edges must be finite, strictly increasing, with 1–64 edges.";
        if (options.Thresholds.Length > 32 || options.Thresholds.Any(v => !double.IsFinite(v)) || options.Thresholds.Distinct().Count() != options.Thresholds.Length) yield return "Select up to 32 unique finite tail thresholds.";
        if (options.Quantiles.Length > 16 || options.Quantiles.Any(q => !(q > 0 && q <= 1))) yield return "Quantiles must be in (0,1], with at most 16 selected.";
        if (!(options.Stake > 0) || !double.IsFinite(options.Stake)) yield return "External stake/cost must be positive and finite.";
        if (!(options.Confidence > 0.5 && options.Confidence < 0.999999) || options.ErrorFamilySize is < 1 or > 256) yield return "Confidence or family error allocation is invalid.";
        if (options.LowerBound is { } low && !double.IsFinite(low) || options.UpperBound is { } high && !double.IsFinite(high) || options.LowerBound > options.UpperBound) yield return "Value bounds must be finite and ordered.";
        if (options.Tolerance is { } tolerance && (!(tolerance > 0) || !double.IsFinite(tolerance)) || options.ReferenceMean is { } mean && !double.IsFinite(mean)) yield return "Reference and positive tolerance must be finite.";
        if (options.ReferenceDistribution.Length > 1024 || options.ReferenceDistribution.Any(p => p is null || !double.IsFinite(p.Value) || !double.IsFinite(p.Probability) || p.Probability < 0)
            || options.ReferenceDistribution.Select(p => p.Value).Distinct().Count() != options.ReferenceDistribution.Length
            || options.ReferenceDistribution.Length > 0 && System.Math.Abs(options.ReferenceDistribution.Sum(p => p.Probability) - 1) > 1e-10) yield return "Reference PMF must have unique finite outcomes and nonnegative probabilities summing to one (up to 1024 atoms).";
        foreach (var (expression, type) in new[] { (options.Pair, (ExprType?)ExprType.Number), (options.Weight, ExprType.Number), (options.Group, null), (options.AwardId, null), (options.EntryFilter, ExprType.Boolean), (options.ExitFilter, ExprType.Boolean), (options.ExitReason, ExprType.String) })
        {
            if (expression is null) continue;
            if (ExpressionCost.Compute(expression) > 1000) { yield return "Additional expression exceeds the 1000-operation budget."; continue; }
            foreach (var error in type is { } expected ? ExpressionTypeChecker.Check(expression, context, expected) : ExpressionTypeChecker.Check(expression, context)) yield return error.Message;
            if (type is null && ExpressionTypeChecker.InferType(expression, context) is not (ExprType.Number or ExprType.Boolean or ExprType.String or ExprType.Symbol)) yield return "Group and award keys must be scalar expressions.";
        }
        if (options.OrdinalLimit is < 0 or > 16 || (options.OrdinalLimit > 0 || options.ExitReason is not null) && options.Subject != "episode") yield return "Ordinal profiles and exit reasons require episodes; retain at most 16 ordinals.";
        if (options.Weight is not null && options.Subject != "observation") yield return "Likelihood weights bind to complete independent observations; within-subject averaging of weights is prohibited.";
        if (options.Source == "event" && definition.Value is null) yield return "Event observations require a Boolean predicate.";
    }
}
