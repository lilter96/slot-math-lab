using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math;

public sealed class InterruptedLifecycleTests
{
    private static MeasurementDefinition Plan(string subject = "episode", int groups = 32) => new()
    {
        Id = "feature", Name = "Interrupted feature", NodeId = "reveal",
        Value = new ConstantExpr { Kind = ConstantKind.Integer, Value = "7" },
        Options = new() { Subject = subject, EntryNodeId = "enter", ExitNodeId = "exit", Reduction = "sum", GroupLimit = groups,
            Group = new ConstantExpr { Kind = ConstantKind.String, Value = "binding" } },
    };
    private static readonly MeasurementBinding<string> Binding = new(_ => ExprValue.Number(7), null, Group: ExprValue.String);
    private static MeasurementSnapshot Snapshot(MeasurementCollector collector) => collector.Total[0].Snapshot("feature");
    private static void At(MeasurementCollector collector, string point, string group) => collector.Point(0, point, group, Binding);

    [Theory][InlineData("episode")][InlineData("transition")]
    public void CancellationKeepsNestedEntryCohortsAndClosedBoundariesWithoutPartialValues(string subject)
    {
        // Manual boundary sequence: outer enter, inner enter/reveal/exit, outer reveal, cancel.
        // Two entries, one exit, one open; even the closed inner value belongs to an unfinished round.
        var collector = new MeasurementCollector([Plan(subject)]); collector.Begin();
        At(collector, "enter", "outer"); At(collector, "enter", "inner"); At(collector, "reveal", "inner");
        At(collector, "exit", "changed"); At(collector, "reveal", "outer");
        collector.Interrupt(PaidRoundInterruption.Cancelled); collector.Interrupt(PaidRoundInterruption.Failed);
        var metric = Snapshot(collector); var analysis = metric.Analysis!;
        Assert.Equal(new FeatureLifecycleCounts(1, 2, 1, 1, true), analysis.InterruptedLifecycle!.Cancelled);
        Assert.Equal(FeatureLifecycleCounts.Empty, analysis.InterruptedLifecycle.Failed);
        Assert.Equal(new FeatureLifecycleCounts(1, 1, 0, 1, true), analysis.Groups["outer"].InterruptedLifecycle!.Cancelled);
        Assert.Equal(new FeatureLifecycleCounts(1, 1, 1, 0, true), analysis.Groups["inner"].InterruptedLifecycle!.Cancelled);
        Assert.False(analysis.Groups.ContainsKey("changed"));
        Assert.Equal(0, metric.Observations); Assert.Equal(0, metric.Count); Assert.Equal(0, metric.Errors); Assert.Null(metric.Mean);
        Assert.Equal(0, analysis.Entries); Assert.Equal(0, analysis.Exits); Assert.Equal(0, analysis.UnclosedEpisodes);
        Assert.Null(analysis.Normalization); Assert.Empty(analysis.Support); Assert.Empty(metric.Witnesses);
        Assert.Throws<InvalidOperationException>(collector.Commit);
    }

    [Fact]
    public void PreparedRoundCanBeDiscardedWithoutDoubleCountingOpenInstances()
    {
        var collector = new MeasurementCollector([Plan()]); collector.Begin(); At(collector, "enter", "sticky");
        At(collector, "reveal", "sticky"); collector.Prepare(); collector.Interrupt(PaidRoundInterruption.Failed);
        var a = Snapshot(collector).Analysis!;
        Assert.Equal(new FeatureLifecycleCounts(1, 1, 0, 1, true), a.InterruptedLifecycle!.Failed);
        Assert.Equal(1, a.Groups["sticky"].InterruptedLifecycle!.Failed.OpenInstances);
        Assert.Equal(0, a.UnclosedEpisodes); Assert.Equal(0, a.Count);
    }

    [Fact]
    public void ResetAndWorkerMergePreserveAuditCountsAndOnlySettledParentNormalization()
    {
        var collector = new MeasurementCollector([Plan()]); collector.Begin(); At(collector, "enter", "cancelled");
        collector.Interrupt(PaidRoundInterruption.Cancelled); var earlier = Snapshot(collector);
        Array.Clear(collector.Delta);
        collector.Begin(); At(collector, "enter", "settled"); At(collector, "reveal", "settled"); At(collector, "exit", "changed"); collector.Commit();
        var other = new MeasurementCollector([Plan()]); other.Begin(); At(other, "enter", "failed"); At(other, "exit", "changed"); other.Interrupt(PaidRoundInterruption.Failed);
        MeasurementCollector.Merge(collector.Total, other.Total);
        other.Begin(); other.Commit(); // reset the source without corrupting the merged audit
        var a = Snapshot(collector).Analysis!;
        Assert.Equal(1, a.Count); Assert.Equal(7, a.Mean); Assert.Equal(1, a.Entries); Assert.Equal(1, a.Exits);
        Assert.Equal(new FeatureLifecycleCounts(1, 1, 1, 0, true), a.InterruptedLifecycle!.Failed);
        Assert.Equal(new FeatureLifecycleCounts(1, 1, 0, 1, true), a.InterruptedLifecycle.Cancelled);
        Assert.Equal(1, a.Normalization!.PaidRounds);
        Assert.All(a.Groups.Values, cohort => Assert.Equal(1, cohort.Normalization!.PaidRounds));
        Assert.Equal(0, earlier.Count); Assert.Single(earlier.Analysis!.Groups); Assert.Equal(0, earlier.Analysis.InterruptedLifecycle!.Failed.InterruptedRounds);
        Assert.Equal(0, collector.Delta[0].Snapshot("feature").Analysis!.InterruptedLifecycle!.Cancelled.InterruptedRounds);
    }

    [Fact]
    public void NestingFaultWithholdsCompletenessAndGroupOverflowWithholdsCohorts()
    {
        var nested = new MeasurementCollector([Plan()]); nested.Begin();
        for (var i = 0; i < 17; i++) At(nested, "enter", "nested");
        nested.Interrupt(PaidRoundInterruption.Failed);
        var fault = Snapshot(nested).Analysis!.InterruptedLifecycle!.Failed;
        Assert.Equal(17, fault.Entries); Assert.Equal(16, fault.OpenInstances); Assert.False(fault.Complete);
        var overflow = new MeasurementCollector([Plan(groups: 1)]); overflow.Begin();
        At(overflow, "enter", "a"); At(overflow, "enter", "b"); overflow.Interrupt(PaidRoundInterruption.Cancelled);
        var a = Snapshot(overflow).Analysis!; Assert.False(a.GroupsComplete); Assert.Empty(a.Groups);
        Assert.Equal(new FeatureLifecycleCounts(1, 2, 0, 2, true), a.InterruptedLifecycle!.Cancelled);
        var unmatched = new MeasurementCollector([Plan()]); unmatched.Begin(); At(unmatched, "exit", "unknown"); unmatched.Interrupt(PaidRoundInterruption.Failed);
        Assert.False(Snapshot(unmatched).Analysis!.InterruptedLifecycle!.Failed.Complete);
    }

    [Fact]
    public void SamplerPublishesCancellationAuditAndNoPartialPayoutOrFeatureMean()
    {
        using var token = new CancellationTokenSource();
        var program = new ObservationSlot<Dict, BigInteger>("enter", new ObservationSlot<Dict, BigInteger>("reveal",
            Slot.Modify<Dict>(_ => { token.Cancel(); throw new OperationCanceledException(token.Token); }).Select(_ => BigInteger.One)));
        var progress = new List<SampledProgress>();
        var result = SampledInterpreter.Evaluate(program, new Dict(), new() { MaxSpins = 5, Measurements = [Plan()], CancellationToken = token.Token, ProgressCallback = progress.Add });
        Assert.True(result.WasCancelled); Assert.Equal(0, result.SpinsCompleted); Assert.Equal(1, result.Execution!.CancelledRounds);
        var metric = Assert.Single(result.Measurements);
        Assert.Equal(new FeatureLifecycleCounts(1, 1, 0, 1, true), metric.Analysis!.InterruptedLifecycle!.Cancelled);
        Assert.Equal(0, metric.Count); Assert.Null(metric.Mean);
        Assert.Equal(metric.Analysis.InterruptedLifecycle, Assert.Single(progress).Measurements[0].Analysis!.InterruptedLifecycle);
    }
}
