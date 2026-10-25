using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

/// <summary>
///     Exhaustive observation-history policies, evaluated by forward path ledgers; no belief-state/Bellman
///     recurrence.
/// </summary>
internal static class DecisionPolicyOracle
{
    private static int Label(ExactDecisionModel model, int state, DecisionInformation information) =>
        information == DecisionInformation.FullState ? state : model.States[state].Observation;

    private static string Root(int observation) => "o" + observation;

    private static string Next(string history, int action, int observation, DecisionOutcome outcome) =>
        history + "/a" + action + "/o" + observation + "/s" + outcome.Signal + "/p" + outcome.Payout;

    internal static (Rational BestNet, long Policies) Enumerate(ExactDecisionModel model, int horizon,
        DecisionInformation information)
    {
        var histories = new Dictionary<string, int[]>();

        void Discover(int state, int remaining, string history)
        {
            if (remaining == 0)
            {
                return;
            }

            DecisionState source = model.States[state];
            histories.TryAdd(history, (source.CanStop ? new[] { -1 } : [])
                .Concat(source.Actions.Select(action => action.Id)).ToArray());
            foreach (DecisionAction action in source.Actions)
            {
                foreach (DecisionOutcome outcome in action.Outcomes)
                {
                    if (outcome.To < 0 || outcome.Probability.IsZero)
                    {
                        continue;
                    }

                    Discover(outcome.To, remaining - 1,
                        Next(history, action.Id, Label(model, outcome.To, information), outcome));
                }
            }
        }

        for (var state = 0; state < model.States.Count; state++)
        {
            if (!model.Initial[state].IsZero)
            {
                Discover(state, horizon, Root(Label(model, state, information)));
            }
        }

        KeyValuePair<string, int[]>[] slots = histories.ToArray();
        var policy = new Dictionary<string, int>();
        Rational? best = null;
        long policies = 0;

        void Choose(int slot)
        {
            if (slot < slots.Length)
            {
                foreach (var action in slots[slot].Value)
                {
                    policy[slots[slot].Key] = action;
                    Choose(slot + 1);
                }

                return;
            }

            if (++policies > 2000000)
            {
                throw new InvalidOperationException("Oracle fixture has too many policies.");
            }

            Rational value = Evaluate(model, horizon, information, (history, _) => policy[history], null);
            if (best is null || value > best.Value)
            {
                best = value;
            }
        }

        Choose(0);
        return (best!.Value, policies);
    }

    internal static Rational Replay(ExactDecisionResult result)
    {
        var nodes = new Dictionary<string, DecisionPolicyNode>();
        foreach (DecisionPolicyRoot root in result.Roots)
        {
            nodes.Add(Root(root.Observation), root.Node);
        }

        int Choice(string history, int remaining)
        {
            DecisionPolicyNode node = nodes[history];
            Assert.Equal(remaining, node.Remaining);
            return node.Choice == DecisionChoice.Stop ? -1 : node.ActionId!.Value;
        }

        void Observe(string history, string successor, DecisionOutcome outcome, int label)
        {
            DecisionPolicyNode node = nodes[history];
            Assert.Equal(Rational.One,
                node.Branches.Aggregate(Rational.Zero, (sum, branch) => sum + branch.Probability));
            DecisionPolicyBranch branch = Assert.Single(node.Branches,
                branch => branch.Observation == new DecisionObservation(label, outcome.Signal, outcome.Payout));
            if (outcome.To < 0)
            {
                Assert.Null(branch.Next);
                return;
            }

            Assert.NotNull(branch.Next);
            nodes[successor] = branch.Next;
        }

        return Evaluate(result.Model, result.Horizon, result.Information, Choice, Observe);
    }

    private static Rational Evaluate(ExactDecisionModel model, int horizon, DecisionInformation information,
        Func<string, int, int> choose, Action<string, string, DecisionOutcome, int>? observe)
    {
        Rational total = Rational.Zero;

        void Walk(int state, int remaining, string history, Rational probability, Rational discount, Rational ledger)
        {
            DecisionState source = model.States[state];
            if (remaining == 0)
            {
                total += probability * (ledger + discount * source.HorizonPayout);
                return;
            }

            var choice = choose(history, remaining);
            if (choice == -1)
            {
                Assert.True(source.CanStop);
                total += probability * (ledger + discount * source.StopPayout);
                return;
            }

            DecisionAction action = Assert.Single(source.Actions, action => action.Id == choice);
            Rational nextDiscount = discount * model.Discount;
            foreach (DecisionOutcome outcome in action.Outcomes)
            {
                if (outcome.Probability.IsZero)
                {
                    continue;
                }

                Rational mass = probability * outcome.Probability;
                Rational cash = ledger - discount * action.Wager + nextDiscount * outcome.Payout;
                var label = outcome.To < 0 ? -1 : Label(model, outcome.To, information);
                var successor = Next(history, action.Id, label, outcome);
                observe?.Invoke(history, successor, outcome, label);
                if (outcome.To < 0)
                {
                    total += mass * cash;
                }
                else
                {
                    Walk(outcome.To, remaining - 1, successor, mass, nextDiscount, cash);
                }
            }
        }

        for (var state = 0; state < model.States.Count; state++)
        {
            if (model.Initial[state].IsZero)
            {
                continue;
            }

            Walk(state, horizon, Root(Label(model, state, information)), model.Initial[state], Rational.One,
                -model.EntryCost);
        }

        return total;
    }
}
