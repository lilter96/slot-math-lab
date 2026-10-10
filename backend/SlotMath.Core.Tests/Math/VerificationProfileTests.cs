using SlotMath.Core.Expressions;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math;

public sealed class VerificationProfileTests
{
    private static MeasurementDefinition D(string id, MeasurementOptions? options = null) => new()
    { Id = id, Name = id, NodeId = "point", Value = new FieldAccessExpr { Target = "state", Path = [id] }, Options = options };
    private static MeasurementOptions Precision(int family = 2) => new() { ReferenceMean = 1, Tolerance = .2, Confidence = .95, ErrorFamilySize = family, IndependentSubjects = true };
    private static VerificationProfile Profile(params VerificationCriterion[] criteria) => new("Required math evidence", .95, criteria);
    private static MeasurementSnapshot[] Collect(MeasurementDefinition[] plan, int count, bool wrong = false, bool filtered = false)
    {
        var collector = new MeasurementCollector(plan);
        for (var i = 0; i < count; i++)
        {
            collector.Begin(i);
            for (var d = 0; d < plan.Length; d++)
                collector.Point(d, "point", i, new MeasurementBinding<int>(_ => ExprValue.Number(plan[d].Id == "residual" ? wrong && i == 0 ? 1 : 0 : i % 2 * 2), filtered ? _ => ExprValue.Bool(false) : null));
            collector.Commit();
        }
        return MeasurementCollector.Snapshot(plan, collector.Total);
    }

    [Fact]
    public void PredeclaredMeanFamilyUsesIndependentKnownAlternatingOracleAndAllChecks()
    {
        // Manual specification: both columns alternate 0,2. Each mean is 1,
        // sample variance is n/(n-1), and correlation does not change the union bound.
        MeasurementDefinition[] plan = [D("x", Precision()), D("y", Precision()), D("residual", new() { Assertion = "zero" })];
        var profile = Profile(new VerificationCriterion("x", "mean-equivalence", 1000), new VerificationCriterion("y", "mean-equivalence", 1000), new VerificationCriterion("residual", "exact-zero-assertion"), new VerificationCriterion("x", "observation-integrity"));
        var report = ProfileVerification.Calculate(profile, plan, Collect(plan, 2000), true);
        Assert.Equal("criteriaMet", report.Status); Assert.Equal(.05, report.AllocatedAlpha, 14);
        Assert.Equal(64, report.ProfileHash.Length); Assert.Equal(ProfileVerification.Hash(profile), report.ProfileHash);
        Assert.All(report.Criteria.Take(2), row => { Assert.Equal("withinPrecision", row.Status); Assert.InRange(row.Interval!.Lower, .8, 1); Assert.InRange(row.Interval.Upper, 1, 1.2); });
        Assert.All(report.Criteria.Skip(2), row => Assert.Equal("noObservedViolations", row.Status));
        Assert.NotEqual(report.ProfileHash, ProfileVerification.Hash(profile with { Criteria = [new VerificationCriterion("x", "mean-equivalence", 999)] }));
    }

    [Fact]
    public void UnderallocatedFamilyAndNonrejectionCannotBeDeclaredAsAcceptance()
    {
        MeasurementDefinition[] plan = [D("x", Precision(1)), D("y", Precision(1))];
        Assert.Throws<ArgumentException>(() => ProfileVerification.Validate(Profile(new VerificationCriterion("x", "mean-equivalence"), new VerificationCriterion("y", "mean-equivalence")), plan));
        Assert.Throws<ArgumentException>(() => ProfileVerification.Validate(Profile(new VerificationCriterion("x", "distribution-goodness-of-fit")), plan));
        Assert.Throws<ArgumentException>(() => ProfileVerification.Validate(Profile(new VerificationCriterion("missing", "observation-integrity")), plan));
        Assert.Throws<ArgumentException>(() => ProfileVerification.Validate(Profile(new VerificationCriterion("x", "observation-integrity"), new VerificationCriterion("x", "observation-integrity")), plan));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2000, true)]
    public void EmptyWideFilteredAndInterruptedEvidenceIsInsufficient(int count, bool filtered)
    {
        MeasurementDefinition[] plan = [D("x", Precision())]; var profile = Profile(new VerificationCriterion("x", "mean-equivalence"));
        Assert.Equal("insufficient", ProfileVerification.Calculate(profile, plan, Collect(plan, count, filtered: filtered), true).Status);
        Assert.Equal("insufficient", ProfileVerification.Calculate(profile, plan, Collect(plan, 2000), false).Status);
    }

    [Fact]
    public void ExactFailureCannotBeHiddenByMinimumExposureOrAnEqualAggregate()
    {
        MeasurementDefinition[] plan = [D("residual", new() { Assertion = "zero" })]; var profile = Profile(new VerificationCriterion("residual", "exact-zero-assertion", 1000));
        var report = ProfileVerification.Calculate(profile, plan, Collect(plan, 2, wrong: true), true);
        Assert.Equal("discrepancy", report.Status); Assert.Equal(1, report.Criteria[0].Evidence!.Observed);
        Assert.Equal("insufficient", ProfileVerification.Calculate(profile, plan, Collect(plan, 2), true).Status);
    }

    [Fact]
    public void BoundViolationsBecomeInvalidRatherThanConstantPrecision()
    {
        MeasurementDefinition[] plan = [D("x", Precision() with { LowerBound = 1, UpperBound = 1 })];
        var snapshots = Collect(plan, 2); Assert.Equal(2, snapshots[0].Errors);
        Assert.Equal("invalid", ProfileVerification.Calculate(Profile(new VerificationCriterion("x", "mean-equivalence")), plan, snapshots, true).Status);
        Assert.DoesNotContain(snapshots[0].Analysis?.Checks ?? [], check => check.Status == "withinPrecision");
    }

    [Fact]
    public void SupportChecksExposeUnexpectedOutcomesAndWithholdOverflow()
    {
        MeasurementDefinition[] plan = [D("x", new() { ReferenceDistribution = [new(0, 1)], SupportLimit = 1 })];
        var profile = Profile(new VerificationCriterion("x", "reference-support"));
        Assert.Equal("insufficient", ProfileVerification.Calculate(profile, plan, Collect(plan, 2), true).Status);
        plan[0] = plan[0] with { Options = plan[0].Options! with { SupportLimit = 2 } };
        Assert.Equal("discrepancy", ProfileVerification.Calculate(profile, plan, Collect(plan, 2), true).Status);
        Assert.Equal("criteriaMet", ProfileVerification.Calculate(profile, plan, Collect(plan, 1), true).Status);
    }

    [Fact]
    public void CorruptExposureAndWeightedPrecisionCannotAcquireAcceptance()
    {
        MeasurementDefinition[] plan = [D("x", Precision())]; var snapshots = Collect(plan, 2000);
        var profile = Profile(new VerificationCriterion("x", "mean-equivalence"));
        Assert.Equal("invalid", ProfileVerification.Calculate(profile, plan, [snapshots[0] with { Errors = 1 }], true).Status);
        Assert.Throws<ArgumentException>(() => ProfileVerification.Calculate(profile, plan, [snapshots[0], snapshots[0]], true));
        plan[0] = plan[0] with { Options = plan[0].Options! with { Weight = new ConstantExpr { Kind = ConstantKind.Integer, Value = "1" } } };
        Assert.Throws<ArgumentException>(() => ProfileVerification.Validate(profile, plan));
        plan[0] = plan[0] with { Options = Precision() with { IndependentSubjects = false, IndependentParents = true, ReferenceStatistic = "probability" } };
        Assert.Throws<ArgumentException>(() => ProfileVerification.Validate(profile, plan));
    }

    [Theory]
    [InlineData(.5, "withinPrecision")]
    [InlineData(1, "discrepancy")]
    public void NumericNonzeroProbabilityUsesEventCountRatherThanBoundedValueMean(double reference, string status)
    {
        // Independent specification: values alternate 0 and 2. The numeric mean
        // is 1 but the nonzero-event probability is 1/2. A narrow bounded value
        // mean interval around 1 cannot validate a probability reference of 1.
        MeasurementDefinition[] plan = [D("x", Precision() with { ReferenceStatistic = "probability", ReferenceMean = reference, Tolerance = .1, LowerBound = 0, UpperBound = 2 })];
        var snapshots = Collect(plan, 20000); var check = Assert.Single(snapshots[0].Analysis!.Checks, c => c.Id == "mean-equivalence");
        Assert.Equal(status, check.Status); Assert.Equal(.5, check.Observed);
        var row = Assert.Single(ProfileVerification.Calculate(Profile(new VerificationCriterion("x", "mean-equivalence")), plan, snapshots, true).Criteria);
        Assert.Equal(status, row.Status); Assert.Equal("Clopper–Pearson, two-sided", row.Interval!.Method);
        Assert.InRange(row.Interval.Lower, .48, .5); Assert.InRange(row.Interval.Upper, .5, .52);
    }

    [Fact]
    public void InternalBooleanSessionEvidenceRetainsItsBoundedSequentialInterval()
    {
        var sessions = new SlotMath.Core.Math.SessionEvidence();
        for (var i = 0; i < 20; i++) sessions.Add("ruin", i % 2);
        var snapshot = Assert.Single(sessions.Snapshot()); Assert.Equal(.5, snapshot.Mean);
        Assert.NotNull(snapshot.Analysis!.SequentialMeanInterval); Assert.NotNull(snapshot.Analysis.ProbabilityInterval);
        Assert.True(ReferenceSemantics.BooleanValue(new() { Source = "event", Subject = "session" }));
        Assert.False(ReferenceSemantics.BooleanValue(new() { Subject = "transition", Reduction = "any" }));
    }
}
