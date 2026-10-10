using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math;

public sealed class SparseCohortResetTests
{
    private static MeasurementOptions Options => new() { Subject = "episode", Group = new ConstantExpr { Kind = ConstantKind.String, Value = "group" }, IndependentParents = true };
    private static void Round(MeasurementAnalysisAccumulator round, string? group, double? value = null, bool open = false)
    {
        round.Reset();
        if (group is not null)
        {
            round.Lifecycle(group, entries: 1, exits: open ? 0 : 1, unclosed: open ? 1 : 0);
            if (value is { } number) { round.Add(number, null, null, group); round.MatchingChild(group, true); }
        }
        round.Parent(value != null, group is null ? 0 : 1, group is null || open ? 0 : 1, open ? 1 : 0, 0, 0, value == null ? 0 : 1, value ?? 0);
    }

    [Fact]
    public void RediscoveredSparseCohortsKeepEveryAbsentParentExactlyOnce()
    {
        var staging = new MeasurementAnalysisAccumulator(Options); var total = staging.Empty();
        for (var round = 0; round < 100; round++)
        {
            Round(staging, round is 0 or 99 ? "rare" : null, round == 0 ? 2 : round == 99 ? 4 : null); total.Merge(staging);
        }
        var a = total.Snapshot(0, true); var rare = a.Groups["rare"];
        Assert.Equal(2, rare.Count); Assert.Equal(6, rare.Sum); Assert.Equal(3, rare.Mean);
        Assert.Equal(2, rare.Entries); Assert.Equal(2, rare.Exits); Assert.Equal(new ParentExposure(2, 2), rare.ParentExposure);
        Assert.Equal(100, rare.Normalization!.PaidRounds);
        // Simple manual cluster residuals: -1, +1 and 98 zeros. The cohort
        // observation mean is 3; only two of the 100 parent rounds contain it.
        Assert.NotNull(rare.ClusteredMeanInterval); Assert.Equal(3, (rare.ClusteredMeanInterval.Lower + rare.ClusteredMeanInterval.Upper) / 2, 12);
    }

    [Fact]
    public void DenseCohortsReuseContainersWithoutRetainingPriorRoundEvidence()
    {
        var staging = new MeasurementAnalysisAccumulator(Options); var total = staging.Empty();
        for (var round = 0; round < 100; round++) { Round(staging, "dense", round % 5); total.Merge(staging); }
        var a = total.Snapshot(0, true).Groups["dense"];
        Assert.Equal(100, a.Count); Assert.Equal(200, a.Sum); Assert.Equal(2, a.Mean!.Value, 12);
        Assert.Equal(100, a.Entries); Assert.Equal(100, a.Exits); Assert.Equal(new ParentExposure(100, 100), a.ParentExposure);
        Assert.Equal(100, a.Normalization!.PaidRounds);
    }

    [Fact]
    public void LifecycleOnlyCohortsAreActiveEvenWithoutAnyReducedValue()
    {
        var staging = new MeasurementAnalysisAccumulator(Options); var total = staging.Empty();
        Round(staging, "closed-empty"); total.Merge(staging);
        Round(staging, "open-empty", open: true); total.Merge(staging);
        Round(staging, null); total.Merge(staging);
        var groups = total.Snapshot(0, true).Groups;
        Assert.Equal(0, groups["closed-empty"].Count); Assert.Equal(1, groups["closed-empty"].Entries); Assert.Equal(1, groups["closed-empty"].Exits);
        Assert.Equal(0, groups["open-empty"].Count); Assert.Equal(1, groups["open-empty"].UnclosedEpisodes);
        foreach (var a in groups.Values) { Assert.Equal(3, a.Normalization!.PaidRounds); Assert.Equal(new ParentExposure(0, 0), a.ParentExposure); }
    }

    [Fact]
    public void DeltaResetClearsPaddingForInactiveHistoricalCohorts()
    {
        var staging = new MeasurementAnalysisAccumulator(Options); var delta = staging.Empty(); var published = staging.Empty();
        Round(staging, "rare", 2); delta.Merge(staging); published.Merge(delta); delta.Reset();
        for (var round = 0; round < 4; round++) { Round(staging, null); delta.Merge(staging); }
        published.Merge(delta); delta.Reset(); // Cached rare has only four zero parents here.
        Round(staging, "rare", 4); delta.Merge(staging); published.Merge(delta);
        var a = published.Snapshot(0, true).Groups["rare"];
        Assert.Equal(6, a.Normalization!.PaidRounds); Assert.Equal(2, a.Count); Assert.Equal(6, a.Sum);
        Assert.Equal(new ParentExposure(2, 2), a.ParentExposure);
    }
}
