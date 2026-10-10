namespace SlotMath.Core.Measurements;

/// <summary>Boundary exposure in unfinished paid rounds, separate from settled
/// numeric subjects and their denominators. Open instances are censored at the
/// interruption. A tracking-budget or unmatched-boundary fault withholds completeness.</summary>
public sealed record FeatureLifecycleCounts(long InterruptedRounds, long Entries, long Exits, long OpenInstances, bool Complete)
{
    internal static readonly FeatureLifecycleCounts Empty = new(0, 0, 0, 0, true);
    internal static FeatureLifecycleCounts? Merge(FeatureLifecycleCounts? a, FeatureLifecycleCounts? b)
        => a is null ? b : b is null ? a : new(a.InterruptedRounds + b.InterruptedRounds,
            a.Entries + b.Entries, a.Exits + b.Exits, a.OpenInstances + b.OpenInstances, a.Complete && b.Complete);
}
public sealed record InterruptedFeatureLifecycle(FeatureLifecycleCounts Cancelled, FeatureLifecycleCounts Failed) { public FeatureLifecycleCounts? ResourceExpiry { get; init; } }
internal enum PaidRoundInterruption { Cancelled, Failed, ResourceExpiry }
