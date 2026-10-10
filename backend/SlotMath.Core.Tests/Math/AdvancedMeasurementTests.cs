using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using Dict = System.Collections.Generic.Dictionary<string, object?>;
namespace SlotMath.Core.Tests.Math;

public class AdvancedMeasurementTests
{
    [Fact]
    public void StateDwellsAndAdjacentDuplicatesExcludeCensoredBoundaryRuns_AtEveryChunkSplit()
    {
        double[] values = [1, 1, 2, 2, 2, 1, 3, 3];
        var complete = new SequenceAccumulator([1]); foreach (var value in values) complete.Add(value);
        var expected = complete.Snapshot(true); Assert.Equal(4, expected.EqualAdjacentPairs); Assert.Equal(7, expected.AdjacentPairs);
        Assert.Equal(new[] { new StateDwell(1, 1, 1, 1, 1), new StateDwell(2, 1, 3, 3, 3) }, expected.StateDwell);
        for (var split = 1; split < values.Length; split++)
        {
            var a = new SequenceAccumulator([1]); var b = new SequenceAccumulator([1]); foreach (var value in values.Take(split)) a.Add(value); foreach (var value in values.Skip(split)) b.Add(value);
            a.Merge(b); var actual = a.Snapshot(true);
            Assert.Equal(expected.StateDwell, actual.StateDwell); Assert.Equal(expected.EqualAdjacentPairs, actual.EqualAdjacentPairs);
            Assert.Equal(expected.LongestDrought, actual.LongestDrought); Assert.Equal(expected.LongestEventStreak, actual.LongestEventStreak);
            Assert.Equal(expected.Autocorrelations[1]!.Value, actual.Autocorrelations[1]!.Value, 14);
        }
    }
    [Fact]
    public void NumericGroupAndAwardIdentifiersPreserveLargeIntegerIdentity()
    {
        var collector = new MeasurementCollector([Metric(new() { Group = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" }, AwardId = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" } })]);
        var binding = new MeasurementBinding<long>(_ => ExprValue.Number(1), null, Group: x => ExprValue.Number(x), AwardId: x => ExprValue.Number(x));
        collector.Begin(); collector.Observe(0, 9_007_199_254_740_992L, binding); collector.Observe(0, 9_007_199_254_740_993L, binding); collector.Commit();
        var analysis = Finish(collector).Analysis!; Assert.Equal(2, analysis.UniqueAwards); Assert.Equal(0, analysis.DuplicateAwards); Assert.Equal(2, analysis.Groups.Count);
    }
    [Theory]
    [InlineData(2, 5.991464547107979)]
    [InlineData(1, 3.841458820694124)]
    [InlineData(10, 18.307038053275146)]
    public void ChiSquareTailMatchesIndependentPublishedCriticalValues(int df, double critical)
        => Assert.Equal(0.05, StatisticalInference.ChiSquareSurvival(critical, df), 11);
    [Fact]
    public void PearsonCalibrationRequiresValidIndependentSubjectsAndAdequatePrespecifiedCounts()
    {
        MeasurementSnapshot Run(bool independent, int rounds, bool invalid)
        {
            var collector = new MeasurementCollector([Metric(new() { IndependentSubjects = independent, ReferenceDistribution = [new(0, 0.5), new(1, 0.5)] })]);
            for (var i = 0; i < rounds; i++) { collector.Begin(); if (invalid && i == 0) collector.Observe(0, i, _ => throw new InvalidOperationException("missing observation"), null); else collector.Observe(0, (double)(i % 2), Numeric); collector.Commit(); }
            return Finish(collector);
        }
        Assert.Equal(1, Run(true, 20, false).Analysis!.Comparison!.PValue);
        Assert.Null(Run(false, 20, false).Analysis!.Comparison!.PValue); Assert.Null(Run(true, 4, false).Analysis!.Comparison!.PValue);
        Assert.Null(Run(true, 20, true).Analysis!.Comparison!.PValue);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SettlementNamespaceSupportsMoneyPredicatesWithoutAddingOrMutatingGameState(bool native)
    {
        var eventPlan = new MeasurementDefinition
        {
            Id = "zero",
            Name = "Zero payout",
            Value = new CompareExpr
            {
                Op = CompareOp.Eq,
                Left = new FieldAccessExpr { Target = "measurement", Path = ["payout"] },
                Right = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" }
            },
            Options = new() { Source = "event", IndependentSubjects = true }
        };
        var compiled = new GraphCompiler(optimizeSampling: native).Compile(MeasurementTests.Model, [eventPlan]); Assert.True(compiled.IsValid);
        var result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new SampledConfig { MaxSpins = 10, Measurements = [eventPlan] });
        Assert.Equal(1, Assert.Single(result.Measurements).Mean); Assert.Equal(0, result.Stats.Mean);
        Assert.False(new GraphCompiler().Compile(MeasurementTests.Model, [eventPlan with { NodeId = "end" }]).IsValid);
        Assert.False(new GraphCompiler().Compile(MeasurementTests.Model, [eventPlan with { Options = eventPlan.Options! with { BinEdges = null! } }]).IsValid);
    }
    [Fact]
    public void ParallelReduction_PreservesFullStatisticsAndDoesNotConsumeGameRandomness()
    {
        var graph = JsonSerializer.Deserialize<GraphConfig>("""
        {"schemaVersion":"1.0.0","name":"Independent capped coin","nodes":[{"nodeType":"draw","id":"draw","drawWeights":[{"outcomeId":"zero","value":0,"weight":1},{"outcomeId":"two","value":2,"weight":1}],"outputs":{"out":{"name":"out","type":"Wins"}}},{"nodeType":"metricsSink","id":"sink","winCap":1,"inputs":{"in":{"name":"in","type":"Wins"}}}],"edges":[{"id":"e","sourceNodeId":"draw","sourcePort":"out","targetNodeId":"sink","targetPort":"in"}]}
        """, JsonOptions.Default)!;
        MeasurementDefinition[] plan = [new() { Id = "law", Name = "Full payout law", Options = new() { Lags = [1, 2, 4], IndependentSubjects = true, ReferenceDistribution = [new(0, 0.5), new(1, 0.5)] } }];
        var compiled = new GraphCompiler().Compile(graph, plan); Assert.True(compiled.IsValid);
        SampledResult<Dict> Run(int workers, MeasurementDefinition[] measurements) => SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new SampledConfig { Seed = 42, MaxSpins = 131073, DegreeOfParallelism = workers, Measurements = measurements });
        var a = Run(1, plan); var b = Run(4, plan); var plain = Run(4, []);
        Assert.Equal(JsonSerializer.Serialize(a.Measurements), JsonSerializer.Serialize(b.Measurements)); Assert.Equal(a.Stats.Mean, plain.Stats.Mean);
        Assert.Equal(131073, a.Measurements[0].Analysis!.Support.Sum(p => p.Count)); Assert.True(a.Measurements[0].Analysis!.Sequence!.Ordered);
    }
    [Fact]
    public void RepeatedWithinRoundObservations_UseClusterUncertaintyWithoutFalseIndependence()
    {
        var metric = Metric(new() { IndependentParents = true }); var collector = new MeasurementCollector([metric]);
        foreach (var values in new[] { new[] { 10d }, new double[9], Array.Empty<double>() })
        { collector.Begin(); foreach (var value in values) collector.Observe(0, value, Numeric); collector.Commit(); }
        var a = Finish(collector).Analysis!; Assert.Null(a.MeanInterval); Assert.NotNull(a.ClusteredMeanInterval);
        var radius = StatisticalInference.NormalCritical(0.05) * System.Math.Sqrt(27) / (10d / 3);
        Assert.Equal(1 - radius, a.ClusteredMeanInterval!.Lower, 12); Assert.Equal(1 + radius, a.ClusteredMeanInterval.Upper, 12);
    }
    [Fact]
    public void AllZeroSamplesCannotAcquireAPayoutAcceptanceVerdict()
    {
        var metric = Metric(new() { IndependentSubjects = true, ReferenceMean = 0, Tolerance = 0.1 }); var collector = new MeasurementCollector([metric]);
        for (var i = 0; i < 100; i++) { collector.Begin(); collector.Observe(0, 0d, Numeric); collector.Commit(); }
        var a = Finish(collector).Analysis!; Assert.Null(a.MeanInterval); Assert.Equal("insufficient", Assert.Single(a.Checks).Status); Assert.True(a.ProbabilityInterval!.Upper > 0);
    }
    [Fact]
    public void MixedStakeReturn_UsesRatioOfSums_AndChargesCostOncePerParent()
    {
        var metric = Metric(new() { Subject = "round", PairRole = "wager", ReferenceStatistic = "ratio" });
        var collector = new MeasurementCollector([metric]); var binding = new MeasurementBinding<(long Win, long Stake)>(x => ExprValue.Number(x.Win), null, Pair: x => ExprValue.Number(x.Stake));
        foreach (var round in new[] { (Win: 2L, Stake: 1L), (Win: 0L, Stake: 3L) })
        { collector.Begin(); collector.Point(0, "reveal", round, binding); collector.Point(0, "reveal", round, binding); collector.CompleteRound(0, round, binding, 0, 0); collector.Commit(); }
        var a = Finish(collector).Analysis!; Assert.Equal(4, a.Sum); Assert.Equal(4, a.Pair!.SumY); Assert.Equal(1, a.Pair.Ratio);
        // Individual round ratios {4/1,0/3} average to 2, while true return is 4/(1+3)=1.
        Assert.NotEqual(2, a.Pair.Ratio);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompilerAndInterpreter_PublishAuthoredEpisodesAndRawSettlement(bool optimized)
    {
        var defs = new MeasurementDefinition[] {
            new() { Id = "feature", Name = "Whole feature", NodeId = "end", Value = new FieldAccessExpr { Target = "state", Path = ["spinWin"] }, Options = new() { Subject = "episode", EntryNodeId = "fs", ExitNodeId = "sink", IndependentSubjects = true } },
            new() { Id = "raw", Name = "Raw payout", Options = new() { Source = "rawPayout" } },
            new() { Id = "loss", Name = "Cap deduction", Options = new() { Source = "capDeduction" } } };
        var compiled = new GraphCompiler(optimizeSampling: optimized).Compile(MeasurementTests.Model, defs);
        Assert.True(compiled.IsValid, string.Join(';', compiled.Errors.Select(e => e.Message)));
        var result = SampledInterpreter.Evaluate(compiled.Program!, new Dict(), new SampledConfig { MaxSpins = 5, Measurements = defs, WinScale = (double)compiled.WinScale });
        Assert.Equal(9, result.Measurements[0].Mean); Assert.Equal(5, result.Measurements[0].Analysis!.Entries); Assert.Equal(0, result.Measurements[0].Errors);
        Assert.Equal(0, result.Measurements[1].Mean); Assert.Equal(0, result.Measurements[2].Mean);
    }
    private static MeasurementDefinition Metric(MeasurementOptions options, string? node = "reveal") => new() { Id = "metric", Name = "Reference population", NodeId = node, Options = options };
    private static MeasurementBinding<double> Numeric => new(x => ExprValue.Number((long)x), null);
    private static MeasurementSnapshot Finish(MeasurementCollector collector) => Assert.Single(MeasurementCollector.Snapshot(collector.Definitions, collector.Total));
    [Fact]
    public void StateTransitionsCaptureBothBoundaries_AndRowsUseTheirOwnExposure()
    {
        var collector = new MeasurementCollector([Metric(new() { Subject = "transition", EntryNodeId = "before", ExitNodeId = "after" })]);
        foreach (var (from, to) in new[] { (0d, 1d), (0d, 1d), (0d, 0d), (1d, 0d) })
        { collector.Begin(); collector.Point(0, "before", from, Numeric); collector.Point(0, "after", to, Numeric); collector.Commit(); }
        var a = Finish(collector).Analysis!; Assert.Equal(4, a.Count); Assert.Equal(4, a.Entries); Assert.Equal(4, a.Exits);
        var zeroToOne = a.Transitions.Single(t => t.From == 0 && t.To == 1); Assert.Equal(2, zeroToOne.Count); Assert.Equal(3, zeroToOne.FromExposure); Assert.Equal(2d / 3, zeroToOne.Probability);
        var oneToZero = a.Transitions.Single(t => t.From == 1); Assert.Equal(1, oneToZero.Probability);
    }
    [Fact]
    public void EntryCohortIsPinned_AndExitScopeDoesNotHideInvalidIncludedChildren()
    {
        var collector = new MeasurementCollector([Metric(new() { Subject = "episode", EntryNodeId = "enter", ExitNodeId = "exit" })]);
        var binding = new MeasurementBinding<double>(x => x < 0 ? throw new InvalidOperationException("missing child") : ExprValue.Number((long)x), null,
            EntryFilter: x => ExprValue.Bool(x == 1), ExitFilter: x => ExprValue.Bool(x >= 20));
        collector.Begin(); collector.Point(0, "enter", 0d, binding); collector.Point(0, "reveal", -1d, binding); collector.Point(0, "exit", 20d, binding); collector.Commit();
        collector.Begin(); collector.Point(0, "enter", 1d, binding); collector.Point(0, "reveal", 10d, binding); collector.Point(0, "exit", 30d, binding); collector.Commit();
        collector.Begin(); collector.Point(0, "enter", 1d, binding); collector.Point(0, "reveal", -1d, binding); collector.Point(0, "exit", 0d, binding); collector.Commit();
        var result = Finish(collector); Assert.Equal(3, result.Observations); Assert.Equal(1, result.Count); Assert.Equal(1, result.Excluded); Assert.Equal(1, result.Errors); Assert.Equal(10, result.Mean);
    }
    [Fact]
    public void EpisodeTotal_RevealMean_AndRoundContributionHaveDifferentPopulations()
    {
        var defs = new[] { Metric(new() { Subject = "episode", EntryNodeId = "enter", ExitNodeId = "exit" }), Metric(new(), "reveal"), Metric(new() { Subject = "round" }, "reveal") };
        var collector = new MeasurementCollector(defs);
        foreach (var values in new[] { new[] { 10d }, new double[9], Array.Empty<double>() })
        {
            collector.Begin();
            if (values.Length > 0)
            {
                collector.Point(0, "enter", 0d, Numeric);
                foreach (var value in values) for (var i = 0; i < defs.Length; i++) collector.Point(i, "reveal", value, Numeric);
                collector.Point(0, "exit", 0d, Numeric);
            }
            collector.Commit();
        }
        var result = MeasurementCollector.Snapshot(defs, collector.Total);
        Assert.Equal(5, result[0].Mean); Assert.Equal(2, result[0].Count);
        Assert.Equal(1, result[1].Mean); Assert.Equal(10, result[1].Count);
        Assert.Equal(10d / 3, result[2].Mean!.Value, 12); Assert.Equal(3, result[2].Count);
        Assert.Equal(2, result[0].Analysis!.Entries); Assert.Equal(2, result[0].Analysis!.Exits);
        Assert.Equal(2, result[2].Analysis!.DistinctParents); // An absent-feature zero is a denominator, not a matching child.
    }
    [Fact]
    public void MomentsBinsTailsQuantilesAndPairs_MatchDirectSums()
    {
        var metric = Metric(new() { Pair = new ConstantExpr { Kind = ConstantKind.Integer, Value = "0" }, BinEdges = [0, 2, 4], Thresholds = [2, 4], Quantiles = [0.5, 0.75, 1], IndependentSubjects = true, ReferenceMean = 2, Tolerance = 0.01 });
        var collector = new MeasurementCollector([metric]); double[] xs = [0, 2, 2, 4], ys = [0, 0, 4, 4];
        for (var i = 0; i < xs.Length; i++) { collector.Begin(); collector.Observe(0, i, new MeasurementBinding<int>(j => ExprValue.Number((long)xs[j]), null, Pair: j => ExprValue.Number((long)ys[j]))); collector.Commit(); }
        var value = Finish(collector); var a = value.Analysis!;
        Assert.Equal(2, value.Mean); Assert.Equal(6, a.Moments.SecondMoment); Assert.Equal(2, a.Moments.PopulationVariance);
        Assert.Equal(8d / 3, a.Moments.SampleVariance!.Value, 12); Assert.Equal(0, a.Moments.Skewness!.Value, 12); Assert.Equal(-1, a.Moments.ExcessKurtosis!.Value, 12);
        Assert.Equal(1, a.Moments.MeanAbsoluteDeviation); Assert.Equal(new long[] { 0, 1, 2, 1 }, a.Bins.Select(b => b.Count));
        Assert.Equal(new double[] { 0, 0, 4, 4 }, a.Bins.Select(b => b.Sum)); Assert.Equal(new double?[] { 2, 2, 4 }, a.Quantiles.Select(q => q.Value));
        Assert.Equal(0.75, a.Tails[0].Probability); Assert.Equal(8d / 3, a.Tails[0].Mean!.Value, 12); Assert.Equal(6, a.Tails[0].SecondMoment);
        Assert.Equal(3, a.UpperTails[0].Mean); Assert.Equal(0.75, a.UpperTails[0].LowerReturnShare);
        Assert.Equal(8d / 3, a.Pair!.Covariance!.Value, 12); Assert.Equal(1 / System.Math.Sqrt(2), a.Pair.Correlation!.Value, 12); Assert.Equal(1, a.Pair.Ratio);
        Assert.Equal("insufficient", Assert.Single(a.Checks).Status);
    }
    [Fact]
    public void GroupedValuesAndWeightedEstimates_AreNotNaivelyAveraged()
    {
        var opts = new MeasurementOptions { Group = new ConstantExpr { Kind = ConstantKind.String, Value = "group" }, Weight = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" } };
        var collector = new MeasurementCollector([Metric(opts)]); double[] xs = [0, 2, 2, 4], weights = [1, 1, 2, 2];
        for (var i = 0; i < 4; i++)
        {
            collector.Begin(); collector.Observe(0, i, new MeasurementBinding<int>(j => ExprValue.Number((long)xs[j]), null,
            Group: j => ExprValue.String(j < 2 ? "base" : "sticky"), Weight: j => ExprValue.Number((long)weights[j]))); collector.Commit();
        }
        var a = Finish(collector).Analysis!;
        Assert.Equal(1, a.Groups["base"].Moments.SecondMoment / 2); Assert.Equal(2, a.Groups["sticky"].DistinctParents);
        Assert.Equal(3.5, a.Weights!.OrdinaryEstimate); Assert.Equal(7d / 3, a.Weights.SelfNormalizedEstimate!.Value, 12); Assert.Equal(3.6, a.Weights.EffectiveSampleSize!.Value, 12);
    }
    [Fact]
    public void SequenceBoundaryMerge_ReconstructsGapsStreaksAndAutocorrelation()
    {
        var opts = new MeasurementOptions { Lags = [1, 2, 4] }; var metric = Metric(opts); double[] xs = [0, 1, 1, 0, 0, 1, 0];
        var whole = new MeasurementCollector([metric]); var first = new MeasurementCollector([metric]); var second = new MeasurementCollector([metric]);
        for (var i = 0; i < xs.Length; i++) foreach (var collector in new[] { whole, i < 3 ? first : second }) { collector.Begin(); collector.Observe(0, xs[i], Numeric); collector.Commit(); }
        MeasurementCollector.Merge(first.Total, second.Total); var a = Finish(first).Analysis!.Sequence!; var b = Finish(whole).Analysis!.Sequence!;
        Assert.Equal(2, a.LongestDrought); Assert.Equal(2, a.LongestEventStreak); Assert.Equal(2, a.CompletedGaps); Assert.Equal(1, a.MeanGap);
        foreach (var lag in opts.Lags)
        {
            var mean = xs.Average(); var expected = Enumerable.Range(lag, xs.Length - lag).Sum(i => (xs[i] - mean) * (xs[i - lag] - mean)) / xs.Sum(x => (x - mean) * (x - mean));
            Assert.Equal(expected, a.Autocorrelations[lag]!.Value, 12); Assert.Equal(b.Autocorrelations[lag]!.Value, a.Autocorrelations[lag]!.Value, 12);
        }
        Assert.False(Assert.Single(MeasurementCollector.Snapshot([metric], first.Total, false)).Analysis!.Sequence!.Ordered);
    }
    [Fact]
    public void InvalidChild_InvalidatesTheWholeSubject_AndOpenEpisodesAreVisible()
    {
        var metric = Metric(new() { Subject = "episode", EntryNodeId = "enter", ExitNodeId = "exit" }); var collector = new MeasurementCollector([metric]);
        collector.Begin(); collector.Point(0, "enter", 0d, Numeric);
        var invalid = new MeasurementBinding<double>(_ => throw new InvalidOperationException("missing award"), null);
        collector.Point(0, "reveal", 1d, invalid); collector.Point(0, "reveal", 1d, invalid); collector.Point(0, "exit", 0d, Numeric); collector.Commit();
        var result = Finish(collector); Assert.Equal(1, result.Observations); Assert.Equal(1, result.Errors); Assert.Equal(0, result.Count); Assert.Null(result.Mean);
        collector.Begin(); collector.Point(0, "enter", 0d, Numeric); collector.Point(0, "reveal", 3d, Numeric); collector.Commit();
        result = Finish(collector); Assert.Equal(2, result.Errors); Assert.Equal(1, result.Analysis!.UnclosedEpisodes); Assert.Equal("invalid", result.Analysis.Checks[0].Status);
    }
    [Fact]
    public void SupportOverflow_PreservesBinsAndReturnsQuantileBounds()
    {
        var collector = new MeasurementCollector([Metric(new() { SupportLimit = 2, BinEdges = [0, 5], Quantiles = [0.5] })]);
        foreach (double value in new[] { 1, 2, 3, 4 }) { collector.Begin(); collector.Observe(0, value, Numeric); collector.Commit(); }
        var a = Finish(collector).Analysis!; Assert.False(a.SupportComplete); Assert.Empty(a.Support); Assert.Equal(4, a.Bins.Sum(b => b.Count));
        var q = Assert.Single(a.Quantiles); Assert.Null(q.Value); Assert.Equal(0, q.Lower); Assert.Equal(5, q.Upper);
        Assert.Equal(2.5, a.UpperTails[0].LowerMean); Assert.Equal(4, a.UpperTails[0].UpperMean);
        Assert.Equal(0, a.MeanAbsoluteDeviationBounds!.Lower); Assert.Equal(1.5, a.MeanAbsoluteDeviationBounds.Upper);
    }
    [Fact]
    public void SameMeanWrongDistribution_IsDetectedByIndependentPmf()
    {
        var collector = new MeasurementCollector([Metric(new() { ReferenceDistribution = [new(0, 0.5), new(2, 0.5)] })]);
        foreach (var value in new[] { 1d, 1d }) { collector.Begin(); collector.Observe(0, value, Numeric); collector.Commit(); }
        var a = Finish(collector).Analysis!; Assert.Equal(1, a.Comparison!.TotalVariation); Assert.Equal(0.5, a.Comparison.CdfDistance); Assert.Equal(2, a.Comparison.UnexpectedObservations); Assert.Null(a.Comparison.ChiSquare);
    }
    [Theory]
    [InlineData(0, 10, 0, 0.3084971078187608)]
    [InlineData(10, 10, 0.6915028921812392, 1)]
    [InlineData(5, 10, 0.1870860284473985, 0.8129139715526015)]
    public void ExactBinomial_MatchesPublishedBetaQuantileReferences(long successes, long n, double lower, double upper)
    { var interval = StatisticalInference.ExactBinomial(successes, n); Assert.Equal(lower, interval.Lower, 10); Assert.Equal(upper, interval.Upper, 10); }
    [Fact]
    public void ZeroEventsAndSequentialBounds_DoNotClaimCertainZero()
    {
        Assert.Equal(0.0000299568740194, StatisticalInference.ZeroEventUpperBound(100000), 14);
        var interval = StatisticalInference.SequentialMean(0.98, 100000, 0, 1000, 0.05); Assert.True(interval.Lower < 0.98); Assert.True(interval.Upper > 0.98); Assert.Contains("Time-uniform", interval.Method);
    }
    [Fact]
    public void ProgressDeltaMerge_DoesNotAliasOrDoubleCountMutableDistributions()
    {
        var collector = new MeasurementCollector([Metric(new())]); var progress = new MeasurementAccumulator[1];
        for (var i = 0; i < 3; i++) { collector.Begin(); collector.Observe(0, 2d, Numeric); collector.Commit(); MeasurementCollector.Merge(progress, collector.Delta); Array.Clear(collector.Delta); }
        var a = Finish(collector).Analysis!; var b = Assert.Single(MeasurementCollector.Snapshot(collector.Definitions, progress)).Analysis!;
        Assert.Equal(3, Assert.Single(a.Support).Count); Assert.Equal(3, Assert.Single(b.Support).Count);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
    }
}
