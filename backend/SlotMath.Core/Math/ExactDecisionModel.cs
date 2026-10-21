using System.Numerics;

namespace SlotMath.Core.Math;

public sealed record ExactDecisionOptions
{
    public static ExactDecisionOptions Default { get; } = new();
    public int MaxStates { get; init; } = 256;
    public int MaxActions { get; init; } = 32;
    public int MaxOutcomes { get; init; } = 100000;
    public int MaxHorizon { get; init; } = 128;
    public int MaxPolicyNodes { get; init; } = 100000;
    public int MaxPolicyBranches { get; init; } = 1000000;
    public int MaxBeliefEntries { get; init; } = 1000000;
    public int MaximumBits { get; init; } = 65536;
    public long MaxWork { get; init; } = 40000000;
    public TimeSpan? MaxTime { get; init; }

    internal ExactLinearBudget Budget(CancellationToken cancellation)
    {
        if (MaxStates is < 1 or > 4096 || MaxActions is < 1 or > 1024 ||
            MaxOutcomes is < 1 or > 1000000 || MaxHorizon is < 0 or > 4096 ||
            MaxPolicyNodes is < 1 or > 1000000 || MaxPolicyBranches is < 1 or > 4000000 ||
            MaxBeliefEntries is < 1 or > 4000000)
        {
            throw new ArgumentException("Invalid finite-decision representation limits.");
        }

        return new ExactLinearBudget(new ExactLinearOptions
        {
            MaxStates = MaxStates,
            MaxCoefficients = MaxOutcomes,
            MaximumBits = MaximumBits,
            MaxWork = MaxWork,
            MaxTime = MaxTime
        }, cancellation);
    }
}

/// <summary>Joint transition and payout. To=-1 terminates; Signal and the realized payout are observed after acting.</summary>
public readonly record struct DecisionOutcome(int To, Rational Probability, Rational Payout, int Signal = 0);

public sealed record DecisionAction(int Id, Rational Wager, IReadOnlyList<DecisionOutcome> Outcomes);

public sealed record DecisionState(
    int Observation,
    bool CanStop,
    Rational StopPayout,
    Rational HorizonPayout,
    IReadOnlyList<DecisionAction> Actions);

/// <summary>
///     Immutable supplied finite model. All future-relevant information, including bankroll constraints, must be in
///     state.
/// </summary>
public sealed class ExactDecisionModel
{
    private ExactDecisionModel(DecisionState[] states, Rational[] initial, Rational entryCost,
        Rational discount, int outcomes)
    {
        States = Array.AsReadOnly(states);
        Initial = Array.AsReadOnly(initial);
        EntryCost = entryCost;
        Discount = discount;
        OutcomeCount = outcomes;
    }

    public IReadOnlyList<DecisionState> States { get; }
    public IReadOnlyList<Rational> Initial { get; }
    public Rational EntryCost { get; }
    public Rational Discount { get; }
    public int OutcomeCount { get; }

    public static ExactDecisionModel Create(IReadOnlyList<DecisionState> states, IReadOnlyList<Rational> initial,
        Rational entryCost, Rational? discount = null, ExactDecisionOptions? options = null,
        CancellationToken cancellation = default)
    {
        ExactDecisionOptions limits = options ?? ExactDecisionOptions.Default;
        ExactLinearBudget budget = limits.Budget(cancellation);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(initial);
        if (states.Count < 1 || states.Count > limits.MaxStates || initial.Count != states.Count)
        {
            throw new ArgumentException("Supply bounded states and one complete initial law.");
        }

        Nonnegative(entryCost, ref budget);
        Rational beta = discount ?? Rational.One;
        budget.Value(beta);
        if (beta <= Rational.Zero || beta > Rational.One)
        {
            throw new ArgumentOutOfRangeException(nameof(discount));
        }

        budget.Spend(2L * states.Count);
        var snapshots = new DecisionState[states.Count];
        var initialSnapshot = new Rational[states.Count];
        var menus = new Dictionary<int, DecisionState>();
        Rational initialMass = Rational.Zero;
        var count = 0;
        for (var state = 0; state < states.Count; state++)
        {
            DecisionState input = states[state] ?? throw new ArgumentException("States cannot be null.");
            if (input.Observation < 0 || input.Actions is null || input.Actions.Count > limits.MaxActions ||
                (!input.CanStop && input.Actions.Count == 0))
            {
                throw new ArgumentException("Use a nonnegative observation and a bounded, nonempty decision menu.");
            }

            Nonnegative(input.StopPayout, ref budget);
            Nonnegative(input.HorizonPayout, ref budget);
            Rational initialProbability = initial[state];
            Nonnegative(initialProbability, ref budget);
            initialMass = budget.Add(initialMass, initialProbability);
            initialSnapshot[state] = initialProbability;
            budget.Spend(input.Actions.Count);
            var actions = new DecisionAction[input.Actions.Count];
            for (var index = 0; index < actions.Length; index++)
            {
                DecisionAction action = input.Actions[index] ?? throw new ArgumentException("Actions cannot be null.");
                Nonnegative(action.Wager, ref budget);
                if (action.Id < 0 || action.Outcomes is not { Count: > 0 } ||
                    action.Outcomes.Count > limits.MaxOutcomes - count)
                {
                    throw new ArgumentException("Use nonnegative action IDs and bounded nonempty outcome laws.");
                }

                count += action.Outcomes.Count;
                budget.Spend(action.Outcomes.Count);
                var outcomes = new DecisionOutcome[action.Outcomes.Count];
                Rational mass = Rational.Zero;
                for (var at = 0; at < outcomes.Length; at++)
                {
                    DecisionOutcome outcome = action.Outcomes[at];
                    Nonnegative(outcome.Probability, ref budget);
                    Nonnegative(outcome.Payout, ref budget);
                    if (outcome.To < -1 || outcome.To >= states.Count || outcome.Signal < 0)
                    {
                        throw new ArgumentException(
                            "Use a valid target or -1 for termination, and a nonnegative signal.");
                    }

                    mass = budget.Add(mass, outcome.Probability);
                    outcomes[at] = outcome;
                }

                if (mass != Rational.One)
                {
                    throw new ArgumentException("Each action law must have mass exactly one.");
                }

                actions[index] = new DecisionAction(action.Id, action.Wager, Array.AsReadOnly(outcomes));
            }

            budget.Spend(actions.Length * (1L + (actions.Length == 0 ? 0 : BitOperations.Log2((uint)actions.Length))));
            Array.Sort(actions, (a, b) => a.Id.CompareTo(b.Id));
            for (var at = 1; at < actions.Length; at++)
            {
                if (actions[at].Id == actions[at - 1].Id)
                {
                    throw new ArgumentException("Action IDs must be unique within a state.");
                }
            }

            var snapshot = new DecisionState(input.Observation, input.CanStop, input.StopPayout,
                input.HorizonPayout, Array.AsReadOnly(actions));
            if (menus.TryGetValue(input.Observation, out DecisionState? menu))
            {
                if (menu.CanStop != snapshot.CanStop || menu.Actions.Count != actions.Length)
                {
                    throw new ArgumentException("States with the same observation must expose the same decision menu.");
                }

                for (var at = 0; at < actions.Length; at++)
                {
                    budget.Spend();
                    if (menu.Actions[at].Id != actions[at].Id || menu.Actions[at].Wager != actions[at].Wager)
                    {
                        throw new ArgumentException(
                            "Action availability and quoted wagers cannot reveal unmodelled hidden state.");
                    }
                }
            }
            else
            {
                menus.Add(input.Observation, snapshot);
            }

            snapshots[state] = snapshot;
        }

        if (initialMass != Rational.One)
        {
            throw new ArgumentException("Initial probabilities must have mass exactly one.");
        }

        budget.Check();
        return new ExactDecisionModel(snapshots, initialSnapshot, entryCost, beta, count);
    }

    internal void Validate(ExactDecisionOptions limits, ref ExactLinearBudget budget)
    {
        if (States.Count > limits.MaxStates || OutcomeCount > limits.MaxOutcomes)
        {
            throw new ExactLinearLimitException("Decision model exceeds the requested state/outcome allowance.");
        }

        budget.Value(EntryCost);
        budget.Value(Discount);
        for (var state = 0; state < States.Count; state++)
        {
            DecisionState source = States[state];
            if (source.Actions.Count > limits.MaxActions)
            {
                throw new ExactLinearLimitException("Decision model exceeds the requested action allowance.");
            }

            budget.Value(Initial[state]);
            budget.Value(source.StopPayout);
            budget.Value(source.HorizonPayout);
            foreach (DecisionAction action in source.Actions)
            {
                budget.Value(action.Wager);
                foreach (DecisionOutcome outcome in action.Outcomes)
                {
                    budget.Value(outcome.Probability);
                    budget.Value(outcome.Payout);
                }
            }
        }
    }

    private static void Nonnegative(Rational value, ref ExactLinearBudget budget)
    {
        budget.Value(value);
        if (value < Rational.Zero)
        {
            throw new ArgumentException("Probabilities, payouts and wagers must be nonnegative.");
        }
    }
}
