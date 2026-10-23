using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

/// <summary>
///     Conservative state invariants: present integer fields in [0, 2^64-1].
///     Every initial value and every possible write must preserve the invariant.
///     Scoped aliases, settlement effects and unknown operations rule a field out.
/// </summary>
internal static class SamplingMaskState
{
    internal static readonly IReadOnlySet<string> None = FrozenSet<string>.Empty;

    internal static IReadOnlySet<string> Infer(GraphConfig config, IReadOnlyList<MeasurementDefinition> measurements)
    {
        var masks = new HashSet<string>(StringComparer.Ordinal);
        foreach ((var key, JsonElement raw) in config.InitialState ?? new Dictionary<string, JsonElement>())
        {
            SamplingCell cell = SamplingCell.FromRaw(InitialStateValues.Materialize(raw));
            if (cell.Readable && IsMask(cell.StoredValue))
            {
                masks.Add(key);
            }
        }

        var excluded = new HashSet<string>(MonetarySettlement.EvidenceKeys, StringComparer.Ordinal)
        {
            "__modified__",
            "__default__"
        };
        var expressions = new List<Expression?>();
        if (config.Expressions is { } shared)
        {
            expressions.AddRange(shared.Values);
        }

        var writes = new List<(string Key, Expression? Value)>();
        foreach (Node node in config.Nodes)
        {
            switch (node)
            {
                case DrawManyNode batch: excluded.Add(batch.OutputKey); break;
                case ModifyStateNode modify:
                    writes.Add((modify.OutputKey ?? "__modified__",
                        modify.ExpressionId is { } id &&
                        config.Expressions?.TryGetValue(id, out Expression? value) == true
                            ? value
                            : null));
                    break;
                case DrawNode draw:
                    if (draw.StateWriteKey is { } key)
                    {
                        excluded.Add(key);
                    }

                    if (draw.BoardStateKey is { } board)
                    {
                        excluded.Add(board);
                    }

                    break;
                case PutStateNode put: excluded.Add(put.StateKey); break;
                case DataNode data: excluded.Add(data.StateKey); break;
                case LoopNode loop:
                    excluded.Add($"__exitReason_{loop.Id}__");
                    excluded.Add($"__iter_{loop.Id}__");
                    excluded.Add($"__wins_{loop.Id}__");
                    expressions.Add(loop.ExitReason);
                    break;
                case BranchNode or GetStateNode or MetricsSinkNode: break;
                default: return None;
            }
        }

        foreach (MeasurementDefinition d in measurements)
        {
            expressions.AddRange([
                d.Value, d.Filter, d.Options?.Group, d.Options?.Pair, d.Options?.Weight, d.Options?.AwardId,
                d.Options?.EntryFilter, d.Options?.ExitFilter, d.Options?.ExitReason
            ]);
        }

        if (!expressions.All(e => SamplingPlan.Variables(e, excluded)))
        {
            return None;
        }

        masks.ExceptWith(excluded);

        // Start with all valid initial values. Removing one field may invalidate
        // copies of it, so iterate to a fixed point. Cycles are valid only when
        // all their initial values and every incoming write are valid masks.
        bool changed;
        do
        {
            changed = false;
            foreach ((var key, Expression? value) in writes)
            {
                if (masks.Contains(key) && !ProducesMask(value, masks))
                {
                    changed |= masks.Remove(key);
                }
            }
        } while (changed);

        return masks.ToFrozenSet(StringComparer.Ordinal);
    }

    private static bool IsMask(ExprValue value) => value.Kind == ExprType.Number && value.NumberDenominator.IsOne
        && value.NumberNumerator >= 0 && value.NumberNumerator <= ulong.MaxValue;

    // Refine an established mask invariant by its maximum occupied bit. Grow
    // from initial values until every incoming write preserves the bound.
    // The finite 0..64-bit lattice terminates even for mutually dependent fields.
    internal static IReadOnlySet<string> InferInline(GraphConfig config, IReadOnlySet<string> masks)
    {
        if (masks.Count == 0)
        {
            return None;
        }

        Dictionary<string, int> initial = masks.ToDictionary(key => key, key => (int)SamplingCell.FromRaw(
                InitialStateValues.Materialize(config.InitialState![key])).StoredValue.NumberNumerator.GetBitLength(),
            StringComparer.Ordinal);
        var widths = new Dictionary<string, int>(initial, StringComparer.Ordinal);
        Dictionary<string, ModifyStateNode[]> writes = config.Nodes.OfType<ModifyStateNode>()
            .Where(node => node.OutputKey is { } key && masks.Contains(key))
            .GroupBy(node => node.OutputKey!, StringComparer.Ordinal).ToDictionary(group => group.Key,
                group => group.ToArray(), StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var key in masks)
            {
                var width = initial[key];
                if (writes.TryGetValue(key, out ModifyStateNode[]? nodes))
                {
                    foreach (ModifyStateNode node in nodes)
                    {
                        width = System.Math.Max(width,
                            Width(
                                node.ExpressionId is { } id &&
                                config.Expressions?.TryGetValue(id, out Expression? value) == true
                                    ? value
                                    : null, widths));
                    }
                }

                if (width <= widths[key])
                {
                    continue;
                }

                widths[key] = width;
                changed = true;
            }
        } while (changed);

        return widths.Where(pair => pair.Value <= 63).Select(pair => pair.Key).ToFrozenSet(StringComparer.Ordinal);
    }

    private static int Width(Expression? expression, IReadOnlyDictionary<string, int> fields,
        string? accumulator = null, int accumulatorWidth = 64, string? item = null, string? index = null)
    {
        switch (expression)
        {
            case ConstantExpr constant:
                try
                {
                    ExprValue value = ExactExpressionEvaluator.Evaluate(constant, new EvalContext());
                    return IsMask(value) ? (int)value.NumberNumerator.GetBitLength() : 64;
                }
                catch (Exception error) when (error is ExpressionEvaluationException or FormatException
                                                  or DivideByZeroException or OverflowException)
                {
                    return 64;
                }
            case FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field:
                var name = field.Path[0];
                if (name == index)
                {
                    return 31;
                }

                if (name == item)
                {
                    return 64;
                }

                if (name == accumulator)
                {
                    return accumulatorWidth;
                }

                return name is not null && fields.TryGetValue(name, out var width) ? width : 64;
            case IfExpr branch:
                return System.Math.Max(Width(branch.ThenExpr, fields, accumulator, accumulatorWidth, item, index),
                    Width(branch.ElseExpr, fields, accumulator, accumulatorWidth, item, index));
            case BitSetExpr { Args.Length: 1, Op: BitSetOp.Count }: return 7;
            case BitSetExpr { Args.Length: 2 } set:
                var left = Width(set.Args[0], fields, accumulator, accumulatorWidth, item, index);
                var right = Width(set.Args[1], fields, accumulator, accumulatorWidth, item, index);
                return set.Op switch
                {
                    BitSetOp.Union => System.Math.Max(left, right),
                    BitSetOp.Intersect => System.Math.Min(left, right),
                    BitSetOp.Except => left,
                    BitSetOp.Shift when set.Args[1] is ConstantExpr { Kind: ConstantKind.Integer } distance
                                        && int.TryParse(distance.Value, NumberStyles.Integer,
                                            CultureInfo.InvariantCulture, out var shift) && shift is >= -63 and <= 63 =>
                        left == 0 ? 0 : System.Math.Clamp(left + shift, 0, 64),
                    BitSetOp.Shift when set.Args[1] is BitSetExpr { Args.Length: 1, Op: BitSetOp.First } first =>
                        left == 0
                            ? 0
                            : System.Math.Min(64,
                                left + System.Math.Max(1,
                                    Width(first.Args[0], fields, accumulator, accumulatorWidth, item, index)) - 1),
                    _ => 64
                };
            case FoldExpr fold:
                var start = Width(fold.Init, fields, accumulator, accumulatorWidth, item, index);
                var bound = start;
                while (true)
                {
                    var next = System.Math.Max(start,
                        Width(fold.Body, fields, fold.AccName, bound, fold.ItemName, fold.IndexName));
                    if (next <= bound)
                    {
                        return bound;
                    }

                    bound = next;
                }
            default: return 64;
        }
    }

    private static bool ProducesMask(Expression? expression, IReadOnlySet<string> fields, string? accumulator = null,
        string? item = null, string? index = null)
    {
        switch (expression)
        {
            case ConstantExpr constant:
                try
                {
                    return IsMask(ExactExpressionEvaluator.Evaluate(constant, new EvalContext()));
                }
                catch (Exception error) when (error is ExpressionEvaluationException or FormatException
                                                  or DivideByZeroException or OverflowException)
                {
                    return false;
                }
            case FieldAccessExpr { Target: null or "state" or "board", Path.Length: 1 } field:
                var name = field.Path[0];
                if (name is null)
                {
                    return false;
                }

                if (name == index)
                {
                    return true;
                }

                if (name == item)
                {
                    return false;
                }

                return name == accumulator || fields.Contains(name);
            case IfExpr branch:
                return ProducesMask(branch.ThenExpr, fields, accumulator, item, index) &&
                       ProducesMask(branch.ElseExpr, fields, accumulator, item, index);
            case BitSetExpr
            {
                Args.Length: 2, Op: BitSetOp.Union or BitSetOp.Intersect or BitSetOp.Except or BitSetOp.Shift
            }:
            case BitSetExpr { Args.Length: 1, Op: BitSetOp.Count }:
                // Errors occur before assignment; every successful result fits.
                return true;
            case FoldExpr fold:
                return ProducesMask(fold.Init, fields, accumulator, item, index)
                       && ProducesMask(fold.Body, fields, fold.AccName, fold.ItemName, fold.IndexName);
            default: return false;
        }
    }
}
