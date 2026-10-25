using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Optimization;

internal sealed record StatePropagationChange(string NodeId, string Slot, string ExpressionId, int ReplacedFacts);

internal sealed record StatePropagationResult(
    GraphConfig Config,
    IReadOnlyList<StatePropagationChange> Changes,
    int Work,
    bool BudgetReached);

/// <summary>
///     Bounded interprocedural conditional propagation over continuation memory SSA.
///     Missing facts are overdefined, null environments are unreachable. No authored writes,
///     nodes, observation points or RNG operations are deleted.
/// </summary>
internal sealed class StateConstantPropagation
{
    private readonly GraphConfig _config;
    private readonly Dictionary<ContinuationKey, HashSet<ContinuationKey>> _dependents = [];
    private readonly Dictionary<string, Dictionary<string, ExprValue>> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExprValue> _initial;
    private readonly Dictionary<ContinuationKey, Dictionary<string, ExprValue>> _inputs = [];
    private readonly CompilerIr _ir;
    private readonly int _limit;
    private readonly Dictionary<string, Node> _nodes;
    private readonly Dictionary<ContinuationKey, Dictionary<string, ExprValue>> _outputs = [];
    private readonly Queue<ContinuationKey> _pending = [];
    private readonly HashSet<ContinuationKey> _queued = [];
    private readonly ContinuationKey _root = new("", "");
    private bool _limited;
    private int _work;

    private StateConstantPropagation(GraphConfig config, CompilerIr ir, IReadOnlyDictionary<string, ExprValue> initial,
        int limit)
    {
        _config = config;
        _ir = ir;
        _limit = limit;
        _initial = initial.Where(pair => Scalar(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _nodes = config.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
    }

    internal static StatePropagationResult Run(GraphConfig config, CompilerIr ir,
        IReadOnlyDictionary<string, ExprValue> initial, int maxWork = 200000)
    {
        var pass = new StateConstantPropagation(config, ir, initial, maxWork);
        return pass.Run();
    }

    private static bool Scalar(ExprValue value) => value.Kind switch
    {
        ExprType.Number => value.NumberNumerator.GetBitLength() <= 4096 &&
                           value.NumberDenominator.GetBitLength() <= 4096,
        ExprType.Boolean => true,
        ExprType.String => value.StringValue is { Length: <= 4096 },
        _ => false
    };

    private bool Tick(int count = 1)
    {
        _work += count;
        if (_work <= _limit)
        {
            return true;
        }

        _limited = true;
        return false;
    }

    private static Dictionary<string, ExprValue> Copy(Dictionary<string, ExprValue> source) =>
        new(source, StringComparer.Ordinal);

    private static bool Equal(Dictionary<string, ExprValue> a, Dictionary<string, ExprValue> b) => a.Count == b.Count &&
        a.All(pair => b.TryGetValue(pair.Key, out ExprValue value) && value.Equals(pair.Value));

    private static Dictionary<string, ExprValue>? Join(Dictionary<string, ExprValue>? a,
        Dictionary<string, ExprValue>? b)
    {
        if (a is null)
        {
            return b is null ? null : Copy(b);
        }

        if (b is null)
        {
            return Copy(a);
        }

        return a.Where(pair => b.TryGetValue(pair.Key, out ExprValue value) && value.Equals(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private void Enqueue(ContinuationKey key)
    {
        if (_queued.Add(key))
        {
            _pending.Enqueue(key);
        }
    }

    private bool Value(string id, Dictionary<string, ExprValue> facts, out ExprValue value) =>
        _config.Expressions is not null && _config.Expressions.TryGetValue(id, out Expression? expression)
            ? Value(expression, new IrExpressionKey(IrRootKind.Named, id, ""), facts, out value)
            : Fail(out value);

    private static bool Fail(out ExprValue value)
    {
        value = default;
        return false;
    }

    private bool Value(Expression expression, IrExpressionKey key, Dictionary<string, ExprValue> facts,
        out ExprValue value)
    {
        value = default;
        if (!_ir.Expressions.TryGetValue(key, out ExpressionIr? ir) || !ir.Effects.Known
                                                                    || (ir.Effects.Flags & (IrEffect.Cancellation |
                                                                        IrEffect.Random | IrEffect.WriteState |
                                                                        IrEffect.Observe)) != 0)
        {
            return false;
        }

        var cost = ExpressionCost.Compute(expression);
        if (cost > 10000 || !Tick((int)cost))
        {
            return false;
        }

        try
        {
            value = ExactExpressionEvaluator.Evaluate(expression, new EvalContext
            {
                State = facts.ToDictionary(pair => pair.Key, pair => pair.Value.ToStateObject(), StringComparer.Ordinal)
            });
            return Scalar(value);
        }
        catch (Exception error) when (error is ExpressionEvaluationException or FormatException or OverflowException
                                          or DivideByZeroException)
        {
            return false; // A failing eager operation stays at its original runtime point.
        }
    }

    private Dictionary<string, ExprValue>? Region(MemoryRegion region, Dictionary<string, ExprValue> input,
        ContinuationKey caller)
    {
        Dictionary<string, ExprValue>? state = Copy(input);
        foreach (MemoryStep step in region.Steps)
        {
            if (state is null || !Tick())
            {
                return null;
            }

            if (step.Phase == "node-checkpoint" && _nodes.ContainsKey(step.Owner))
            {
                _entries[step.Owner] = Join(_entries.GetValueOrDefault(step.Owner), state)!;
            }

            switch (step.Kind)
            {
                case MemoryStepKind.Call:
                {
                    MemoryCall call = step.Call!;
                    Dictionary<string, ExprValue>? before = _inputs.GetValueOrDefault(call.Target);
                    Dictionary<string, ExprValue>? joined = Join(before, state)!;
                    if (before is null || !Equal(before, joined))
                    {
                        _inputs[call.Target] = joined;
                        Enqueue(call.Target);
                    }

                    if (!_dependents.TryGetValue(call.Target, out HashSet<ContinuationKey>? users))
                    {
                        _dependents[call.Target] = users = [];
                    }

                    users.Add(caller);
                    if (!_outputs.TryGetValue(call.Target, out Dictionary<string, ExprValue>? output))
                    {
                        return null;
                    }

                    IrEffects effects = _ir.Memory.Functions[call.Target].Effects;
                    if (!effects.Known)
                    {
                        state.Clear();
                    }
                    else
                    {
                        foreach (var field in effects.Writes)
                        {
                            state.Remove(field);
                            if (output.TryGetValue(field, out ExprValue value))
                            {
                                state[field] = value;
                            }
                        }
                    }

                    break;
                }
                case MemoryStepKind.Write:
                    Write(step, state);
                    break;
                case MemoryStepKind.Branch:
                {
                    if (step.Phase == "branch" && _nodes[step.Owner] is BranchNode branch)
                    {
                        if (branch.ConditionId is { } id && Value(id, state, out ExprValue condition) &&
                            condition.Kind == ExprType.Boolean)
                        {
                            state = Region(step.Regions[condition.BoolValue ? 0 : 1], state, caller);
                        }
                        else
                        {
                            state = Join(Region(step.Regions[0], state, caller),
                                Region(step.Regions[1], state, caller));
                        }
                    }
                    else
                    {
                        Dictionary<string, ExprValue> before = state;
                        foreach (MemoryRegion child in step.Regions)
                        {
                            state = Join(state, Region(child, before, caller));
                        }
                    }

                    break;
                }
                case MemoryStepKind.Loop:
                {
                    var loop = (LoopNode)_nodes[step.Owner];
                    var zero = loop.MaxIterations <= 0 || (loop.StopConditionId is { } id &&
                                                           Value(id, state, out ExprValue condition) &&
                                                           condition.Kind == ExprType.Boolean &&
                                                           condition.BoolValue);
                    if (!zero)
                    {
                        // Widen loop-carried fields before evaluating the body. The user
                        // may reset the service counter, so no trip-count claim is made.
                        if (!step.Effects.Known)
                        {
                            state.Clear();
                        }
                        else
                        {
                            foreach (var field in step.Effects.Writes)
                            {
                                state.Remove(field);
                            }
                        }

                        Region(step.Regions[1], state, caller);
                    }

                    state = Region(step.Regions[2], state, caller);
                    break;
                }
                case MemoryStepKind.Observe:
                {
                    Dictionary<string, ExprValue> before = state;
                    foreach (MemoryRegion child in step.Regions)
                    {
                        state = Join(state, Region(child, before, caller));
                    }

                    break;
                }
                case MemoryStepKind.Read:
                    if (!step.Effects.Known)
                    {
                        state.Clear();
                    }

                    break;
            }
        }

        return state;
    }

    private void Write(MemoryStep step, Dictionary<string, ExprValue> state)
    {
        MemoryVersion version = _ir.Memory.Versions[step.Result];
        if (version.UnknownWrites)
        {
            state.Clear();
            return;
        }

        ExprValue value = default;
        string? output = null;
        var known = false;
        if (step.Phase == "node-commit" && _nodes[step.Owner] is ModifyStateNode modify &&
            modify.ExpressionId is { } expression)
        {
            output = modify.OutputKey ?? "__modified__";
            known = Value(expression, state, out value);
            if (known && modify.OutputKey is null)
            {
                if (value.Kind != ExprType.Number)
                {
                    known = false;
                }
                else
                {
                    value = ExprValue.Number(value.AsInteger());
                }
            }
        }

        foreach (var field in version.Writes)
        {
            state.Remove(field);
        }

        if (known && output is not null)
        {
            state[output] = value;
        }

        // Service counters retain raw Int32 storage in the sampler. Do not turn
        // that representation-sensitive state into a general scalar fact.
    }

    private StatePropagationResult Run()
    {
        _inputs[_root] = Copy(_initial);
        Enqueue(_root);
        while (_pending.TryDequeue(out ContinuationKey key) && !_limited)
        {
            _queued.Remove(key);
            MemoryRegion body = key == _root ? _ir.Memory.Root : _ir.Memory.Functions[key].Body;
            Dictionary<string, ExprValue>? result = Region(body, _inputs[key], key);
            if (result is null)
            {
                continue;
            }

            Dictionary<string, ExprValue>? previous = _outputs.GetValueOrDefault(key);
            Dictionary<string, ExprValue>? merged = Join(previous, result)!;
            if (previous is not null && Equal(previous, merged))
            {
                continue;
            }

            _outputs[key] = merged;
            if (_dependents.TryGetValue(key, out HashSet<ContinuationKey>? callers))
            {
                foreach (ContinuationKey caller in callers)
                {
                    Enqueue(caller);
                }
            }
        }

        if (_limited)
        {
            return new StatePropagationResult(_config, [], _work, true);
        }

        return Rewrite();
    }

    private StatePropagationResult Rewrite()
    {
        var expressions = new Dictionary<string, Expression>(_config.Expressions ?? [], StringComparer.Ordinal);
        var changes = new List<StatePropagationChange>();
        var serial = 0;

        string? Specialized(string? id, string node, string slot, Dictionary<string, ExprValue> facts)
        {
            if (id is null || !expressions.TryGetValue(id, out Expression? expression))
            {
                return id;
            }

            if (_limited || !Tick())
            {
                return id;
            }

            ExpressionSimplification simplified =
                ExpressionSimplifier.Simplify(expression, facts, System.Math.Min(50000, _limit - _work));
            if (!Tick(simplified.Visited) || simplified.BudgetReached)
            {
                _limited = true;
                return id;
            }

            if (simplified.Rules.Count == 0)
            {
                return id;
            }

            string key;
            do
            {
                key = "__sccp_" + serial++;
            } while (expressions.ContainsKey(key));

            expressions[key] = simplified.Expression;
            changes.Add(new StatePropagationChange(node, slot, key, simplified.Rules.Values.Sum()));
            return key;
        }

        Node[] nodes = _config.Nodes.Select(node =>
        {
            if (!_entries.TryGetValue(node.Id, out Dictionary<string, ExprValue>? facts))
            {
                return node;
            }

            return node switch
            {
                ModifyStateNode e => e with
                {
                    ExpressionId = Specialized(e.ExpressionId, e.Id, "value", facts)
                },
                BranchNode e => e with
                {
                    ConditionId = Specialized(e.ConditionId, e.Id, "condition", facts)
                },
                DrawNode e => e with
                {
                    WeightExpressionId = Specialized(e.WeightExpressionId, e.Id, "weight", facts),
                    WeightVectorExpressionId = Specialized(e.WeightVectorExpressionId, e.Id, "weights", facts),
                    DistributionIndexExpressionId = Specialized(e.DistributionIndexExpressionId, e.Id, "family", facts)
                },
                DrawManyNode e => e with
                {
                    CountExpressionId = Specialized(e.CountExpressionId, e.Id, "count", facts)
                },
                _ => node // Loop conditions use header facts, not entry facts; do not specialize them here.
            };
        }).ToArray();
        return _limited
            ? new StatePropagationResult(_config, [], _work, true)
            : new StatePropagationResult(_config with
            {
                Nodes = nodes,
                Expressions = expressions
            }, changes.AsReadOnly(), _work, false);
    }
}
