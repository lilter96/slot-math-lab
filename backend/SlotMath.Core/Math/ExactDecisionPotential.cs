namespace SlotMath.Core.Math;

public enum DecisionPotentialViolationKind
{
    HorizonSettlement,
    StopSettlement,
    ActionDrift
}

public sealed record DecisionPotentialViolation(
    int State,
    int? ActionId,
    DecisionPotentialViolationKind Kind,
    Rational ExpectedValue,
    Rational Potential,
    Rational Excess);

/// <summary>All-state/action Bellman super-solution, valid for bounded horizons and any restricted information policy.</summary>
public sealed class ExactDecisionPotential
{
    private ExactDecisionPotential(ExactDecisionModel model, Rational[] potential, Rational candidateBound,
        long violations, DecisionPotentialViolation? first, long work)
    {
        Model = model;
        Potential = Array.AsReadOnly(potential);
        CandidateInitialNetValue = candidateBound;
        ViolationCount = violations;
        FirstViolation = first;
        Work = work;
    }

    public ExactDecisionModel Model { get; }
    public IReadOnlyList<Rational> Potential { get; }
    public Rational CandidateInitialNetValue { get; }
    public long ViolationCount { get; }
    public DecisionPotentialViolation? FirstViolation { get; }
    public long Work { get; }
    public bool Holds => ViolationCount == 0;
    public Rational? InitialNetUpperBound => Holds ? CandidateInitialNetValue : null;
    public bool ProvesNoPositiveExpectedNet => Holds && CandidateInitialNetValue <= Rational.Zero;

    public static ExactDecisionPotential Verify(ExactDecisionModel model, IReadOnlyList<Rational> potential,
        ExactDecisionOptions? options = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(potential);
        ExactDecisionOptions limits = options ?? ExactDecisionOptions.Default;
        ExactLinearBudget budget = limits.Budget(cancellation);
        model.Validate(limits, ref budget);
        if (potential.Count != model.States.Count)
        {
            throw new ArgumentException("Supply one potential per state.");
        }

        budget.Spend(potential.Count);
        var snapshot = new Rational[potential.Count];
        Rational initial = Rational.Zero;
        for (var state = 0; state < potential.Count; state++)
        {
            Rational value = potential[state];
            budget.Value(value);
            snapshot[state] = value;
            initial = budget.Add(initial, budget.Multiply(model.Initial[state], value));
        }

        long violations = 0;
        DecisionPotentialViolation? first = null;
        for (var state = 0; state < model.States.Count; state++)
        {
            DecisionState source = model.States[state];
            if (budget.Less(snapshot[state], source.HorizonPayout))
            {
                violations++;
                first ??= Violation(state, null, DecisionPotentialViolationKind.HorizonSettlement,
                    source.HorizonPayout, snapshot[state], ref budget);
            }

            if (source.CanStop && budget.Less(snapshot[state], source.StopPayout))
            {
                violations++;
                first ??= Violation(state, null, DecisionPotentialViolationKind.StopSettlement,
                    source.StopPayout, snapshot[state], ref budget);
            }

            foreach (DecisionAction action in source.Actions)
            {
                Rational expectation = Rational.Zero;
                foreach (DecisionOutcome outcome in action.Outcomes)
                {
                    budget.Spend();
                    if (outcome.Probability.IsZero)
                    {
                        continue;
                    }

                    Rational next = budget.Add(outcome.Payout,
                        outcome.To < 0 ? Rational.Zero : snapshot[outcome.To]);
                    expectation = budget.Add(expectation, budget.Multiply(outcome.Probability, next));
                }

                Rational value = budget.Add(-action.Wager, budget.Multiply(model.Discount, expectation));
                if (budget.Less(snapshot[state], value))
                {
                    violations++;
                    first ??= Violation(state, action.Id, DecisionPotentialViolationKind.ActionDrift,
                        value, snapshot[state], ref budget);
                }
            }
        }

        Rational bound = budget.Add(initial, -model.EntryCost);
        budget.Value(bound);
        budget.Check();
        return new ExactDecisionPotential(model, snapshot, bound, violations, first, budget.Work);
    }

    private static DecisionPotentialViolation Violation(int state, int? action,
        DecisionPotentialViolationKind kind, Rational value, Rational potential, ref ExactLinearBudget budget) =>
        new(state, action, kind, value, potential, budget.Add(value, -potential));
}
