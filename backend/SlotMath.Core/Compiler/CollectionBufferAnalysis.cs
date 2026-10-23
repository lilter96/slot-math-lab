using System.Collections.Frozen;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

internal sealed record BufferLifetime(int Uses, bool Unique, bool Borrowed, IReadOnlySet<int> Origins);

/// <summary>
///     Sparse SSA use/alias facts for top-level array containers. Nested elements
///     remain borrowed; inferred array shape is never a proof of exclusive storage.
/// </summary>
internal sealed class CollectionBufferAnalysis
{
    private readonly IReadOnlyDictionary<int, IrOperation> _definitions;
    private readonly ExpressionIr _ir;

    private CollectionBufferAnalysis(ExpressionIr ir, IReadOnlyDictionary<int, IrOperation> definitions,
        IReadOnlyDictionary<int, BufferLifetime> values, bool limited)
    {
        _ir = ir;
        _definitions = definitions;
        Values = values;
        BudgetReached = limited;
    }

    internal IReadOnlyDictionary<int, BufferLifetime> Values { get; }
    internal bool BudgetReached { get; }

    internal static CollectionBufferAnalysis Build(ExpressionIr ir, int maxWork = 100000)
    {
        var definitions = new Dictionary<int, IrOperation>();
        var uses = new Dictionary<int, int>();
        var values = new Dictionary<int, BufferLifetime>();
        var work = 0;
        var limited = false;

        void Use(IrValue value)
        {
            uses[value.Id] = uses.GetValueOrDefault(value.Id) + 1;
        }

        void Visit(IrRegion region)
        {
            if (limited || ++work > maxWork)
            {
                limited = true;
                return;
            }

            Use(region.Result);
            foreach (IrOperation operation in region.Operations)
            {
                if (++work > maxWork)
                {
                    limited = true;
                    return;
                }

                definitions[operation.Result.Id] = operation;
                // Let inputs define bindings; actual projections account for uses.
                if (operation.Code != IrOp.Let)
                {
                    foreach (IrValue input in operation.Inputs)
                    {
                        if (++work > maxWork)
                        {
                            limited = true;
                            return;
                        }

                        Use(input);
                    }
                }

                foreach (IrRegion child in operation.Regions)
                {
                    Visit(child);
                }
            }
        }

        Visit(ir.Root);
        var allowed = ir.Effects.Known && (ir.Effects.Flags &
                                           (IrEffect.Unknown | IrEffect.WriteState | IrEffect.Observe |
                                            IrEffect.Random)) == 0;
        foreach (IrOperation operation in definitions.Values.OrderBy(operation => operation.Id))
        {
            if (limited || ++work > maxWork)
            {
                limited = true;
                values.Clear();
                break;
            }

            var count = uses.GetValueOrDefault(operation.Result.Id);
            var origins = new HashSet<int>();
            var borrowed = true;
            var unique = false;
            if (allowed && operation.Code is IrOp.Map or IrOp.Filter)
            {
                origins.Add(operation.Result.Id);
                borrowed = false;
                unique = count == 1;
            }
            else
            {
                IrValue[] aliases = operation.Code switch
                {
                    IrOp.Let => operation.Regions.Select(region => region.Result).ToArray(),
                    IrOp.If => operation.Regions.Select(region => region.Result).ToArray(),
                    IrOp.Project when operation.Path is { Count: 0 } => operation.Inputs.ToArray(),
                    _ => []
                };
                if (allowed && aliases.Length > 0)
                {
                    borrowed = false;
                    unique = count == 1;
                    foreach (IrValue alias in aliases)
                    {
                        if (++work > maxWork)
                        {
                            limited = true;
                            break;
                        }

                        if (!values.TryGetValue(alias.Id, out BufferLifetime? fact))
                        {
                            borrowed = true;
                            unique = false;
                            continue;
                        }

                        work += fact.Origins.Count;
                        origins.UnionWith(fact.Origins);
                        borrowed |= fact.Borrowed;
                        unique &= fact.Unique;
                    }
                }
            }

            if (limited || work > maxWork)
            {
                limited = true;
                values.Clear();
                break;
            }

            values[operation.Result.Id] = new BufferLifetime(count, unique && !borrowed, borrowed,
                origins.ToFrozenSet());
        }

        return new CollectionBufferAnalysis(ir, definitions.ToFrozenDictionary(), values.ToFrozenDictionary(), limited);
    }

    internal bool CanReuseSource(Expression consumer)
    {
        return !BudgetReached && _ir.Collections.TryGetValue(consumer, out IReadOnlyList<int>? results) &&
               results.Count != 0 && results.All(result =>
                   _definitions.TryGetValue(result, out IrOperation? operation) &&
                   operation.Code is IrOp.Map or IrOp.Filter && operation.Inputs.Count == 1 &&
                   Values.TryGetValue(operation.Inputs[0].Id, out BufferLifetime? source) && source.Unique);
    }

    internal bool TryConstantProducer(MapExpr consumer, out ExprValue value)
    {
        value = default;
        if (!CanReuseSource(consumer) || consumer.Source is not MapExpr producer ||
            !_ir.Collections.TryGetValue(producer, out IReadOnlyList<int>? results) || results.Count == 0)
        {
            return false;
        }

        ExprValue? constant = null;
        foreach (var result in results)
        {
            if (!_definitions.TryGetValue(result, out IrOperation? operation) || operation.Regions.Count != 1)
            {
                return false;
            }

            IrRegion body = operation.Regions[0];
            if (!body.Effects.CanSpeculate || body.Effects.Reads.Count != 0 || body.Effects.Bindings.Count != 0 ||
                body.Result.Constant is not { } candidate || candidate.Kind is not
                    (ExprType.Number or ExprType.Boolean or ExprType.String) ||
                (candidate.Kind == ExprType.Number && (candidate.NumberNumerator.GetBitLength() > 4096 ||
                                                       candidate.NumberDenominator.GetBitLength() > 4096)) ||
                (candidate.Kind == ExprType.String && candidate.StringValue!.Length > 4096) ||
                (constant is { } previous && !previous.Equals(candidate)))
            {
                return false;
            }

            constant = candidate;
        }

        value = constant!.Value;
        return true;
    }
}
