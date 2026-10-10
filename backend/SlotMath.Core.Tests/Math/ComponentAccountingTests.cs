using SlotMath.Core.Expressions;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math;

public sealed class ComponentAccountingTests
{
    // Independent two-row specification: X=(0,2), Y=(0,2), Z=(2,0),
    // total=(2,4). Each component sample variance is 2. Pair covariances
    // are +2,-2,-2, so the true total variance is 6-4=2, not 6.
    private static readonly ComponentAccountingRequest Request = new("Three components", "total", ["x", "y", "z"], "residual");
    private static Expression Field(string key) => new FieldAccessExpr { Target = "state", Path = [key] };
    private static Expression Add(Expression x, Expression y) => new BinaryExpr { Op = BinaryOp.Add, Left = x, Right = y };
    private static Expression Sub(Expression x, Expression y) => new BinaryExpr { Op = BinaryOp.Sub, Left = x, Right = y };
    private static MeasurementDefinition Definition(string id, Expression value, Expression? pair = null, bool assertion = false, bool grouped = false) => new()
    {
        Id = id, Name = id, NodeId = "point", Value = value, Unit = "coins",
        Options = new() { Pair = pair, Assertion = assertion ? "zero" : "none", Group = grouped ? Field("group") : null, Stake = 2 }
    };
    private static MeasurementDefinition[] Plan(bool grouped = false) => [
        Definition("x", Field("x"), grouped: grouped), Definition("y", Field("y"), grouped: grouped), Definition("z", Field("z"), grouped: grouped),
        Definition("total", Field("total"), grouped: grouped),
        Definition("xy", Field("x"), Field("y"), grouped: grouped), Definition("xz", Field("x"), Field("z"), grouped: grouped), Definition("yz", Field("y"), Field("z"), grouped: grouped),
        Definition("residual", Sub(Field("total"), Add(Add(Field("x"), Field("y")), Field("z"))), assertion: true, grouped: grouped) ];

    private static MeasurementSnapshot[] Collect(MeasurementDefinition[] plan, int rows = 2, bool wrongTotal = false, int[]? totalValues = null)
    {
        var collector = new MeasurementCollector(plan);
        for (var round = 0; round < rows; round++)
        {
            collector.Begin(round);
            Dictionary<string, object?> state = new() { ["x"] = round % 2 * 2, ["y"] = round % 2 * 2, ["z"] = 2 - round % 2 * 2,
                ["total"] = totalValues?[round] ?? (2 + round % 2 * 2 + (wrongTotal && round == 0 ? 1 : 0)), ["group"] = "feature" };
            for (var i = 0; i < plan.Length; i++)
            {
                var d = plan[i];
                ExprValue Evaluate(Expression expression, Dictionary<string, object?> s) => ExactExpressionEvaluator.Evaluate(expression, new EvalContext { State = s });
                var binding = new MeasurementBinding<Dictionary<string, object?>>(s => Evaluate(d.Value!, s),
                    d.Filter is { } filter ? s => Evaluate(filter, s) : null,
                    Group: d.Options?.Group is { } group ? s => Evaluate(group, s) : null,
                    Pair: d.Options?.Pair is { } pair ? s => Evaluate(pair, s) : null);
                collector.Point(i, "point", state, binding);
            }
            collector.Commit();
        }
        return MeasurementCollector.Snapshot(plan, collector.Total);
    }

    [Fact]
    public void AllCrossTermsReconcileTheIndependentThreeComponentOracle()
    {
        var plan = Plan(); var report = ComponentAccounting.Calculate(Request, plan, Collect(plan));
        Assert.Equal("noObservedViolations", report.Status); Assert.Equal(2, report.Count); Assert.Equal(3, report.TotalMean);
        Assert.Equal(6, report.ComponentVarianceSum); Assert.Equal(-4, report.TwiceCovarianceSum); Assert.Equal(2, report.ReconstructedVariance);
        Assert.Equal(2, report.TotalSampleVariance); Assert.Equal(0, report.VarianceResidual); Assert.Equal(0, report.ExactViolations);
        Assert.All(report.Components, row => { Assert.Equal(1, row.Mean); Assert.Equal(2, row.SampleVariance); Assert.Equal(.5, row.PaidTurnoverContribution); });
    }

    [Fact]
    public void ExactResidualViolationsRemainSeparateFromNumericVarianceAgreement()
    {
        var plan = Plan(); var report = ComponentAccounting.Calculate(Request, plan, Collect(plan, wrongTotal: true));
        Assert.Equal("discrepancy", report.Status); Assert.Equal(1, report.ExactViolations); Assert.Equal(.5, report.MeanResidual);
    }

    [Theory][InlineData(0)][InlineData(1)]
    public void EmptyAndSingleObservationPopulationsCannotInventSampleVariances(int rows)
    {
        var plan = Plan(); var report = ComponentAccounting.Calculate(Request, plan, Collect(plan, rows));
        Assert.Equal("insufficient", report.Status); Assert.Null(report.ReconstructedVariance); Assert.Null(report.TotalSampleVariance);
    }

    [Fact]
    public void ReversedPairsAreValidButMissingOrChangedScopesCannotBeInferred()
    {
        var plan = Plan(); plan[4] = plan[4] with { Value = Field("y"), Options = plan[4].Options! with { Pair = Field("x") } };
        Assert.Equal("noObservedViolations", ComponentAccounting.Calculate(Request, plan, Collect(plan)).Status);
        Assert.Throws<ArgumentException>(() => ComponentAccounting.Calculate(Request, plan.Where(d => d.Id != "xz").ToArray(), Collect(plan)));
        var wrongScope = plan.Select(d => d.Id == "x" ? d with { Filter = new ConstantExpr { Kind = ConstantKind.Boolean, Value = "true" } } : d).ToArray();
        Assert.Throws<ArgumentException>(() => ComponentAccounting.Calculate(Request, wrongScope, Collect(wrongScope)));
    }

    [Fact]
    public void CohortAccountingUsesCompletePaidTurnoverAndCannotUseIncompleteGroups()
    {
        var plan = Plan(grouped: true); var values = Collect(plan); var request = Request with { GroupKey = "feature" };
        Assert.Equal("noObservedViolations", ComponentAccounting.Calculate(request, plan, values).Status);
        Assert.Equal("insufficient", ComponentAccounting.Calculate(request with { GroupKey = "never observed" }, plan, values).Status);
        values[0] = values[0] with { Analysis = values[0].Analysis! with { GroupsComplete = false } };
        Assert.Equal("invalid", ComponentAccounting.Calculate(request, plan, values).Status);
    }

    [Fact]
    public void InconsistentExposureAndInvalidObservationsWithholdTheVerdict()
    {
        var plan = Plan(); var values = Collect(plan);
        values[0] = values[0] with { Analysis = values[0].Analysis! with { Count = 1 } };
        Assert.Equal("invalid", ComponentAccounting.Calculate(Request, plan, values).Status);
        values = Collect(plan); values[0] = values[0] with { Errors = 1 };
        Assert.Equal("invalid", ComponentAccounting.Calculate(Request, plan, values).Status);
    }

    [Fact]
    public void WeightedAndWithinEpisodeAveragesRequireADifferentPopulationContract()
    {
        var plan = Plan(); plan[0] = plan[0] with { Options = plan[0].Options! with { Weight = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" } } };
        Assert.Throws<ArgumentException>(() => ComponentAccounting.Calculate(Request, plan, Collect(Plan())));
        plan = Plan(); plan[0] = plan[0] with { Options = plan[0].Options! with { Subject = "episode" } };
        Assert.Throws<ArgumentException>(() => ComponentAccounting.Calculate(Request, plan, Collect(Plan())));
    }
    [Fact]
    public void SevereCovarianceCancellationWithholdsTheFloatingPointVerdict()
    {
        var plan = Plan(); var values = Collect(plan);
        foreach (var id in new[] { "x", "y", "z", "xy", "xz", "yz" })
        {
            var i = Array.FindIndex(values, s => s.Id == id); var a = values[i].Analysis!;
            values[i] = values[i] with { Analysis = a with { Moments = a.Moments with { SampleVariance = 1e20 },
                Pair = a.Pair is { } pair ? pair with { SampleVarianceY = 1e20, Covariance = -5e19 } : null } };
        }
        var report = ComponentAccounting.Calculate(Request, plan, values);
        Assert.Equal("numericalResolution", report.Status); Assert.Equal(0, report.ExactViolations);
        Assert.Equal(2, report.VarianceResidual);
    }

    [Fact]
    public void NonFiniteAndNegativeMomentsNeverBecomeAnAccountingPass()
    {
        var plan = Plan(); var values = Collect(plan); var a = values[0].Analysis!;
        values[0] = values[0] with { Analysis = a with { Mean = double.NaN } };
        var report = ComponentAccounting.Calculate(Request, plan, values); Assert.Equal("invalid", report.Status);
        Assert.Null(report.Components[0].Mean); System.Text.Json.JsonSerializer.Serialize(report);
        values = Collect(plan); a = values[0].Analysis!;
        values[0] = values[0] with { Analysis = a with { Moments = a.Moments with { SampleVariance = -1 } } };
        Assert.Equal("invalid", ComponentAccounting.Calculate(Request, plan, values).Status);
    }

    [Fact]
    public void EqualAggregateMeansDoNotConcealWrongPerObservationAwards()
    {
        var plan = Plan(); var report = ComponentAccounting.Calculate(Request, plan, Collect(plan, totalValues: [3, 3]));
        Assert.Equal(0, report.MeanResidual); Assert.Equal(2, report.ExactViolations); Assert.Equal("discrepancy", report.Status);
    }

}
