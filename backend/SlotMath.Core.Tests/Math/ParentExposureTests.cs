using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math;

public sealed class ParentExposureTests
{
    private sealed record Child(string Group = "sticky", bool Included = true, bool Exit = true, long Value = 1);
    private static MeasurementDefinition Definition(string subject = "episode", int groupLimit = 32) => new()
    {
        Id = "parents", Name = "Matching parent exposure", NodeId = "reveal",
        Options = new() { Subject = subject, Reduction = "sum", EntryNodeId = "enter", ExitNodeId = "exit", GroupLimit = groupLimit,
            Group = new ConstantExpr { Kind = ConstantKind.String, Value = "binding" } }
    };
    private static readonly MeasurementBinding<Child> Binding = new(c => ExprValue.Number(c.Value), c => ExprValue.Bool(c.Included),
        Group: c => ExprValue.String(c.Group), EntryFilter: c => ExprValue.Bool(c.Included), ExitFilter: c => ExprValue.Bool(c.Exit));
    private static MeasurementAnalysis Finish(MeasurementCollector collector) => collector.Total[0].Snapshot("parents").Analysis!;
    private static void Enter(MeasurementCollector collector, Child child) => collector.Point(0, "enter", child, Binding);
    private static void Reveal(MeasurementCollector collector, Child child) => collector.Point(0, "reveal", child, Binding);
    private static void Exit(MeasurementCollector collector, Child child) => collector.Point(0, "exit", child, Binding);

    [Fact]
    public void RepeatedChildrenAndMultipleEpisodesHaveDifferentParentDenominators()
    {
        var collector = new MeasurementCollector([Definition()]); collector.Begin();
        Enter(collector, new()); for (var i = 0; i < 10; i++) Reveal(collector, new()); Exit(collector, new());
        Enter(collector, new()); Reveal(collector, new()); Exit(collector, new());
        collector.Commit(); var a = Finish(collector);
        Assert.Equal(2, a.Count); Assert.Equal(11, a.Sum);
        Assert.Equal(new ParentExposure(1, 2), a.ParentExposure);
        Assert.Equal(new ParentExposure(1, 2), a.Groups["sticky"].ParentExposure);
        Assert.Equal(1, a.Normalization!.PaidRounds);
    }

    [Fact]
    public void NestedChildrenBelongOnlyToTheirInnermostOwningInstance()
    {
        var collector = new MeasurementCollector([Definition()]); collector.Begin();
        Enter(collector, new("outer")); Enter(collector, new("inner"));
        Reveal(collector, new("ignored")); Reveal(collector, new("ignored"));
        Exit(collector, new("changed")); Exit(collector, new("changed")); collector.Commit();
        var a = Finish(collector); Assert.Equal(2, a.Entries); Assert.Equal(2, a.Count); // Outer sum is a deliberate empty identity.
        Assert.Equal(new ParentExposure(1, 1), a.ParentExposure);
        Assert.Equal(new ParentExposure(0, 0), a.Groups["outer"].ParentExposure);
        Assert.Equal(new ParentExposure(1, 1), a.Groups["inner"].ParentExposure);
    }

    [Fact]
    public void MatchingExposurePrecedesExitFilteringButExcludedChildrenCannotActivateAParent()
    {
        var collector = new MeasurementCollector([Definition()]); collector.Begin();
        Enter(collector, new("accepted")); Reveal(collector, new("ignored", Value: 0)); Exit(collector, new(Exit: false));
        Enter(collector, new("empty")); Reveal(collector, new(Included: false)); Exit(collector, new());
        Enter(collector, new("outside", Included: false)); Reveal(collector, new()); Exit(collector, new()); collector.Commit();
        var a = Finish(collector); Assert.Equal(new ParentExposure(1, 1), a.ParentExposure);
        Assert.Equal(new ParentExposure(1, 1), a.Groups["accepted"].ParentExposure);
        Assert.Equal(0, a.Groups["accepted"].Count); // Included child was a real zero; the reduced value was exit-excluded.
        Assert.Equal(new ParentExposure(0, 0), a.Groups["empty"].ParentExposure);
        Assert.False(a.Groups.ContainsKey("outside"));
    }

    [Fact]
    public void SettledOpenEpisodesKeepExposureButUncommittedRoundsDoNotLeak()
    {
        var collector = new MeasurementCollector([Definition()]); collector.Begin();
        Enter(collector, new("discarded")); Reveal(collector, new());
        collector.Begin(1); Enter(collector, new("open")); Reveal(collector, new()); collector.Commit();
        var a = Finish(collector); Assert.Equal(new ParentExposure(1, 1), a.ParentExposure);
        Assert.Equal(1, a.UnclosedEpisodes); Assert.Equal(0, a.Count);
        Assert.Equal(new ParentExposure(1, 1), a.Groups["open"].ParentExposure); Assert.False(a.Groups.ContainsKey("discarded"));
    }

    [Fact]
    public void SparseWorkerChunksAndResetDoNotMultiplyMatchingParents()
    {
        var first = new MeasurementCollector([Definition()]); var second = new MeasurementCollector([Definition()]);
        for (var round = 0; round < 6; round++)
        {
            var collector = round < 3 ? first : second; collector.Begin(round);
            if (round is 2 or 5) { Enter(collector, new()); Reveal(collector, new()); Reveal(collector, new()); Exit(collector, new()); }
            collector.Commit();
        }
        MeasurementCollector.Merge(first.Total, second.Total); var a = Finish(first);
        Assert.Equal(new ParentExposure(2, 2), a.ParentExposure);
        Assert.Equal(new ParentExposure(2, 2), a.Groups["sticky"].ParentExposure); Assert.Equal(6, a.Groups["sticky"].Normalization!.PaidRounds);
        first.Begin(6); first.Commit(); Assert.Equal(new ParentExposure(2, 2), Finish(first).ParentExposure);
    }

    [Fact]
    public void InvalidChildrenAndGroupBudgetCannotInventExposureOrEraseTheOverallCount()
    {
        var collector = new MeasurementCollector([Definition(groupLimit: 1)]); collector.Begin();
        Enter(collector, new("a"));
        collector.Point(0, "reveal", new Child(), Binding with { Value = _ => throw new InvalidOperationException("Invalid child.") });
        Exit(collector, new()); Enter(collector, new("b")); Reveal(collector, new()); Exit(collector, new()); collector.Commit();
        var a = Finish(collector); Assert.Equal(new ParentExposure(1, 1), a.ParentExposure);
        Assert.False(a.GroupsComplete); Assert.Empty(a.Groups);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void BothEnginesDistinguishAcceptedChildrenFromEmptyRoundReduction(bool optimize)
    {
        var value = new FieldAccessExpr { Target = "state", Path = ["spinWin"] };
        var always = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "false" };
        MeasurementDefinition[] plan = [
            new() { Id = "episode", Name = "Episode", NodeId = "end", Value = value, Options = new() { Subject = "episode", EntryNodeId = "fs", ExitNodeId = "sink" } },
            new() { Id = "empty", Name = "Empty round", NodeId = "end", Value = value, Filter = always, Options = new() { Subject = "round" } },
            new() { Id = "events", Name = "False is an accepted value", NodeId = "end", Value = always, Options = new() { Source = "event" } } ];
        var compiled = new GraphCompiler(optimizeSampling: optimize).Compile(MeasurementTests.Model, plan); Assert.True(compiled.IsValid);
        var result = SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(), new() { Measurements = plan, MaxSpins = 20 });
        Assert.Equal(new ParentExposure(20, 20), result.Measurements[0].Analysis!.ParentExposure);
        Assert.Equal(20, result.Measurements[1].Count); Assert.Equal(0, result.Measurements[1].Sum);
        Assert.Equal(new ParentExposure(0, null), result.Measurements[1].Analysis!.ParentExposure);
        Assert.Equal(60, result.Measurements[2].Count); Assert.Equal(0, result.Measurements[2].Sum);
        Assert.Equal(new ParentExposure(20, null), result.Measurements[2].Analysis!.ParentExposure);
        var exact = GraphMeasurementEnumeration.Evaluate(compiled, plan, 10);
        Assert.Equal(new EnumeratedParentExposure("1/1", "1/1", true), exact.Measurements[0].ParentExposure);
        Assert.Equal(new EnumeratedParentExposure("0/1", null, true), exact.Measurements[1].ParentExposure);
        Assert.Equal(new EnumeratedParentExposure("1/1", null, true), exact.Measurements[2].ParentExposure);
    }
}
