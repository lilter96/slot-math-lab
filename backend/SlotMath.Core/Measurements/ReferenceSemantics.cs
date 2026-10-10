namespace SlotMath.Core.Measurements;

/// <summary>Shared reference-statistic selection for collected and combined evidence.
/// Numeric nonzero probability differs from numeric mean; only guaranteed Boolean
/// sources/reductions may reuse a bounded mean interval for event probability.</summary>
internal static class ReferenceSemantics
{
    public static bool BooleanValue(MeasurementOptions options) =>
        options.Source == "event" && options.Subject is "observation" or "session"
        || options.Reduction is "any" or "all" && options.Subject is "round" or "episode";
    public static bool Probability(MeasurementOptions options) => options.ReferenceStatistic == "probability" || BooleanValue(options);
    public static bool Ratio(MeasurementOptions options) => options.ReferenceStatistic == "ratio" || options.PairRole == "wager";
}
