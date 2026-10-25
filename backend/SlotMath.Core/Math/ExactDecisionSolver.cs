using System.Numerics;

namespace SlotMath.Core.Math;

public enum DecisionInformation
{
    AuthoredObservations,
    FullState
}

public enum DecisionChoice
{
    Horizon,
    Stop,
    Action
}

public readonly record struct DecisionBeliefAtom(int State, Rational Probability);

public readonly record struct DecisionObservation(int Label, int Signal, Rational Payout);

public sealed record DecisionPolicyBranch(
    DecisionObservation Observation,
    Rational Probability,
    DecisionPolicyNode? Next);

public sealed record DecisionPolicyRoot(int Observation, Rational Probability, DecisionPolicyNode Node);

public sealed class DecisionPolicyNode
{
    internal DecisionPolicyNode(int remaining, IReadOnlyList<DecisionBeliefAtom> belief, Rational value,
        DecisionChoice choice, int? action, Rational wager, DecisionPolicyBranch[] branches)
    {
        Remaining = remaining;
        Belief = belief;
        Value = value;
        Choice = choice;
        ActionId = action;
        Wager = wager;
        Branches = Array.AsReadOnly(branches);
    }

    public int Remaining { get; }
    public IReadOnlyList<DecisionBeliefAtom> Belief { get; }
    public Rational Value { get; }
    public DecisionChoice Choice { get; }
    public int? ActionId { get; }
    public Rational Wager { get; }
    public IReadOnlyList<DecisionPolicyBranch> Branches { get; }
}

public sealed class ExactDecisionResult
{
    internal ExactDecisionResult(ExactDecisionModel model, DecisionInformation information, int horizon,
        DecisionPolicyRoot[] roots, Rational value, Rational net, long work, int nodes, int branches, int entries)
    {
        Model = model;
        Information = information;
        Horizon = horizon;
        Roots = Array.AsReadOnly(roots);
        Value = value;
        ExpectedNet = net;
        Work = work;
        EvaluatedBeliefs = nodes;
        ExploredBranches = branches;
        ChargedBeliefEntries = entries;
    }

    public ExactDecisionModel Model { get; }
    public DecisionInformation Information { get; }
    public int Horizon { get; }
    public IReadOnlyList<DecisionPolicyRoot> Roots { get; }

    /// <summary>Optimal present value after action wagers, before the once-only entry cost.</summary>
    public Rational Value { get; }

    public Rational ExpectedNet { get; }
    public long Work { get; }
    public int EvaluatedBeliefs { get; }
    public int ExploredBranches { get; }

    /// <summary>Cumulative posterior-weight slots and normalized belief entries charged during construction, not peak bytes.</summary>
    public int ChargedBeliefEntries { get; }
}

/// <summary>Exact finite-horizon belief-state Bellman solver; full-state information is an explicit relaxation.</summary>
public static class ExactDecisionSolver
{
    public static ExactDecisionResult Solve(ExactDecisionModel model, int horizon,
        DecisionInformation information = DecisionInformation.AuthoredObservations,
        ExactDecisionOptions? options = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new Engine(model, horizon, information, options ?? ExactDecisionOptions.Default, cancellation).Run();
    }

    private sealed class Belief : IEquatable<Belief>
    {
        private readonly int _hash;

        public Belief(DecisionBeliefAtom[] atoms)
        {
            Atoms = Array.AsReadOnly(atoms);
            var hash = new HashCode();
            foreach (DecisionBeliefAtom atom in atoms)
            {
                hash.Add(atom);
            }

            _hash = hash.ToHashCode();
        }

        public IReadOnlyList<DecisionBeliefAtom> Atoms { get; }

        public bool Equals(Belief? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null || Atoms.Count != other.Atoms.Count)
            {
                return false;
            }

            for (var at = 0; at < Atoms.Count; at++)
            {
                if (Atoms[at] != other.Atoms[at])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is Belief other && Equals(other);
        public override int GetHashCode() => _hash;
    }

    private sealed class Group(DecisionObservation observation)
    {
        public readonly DecisionObservation Observation = observation;
        public readonly Dictionary<int, Rational> Weights = new();
        public Rational Mass = Rational.Zero;
    }

    private sealed class Node(int remaining, Belief belief)
    {
        public readonly Belief Belief = belief;
        public readonly int Remaining = remaining;
        public WorkAction[] Actions = [];
        public DecisionPolicyNode? Policy;
        public Rational Value = Rational.Zero;
    }

    private sealed record WorkBranch(DecisionObservation Observation, Rational Probability, Node? Next);

    private sealed record WorkAction(int Id, Rational Wager, WorkBranch[] Branches);

    private sealed class Engine
    {
        private readonly int _horizon;
        private readonly DecisionInformation _information;
        private readonly List<Node>[] _levels;
        private readonly ExactDecisionOptions _limits;
        private readonly Dictionary<(int Remaining, Belief Belief), Node> _memo = new();
        private readonly ExactDecisionModel _model;
        private int _branches, _entries;
        private ExactLinearBudget _budget;

        public Engine(ExactDecisionModel model, int horizon, DecisionInformation information,
            ExactDecisionOptions limits, CancellationToken cancellation)
        {
            _limits = limits;
            _budget = limits.Budget(cancellation);
            model.Validate(limits, ref _budget);
            if (horizon < 0 || horizon > limits.MaxHorizon)
            {
                throw new ExactLinearLimitException("Decision horizon exceeds the allowance.");
            }

            if (!Enum.IsDefined(information))
            {
                throw new ArgumentOutOfRangeException(nameof(information));
            }

            _model = model;
            _horizon = horizon;
            _information = information;
            _budget.Spend(horizon + 1L);
            _levels = Enumerable.Range(0, horizon + 1).Select(_ => new List<Node>()).ToArray();
        }

        private void ReserveEntries(int count)
        {
            if (count > _limits.MaxBeliefEntries - _entries)
            {
                throw new ExactLinearLimitException("Decision posterior-storage allowance exhausted.");
            }

            _budget.Spend(count);
            _entries += count;
        }

        private int Label(int state) => state < 0 ? -1 :
            _information == DecisionInformation.FullState ? state : _model.States[state].Observation;

        private void Add(Dictionary<DecisionObservation, Group> groups, DecisionObservation observation,
            int target, Rational weight, bool actionBranch)
        {
            if (!groups.TryGetValue(observation, out Group? group))
            {
                if (actionBranch)
                {
                    if (_branches == _limits.MaxPolicyBranches)
                    {
                        throw new ExactLinearLimitException("Decision branch allowance exhausted.");
                    }

                    _branches++;
                }

                _budget.Spend();
                group = new Group(observation);
                groups.Add(observation, group);
            }

            group.Mass = _budget.Add(group.Mass, weight);
            if (target < 0)
            {
                return;
            }

            if (group.Weights.TryGetValue(target, out Rational previous))
            {
                group.Weights[target] = _budget.Add(previous, weight);
            }
            else
            {
                ReserveEntries(1);
                group.Weights.Add(target, weight);
            }
        }

        private Node Intern(Group group, int remaining)
        {
            ReserveEntries(group.Weights.Count);
            _budget.Spend(group.Weights.Count * (1L + BitOperations.Log2((uint)group.Weights.Count)));
            var states = group.Weights.Keys.ToArray();
            Array.Sort(states);
            var atoms = new DecisionBeliefAtom[states.Length];
            for (var index = 0; index < states.Length; index++)
            {
                var state = states[index];
                atoms[index] = new DecisionBeliefAtom(state,
                    ExactChainArithmetic.Divide(group.Weights[state], group.Mass, ref _budget));
            }

            var belief = new Belief(atoms);
            (int remaining, Belief belief) key = (remaining, belief);
            if (_memo.TryGetValue(key, out Node? prior))
            {
                return prior;
            }

            if (_memo.Count == _limits.MaxPolicyNodes)
            {
                throw new ExactLinearLimitException("Decision node allowance exhausted.");
            }

            var node = new Node(remaining, belief);
            _memo.Add(key, node);
            _levels[remaining].Add(node);
            return node;
        }

        private void Expand(Node node)
        {
            DecisionState menu = _model.States[node.Belief.Atoms[0].State];
            _budget.Spend(menu.Actions.Count);
            var actions = new WorkAction[menu.Actions.Count];
            for (var index = 0; index < actions.Length; index++)
            {
                var groups = new Dictionary<DecisionObservation, Group>();
                foreach (DecisionBeliefAtom atom in node.Belief.Atoms)
                {
                    DecisionAction action = _model.States[atom.State].Actions[index];
                    foreach (DecisionOutcome outcome in action.Outcomes)
                    {
                        _budget.Spend();
                        if (outcome.Probability.IsZero)
                        {
                            continue;
                        }

                        Rational weight = _budget.Multiply(atom.Probability, outcome.Probability);
                        Add(groups, new DecisionObservation(Label(outcome.To), outcome.Signal, outcome.Payout),
                            outcome.To, weight, true);
                    }
                }

                _budget.Spend(groups.Count);
                var branches = new WorkBranch[groups.Count];
                var at = 0;
                foreach (Group group in groups.Values)
                {
                    Node? next = group.Observation.Label < 0 ? null : Intern(group, node.Remaining - 1);
                    branches[at++] = new WorkBranch(group.Observation, group.Mass, next);
                }

                actions[index] = new WorkAction(menu.Actions[index].Id, menu.Actions[index].Wager, branches);
            }

            node.Actions = actions;
        }

        private Rational Settlement(Node node, bool horizon)
        {
            Rational sum = Rational.Zero;
            foreach (DecisionBeliefAtom atom in node.Belief.Atoms)
            {
                DecisionState state = _model.States[atom.State];
                sum = _budget.Add(sum, _budget.Multiply(atom.Probability,
                    horizon ? state.HorizonPayout : state.StopPayout));
            }

            return sum;
        }

        private void Reduce(Node node)
        {
            if (node.Remaining == 0)
            {
                node.Value = Settlement(node, true);
                node.Policy = new DecisionPolicyNode(0, node.Belief.Atoms, node.Value,
                    DecisionChoice.Horizon, null, Rational.Zero, []);
                return;
            }

            DecisionState menu = _model.States[node.Belief.Atoms[0].State];
            Rational? best = menu.CanStop ? Settlement(node, false) : null;
            WorkAction? selected = null;
            foreach (WorkAction action in node.Actions)
            {
                Rational sum = Rational.Zero;
                foreach (WorkBranch branch in action.Branches)
                {
                    Rational continuation = _budget.Add(branch.Observation.Payout, branch.Next?.Value ?? Rational.Zero);
                    sum = _budget.Add(sum, _budget.Multiply(branch.Probability, continuation));
                }

                Rational value = _budget.Add(-action.Wager, _budget.Multiply(_model.Discount, sum));
                if (best is null || _budget.Less(best.Value, value))
                {
                    best = value;
                    selected = action;
                }
            }

            node.Value = best!.Value;
            DecisionPolicyBranch[] branches = selected?.Branches.Select(branch =>
                new DecisionPolicyBranch(branch.Observation, branch.Probability, branch.Next?.Policy)).ToArray() ?? [];
            node.Policy = new DecisionPolicyNode(node.Remaining, node.Belief.Atoms, node.Value,
                selected is null ? DecisionChoice.Stop : DecisionChoice.Action, selected?.Id,
                selected?.Wager ?? Rational.Zero, branches);
        }

        public ExactDecisionResult Run()
        {
            var groups = new Dictionary<DecisionObservation, Group>();
            for (var state = 0; state < _model.States.Count; state++)
            {
                if (_model.Initial[state].IsZero)
                {
                    continue;
                }

                Add(groups, new DecisionObservation(Label(state), 0, Rational.Zero), state, _model.Initial[state],
                    false);
            }

            (Group Group, Node Node)[] roots =
                groups.Values.Select(group => (Group: group, Node: Intern(group, _horizon))).ToArray();
            for (var remaining = _horizon; remaining > 0; remaining--)
            {
                foreach (Node node in _levels[remaining])
                {
                    Expand(node);
                }
            }

            for (var remaining = 0; remaining <= _horizon; remaining++)
            {
                foreach (Node node in _levels[remaining])
                {
                    Reduce(node);
                }
            }

            Rational value = Rational.Zero;
            var policy = new DecisionPolicyRoot[roots.Length];
            for (var index = 0; index < roots.Length; index++)
            {
                (Group group, Node node) = roots[index];
                value = _budget.Add(value, _budget.Multiply(group.Mass, node.Value));
                policy[index] = new DecisionPolicyRoot(group.Observation.Label, group.Mass, node.Policy!);
            }

            Rational net = _budget.Add(value, -_model.EntryCost);
            _budget.Value(net);
            _budget.Check();
            return new ExactDecisionResult(_model, _information, _horizon, policy, value, net, _budget.Work,
                _memo.Count, _branches, _entries);
        }
    }
}
