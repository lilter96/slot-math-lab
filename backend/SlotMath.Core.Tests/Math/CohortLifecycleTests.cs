using SlotMath.Core.Expressions;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math;

public sealed class CohortLifecycleTests
{
    private sealed record Event(string Group, long Value = 1, bool Entry = true, bool Exit = true);
    private static MeasurementDefinition Definition(string subject = "episode", int limit = 32) => new()
    {
        Id = "cohort",
        Name = "Entry-selected feature",
        NodeId = "reveal",
        Options = new()
        {
            Subject = subject,
            EntryNodeId = "enter",
            ExitNodeId = "exit",
            Reduction = "average",
            GroupLimit = limit,
            Group = new ConstantExpr { Kind = ConstantKind.String, Value = "binding" }
        },
    };
    private static readonly MeasurementBinding<Event> Binding = new(
        e => ExprValue.Number(e.Value), null, Group: e => ExprValue.String(e.Group),
        EntryFilter: e => ExprValue.Bool(e.Entry), ExitFilter: e => ExprValue.Bool(e.Exit));
    private static MeasurementAnalysis Finish(MeasurementCollector collector) => collector.Total[0].Snapshot("cohort").Analysis!;

    [Fact]
    public void MultipleInstancesKeepEntryCohortsDespiteChangedExitState()
    {
        var collector = new MeasurementCollector([Definition()]); collector.Begin();
        collector.Point(0, "enter", new Event("sticky"), Binding);
        collector.Point(0, "reveal", new Event("ignored", 2), Binding);
        collector.Point(0, "exit", new Event("changed"), Binding);
        collector.Point(0, "enter", new Event("sticky"), Binding);
        collector.Point(0, "reveal", new Event("ignored", 4), Binding);
        collector.Point(0, "exit", new Event("changed"), Binding);
        collector.Point(0, "enter", new Event("other"), Binding);
        collector.Point(0, "reveal", new Event("ignored", 10), Binding);
        collector.Point(0, "exit", new Event("changed"), Binding); collector.Commit();
        var a = Finish(collector); Assert.Equal(3, a.Entries); Assert.Equal(3, a.Exits);
        var sticky = a.Groups["sticky"]; Assert.Equal(2, sticky.Entries); Assert.Equal(2, sticky.Exits); Assert.Equal(2, sticky.Count); Assert.Equal(3, sticky.Mean); Assert.Equal(1, sticky.DistinctParents);
        Assert.Equal(1, a.Groups["other"].Entries); Assert.False(a.Groups.ContainsKey("changed"));
    }

    [Fact]
    public void EmptyAndExitExcludedInstancesRemainVisibleWithoutInventingAValue()
    {
        var collector = new MeasurementCollector([Definition()]); collector.Begin();
        collector.Point(0, "enter", new Event("empty"), Binding); collector.Point(0, "exit", new Event("changed"), Binding);
        collector.Point(0, "enter", new Event("excluded"), Binding); collector.Point(0, "reveal", new Event("ignored", 4), Binding);
        collector.Point(0, "exit", new Event("changed", Exit: false), Binding);
        collector.Point(0, "enter", new Event("outside", Entry: false), Binding); collector.Point(0, "exit", new Event("outside"), Binding); collector.Commit();
        var a = Finish(collector); Assert.Equal(3, a.Entries); Assert.Equal(3, a.Exits); Assert.Equal(0, a.Count);
        Assert.Equal(2, a.Groups.Count); Assert.False(a.Groups.ContainsKey("outside"));
        foreach (var g in a.Groups.Values) { Assert.Equal(1, g.Entries); Assert.Equal(1, g.Exits); Assert.Equal(0, g.Count); Assert.Null(g.Mean); Assert.Equal(0, g.DistinctParents); Assert.Equal(1, g.Normalization!.PaidRounds); }
        Assert.All(a.Tails.Concat(a.Groups.Values.SelectMany(g => g.Tails)), t => { Assert.Equal(0, t.Count); Assert.Equal(0, t.Sum); Assert.Null(t.Probability); Assert.Null(t.SecondMoment); });
    }

    [Theory]
    [InlineData("episode")]
    [InlineData("transition")]
    public void OpenInstancesReconcileInTheEntryCohort(string subject)
    {
        var collector = new MeasurementCollector([Definition(subject)]); collector.Begin();
        collector.Point(0, "enter", new Event("sticky"), Binding); collector.Commit();
        var a = Finish(collector); var sticky = a.Groups["sticky"];
        Assert.Equal(1, a.UnclosedEpisodes); Assert.Equal(1, sticky.UnclosedEpisodes);
        Assert.Equal(sticky.Entries, sticky.Exits + sticky.UnclosedEpisodes); Assert.Equal(0, sticky.Count); Assert.Null(sticky.MeanInterval);
    }

    [Fact]
    public void EmptyCohortParentsMergeOnceAcrossSparseChunksAndCannotLeakResetCounters()
    {
        var definition = Definition(); var collector = new MeasurementCollector([definition]);
        collector.Begin(); collector.Commit();
        collector.Begin(); collector.Point(0, "enter", new Event("empty"), Binding); collector.Point(0, "exit", new Event("changed"), Binding); collector.Commit();
        collector.Begin(); collector.Commit();
        var other = new MeasurementCollector([definition]); other.Begin(); other.Commit();
        other.Begin(); other.Point(0, "enter", new Event("empty"), Binding); other.Point(0, "exit", new Event("changed"), Binding); other.Commit();
        MeasurementCollector.Merge(collector.Total, other.Total);
        var empty = Finish(collector).Groups["empty"];
        Assert.Equal(2, empty.Entries); Assert.Equal(2, empty.Exits); Assert.Equal(5, empty.Normalization!.PaidRounds); Assert.Equal(0, empty.Count); Assert.Null(empty.Sum);
    }

    [Fact]
    public void UncommittedRoundsAndGroupOverflowPreserveTheCorrectAccountingPopulation()
    {
        var collector = new MeasurementCollector([Definition(limit: 1)]); collector.Begin();
        collector.Point(0, "enter", new Event("discarded"), Binding);
        collector.Begin(); collector.Point(0, "enter", new Event("a"), Binding); collector.Point(0, "exit", new Event("changed"), Binding);
        collector.Point(0, "enter", new Event("b"), Binding); collector.Point(0, "exit", new Event("changed"), Binding); collector.Commit();
        var a = Finish(collector); Assert.Equal(2, a.Entries); Assert.Equal(2, a.Exits); Assert.False(a.GroupsComplete); Assert.Empty(a.Groups); Assert.Equal(0, a.UnclosedEpisodes);
    }
}
