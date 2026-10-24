using System.Globalization;
using System.Text.Json;
using SlotMath.Core.Compiler.Ir;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using SlotMath.Core.Optimization;

namespace SlotMath.Core.Compiler;

internal sealed class FiniteDomain(PrecomputationScope scope, string name, ExprValue[] values)
{
    private readonly int[]? _dense = Dense(values);

    private readonly long _minimum =
        values.All(value => value.IsInlineInteger) ? values.Min(value => value.InlineInteger) : 0;

    private readonly Dictionary<ExprValue, int> _positions = values.Select((value, index) => (value, index))
        .ToDictionary(pair => pair.value, pair => pair.index);

    internal PrecomputationScope Scope { get; } = scope;
    internal string Name { get; } = name;
    internal ExprValue[] Values { get; } = values;
    internal int Stride { get; set; }

    internal long RetainedBytes => 160 + Name.Length * 2L + Values.Length * 112L + (_dense?.Length ?? 0) * 4L +
                                   Values.Sum(value => value.Kind is ExprType.String or ExprType.Symbol
                                       ? 24L + value.StringValue!.Length * 2L
                                       : value.Kind == ExprType.Number && !value.IsInlineInteger
                                           ? 64L + value.NumberNumerator.GetByteCount() +
                                             value.NumberDenominator.GetByteCount()
                                           : 0);

    private static int[]? Dense(ExprValue[] values)
    {
        if (!values.All(value => value.IsInlineInteger))
        {
            return null;
        }

        long first = values.Min(value => value.InlineInteger), last = values.Max(value => value.InlineInteger);
        if (last - first is < 0 or > 4095)
        {
            return null;
        }

        var result = Enumerable.Repeat(-1, (int)(last - first + 1)).ToArray();
        for (var index = 0; index < values.Length; index++)
        {
            result[(int)(values[index].InlineInteger - first)] = index;
        }

        return result;
    }

    internal bool TryOrdinal(ExprValue value, out int ordinal)
    {
        ordinal = -1;
        if (_dense is { } dense)
        {
            if (!value.IsInlineInteger ||
                unchecked((ulong)(value.InlineInteger - _minimum)) >= (ulong)dense.Length)
            {
                return false;
            }

            ordinal = dense[(int)(value.InlineInteger - _minimum)];
            return ordinal >= 0;
        }

        if (value.Kind == ExprType.Number &&
            (value.NumberNumerator.GetBitLength() > PrecomputationAnalysis.NumericBits ||
             value.NumberDenominator.GetBitLength() > PrecomputationAnalysis.NumericBits))
        {
            return false;
        }

        if (value.Kind is ExprType.String or ExprType.Symbol &&
            value.StringValue!.Length > PrecomputationAnalysis.MaxText)
        {
            return false;
        }

        return value.Kind is ExprType.Number or ExprType.Boolean or ExprType.String or ExprType.Symbol &&
               _positions.TryGetValue(value, out ordinal);
    }
}

internal sealed class FiniteTable(FiniteDomain[] domains, ExprValue[] rows, long bytes)
{
    internal FiniteDomain[] Domains { get; } = domains;
    internal long Bytes { get; } = bytes;
    internal int Entries => rows.Length;

    internal bool TryArgument(int input, ExprValue value, out int contribution)
    {
        contribution = 0;
        if (!Domains[input].TryOrdinal(value, out var ordinal))
        {
            return false;
        }

        contribution = ordinal * Domains[input].Stride;
        return true;
    }

    internal ExprValue Result(int row) => rows[row];

    internal bool TryLookup(SamplingFrame frame, int[] slots, out ExprValue result)
    {
        var row = 0;
        for (var input = 0; input < Domains.Length; input++)
        {
            FiniteDomain domain = Domains[input];
            ExprValue value = default;
            var found = domain.Scope == PrecomputationScope.Local
                ? LexicalValues.TryRead(frame.Locals, domain.Name, out value)
                : frame.Cells[slots[input]].Present && frame.Cells[slots[input]].TryCachedValue(out value);
            if (!found || !TryArgument(input, value, out var contribution))
            {
                result = default;
                return false;
            }

            row += contribution;
        }

        result = rows[row];
        return true;
    }
}

internal sealed record PrecomputationPlan(
    FiniteTable? Table,
    string Reason,
    int Entries = 0,
    long Work = 0,
    long Bytes = 0)
{
    internal bool Admitted => Table is not null;
}

internal sealed class FinitePrecomputationCache
{
    private const int MaxPlans = 64;
    private const long MaxRetained = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions CompactJson = new(JsonOptions.Default)
    {
        WriteIndented = false
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, PrecomputationPlan> _plans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FiniteTable> _tables = new(StringComparer.Ordinal);
    private long _bytes;

    internal int BuiltTables
    {
        get
        {
            lock (_gate)
            {
                return _tables.Count;
            }
        }
    }

    internal long RetainedBytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    internal PrecomputationPlan Get(Expression expression, Func<string, ExprValue?> constant)
    {
        PrecomputationHint? hint = expression.Precompute;
        if (hint is null)
        {
            return new PrecomputationPlan(null, "not-requested");
        }

        lock (_gate)
        {
            if (_plans.Count >= MaxPlans)
            {
                return new PrecomputationPlan(null, "cache-plan-limit");
            }

            try
            {
                if (hint.ExpectedUses <= 0 || hint.ExpectedUses > 1_000_000_000_000 ||
                    hint.MaxEntries is < 1 or > 65536 || hint.MaxWork is < 1 or > 8_000_000 ||
                    hint.MaxBytes is < 1 or > 8 * 1024 * 1024 ||
                    hint.Domains is not { Length: > 0 and <= 8 })
                {
                    throw new PrecomputationDeclined("hint-budget");
                }

                Expression body = expression with
                {
                    Precompute = null
                };
                var domains = new List<FiniteDomain>();
                var stateBounds = new Dictionary<string, PrecomputationAnalysis.Bound>(StringComparer.Ordinal);
                var localBounds = new Dictionary<string, PrecomputationAnalysis.Bound>(StringComparer.Ordinal);
                var distinct = new HashSet<(PrecomputationScope, string)>();
                var entries = 1;
                foreach (PrecomputationDomain input in hint.Domains)
                {
                    if (input is null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 128 ||
                        input.Scope is not (PrecomputationScope.State or PrecomputationScope.Local) ||
                        !distinct.Add((input.Scope, input.Name)) ||
                        input.Values is not { Length: > 0 and <= 256 })
                    {
                        throw new PrecomputationDeclined("input-domain");
                    }

                    var values = new ExprValue[input.Values.Length];
                    PrecomputationAnalysis.Bound? bound = null;
                    var unique = new HashSet<ExprValue>();
                    for (var index = 0; index < values.Length; index++)
                    {
                        PrecomputationValue literal = input.Values[index];
                        if (literal is null || literal.Value is null || literal.Value.Length > 1024)
                        {
                            throw new PrecomputationDeclined("domain-literal");
                        }

                        ExprValue value = ExactExpressionEvaluator.Evaluate(literal.Expression, EvalContext.Empty);
                        PrecomputationAnalysis.Bound current = PrecomputationAnalysis.Scalar(value);
                        if (!unique.Add(value))
                        {
                            throw new PrecomputationDeclined("duplicate-domain-value");
                        }

                        bound = bound is null ? current : PrecomputationAnalysis.Join(bound, current);
                        values[index] = value;
                    }

                    if (entries > hint.MaxEntries / values.Length)
                    {
                        throw new PrecomputationDeclined("entry-budget");
                    }

                    entries *= values.Length;
                    domains.Add(new FiniteDomain(input.Scope, input.Name, values));
                    (input.Scope == PrecomputationScope.Local ? localBounds : stateBounds)[input.Name] = bound!;
                }

                var analysis = new PrecomputationAnalysis(constant, stateBounds, localBounds);
                analysis.ChargeResult(analysis.Analyze(body));
                if (analysis.Work <= 0 || analysis.Work > hint.MaxWork / entries)
                {
                    throw new PrecomputationDeclined("work-budget");
                }

                var work = entries * analysis.Work;
                long bodyCost = UniqueCost(ExpressionSimplifier.Simplify(body, analysis.Captured).Expression),
                    lookupCost = 2 + domains.Count * 4;
                if (bodyCost < 2 * lookupCost ||
                    hint.ExpectedUses < (2 * work + bodyCost - lookupCost - 1) / (bodyCost - lookupCost))
                {
                    throw new PrecomputationDeclined("not-amortized");
                }

                var context = new TypeCheckContext
                {
                    StateFields =
                    [
                        .. stateBounds.Select(pair => Descriptor(pair.Key, pair.Value)),
                        .. analysis.Captured.Select(pair => ExpressionShapes.FromJson(pair.Key,
                            JsonSerializer.SerializeToElement(pair.Value.ToStateObject(), JsonOptions.Default)))
                    ],
                    BoardFields = TypeCheckContext.Default.BoardFields
                };
                Dictionary<string, FieldDescriptor> locals = localBounds.ToDictionary(pair => pair.Key,
                    pair => Descriptor(pair.Key, pair.Value),
                    StringComparer.Ordinal);
                ExpressionIr ir = ExpressionIrBuilder.Build(body, context, locals: locals);
                IrEffects effects = ir.Effects;
                var uses = new HashSet<int>();

                void Used(IrRegion region)
                {
                    uses.Add(region.Result.Id);
                    foreach (IrOperation operation in region.Operations)
                    {
                        foreach (IrValue input in operation.Inputs)
                        {
                            uses.Add(input.Id);
                        }

                        foreach (IrRegion child in operation.Regions)
                        {
                            Used(child);
                        }
                    }
                }

                Used(ir.Root);
                Dictionary<string, int> localParameters = locals.Keys.Zip(ir.Root.Parameters)
                    .ToDictionary(pair => pair.First, pair => pair.Second.Id, StringComparer.Ordinal);
                if (domains.Any(domain =>
                        domain.Scope == PrecomputationScope.State
                            ? !effects.Reads.Contains(domain.Name)
                            : !uses.Contains(localParameters[domain.Name])))
                {
                    throw new PrecomputationDeclined("unused-domain");
                }

                const IrEffect forbidden = IrEffect.Unknown | IrEffect.Random | IrEffect.WriteState | IrEffect.Observe |
                                           IrEffect.Cancellation;
                if (!effects.Known || (effects.Flags & forbidden) != 0)
                {
                    throw new PrecomputationDeclined("effects");
                }

                var identity = JsonSerializer.Serialize(new
                {
                    Body = body,
                    hint.Domains,
                    Parameters = analysis.Captured.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
                    {
                        pair.Key,
                        Value = Fingerprint(pair.Value)
                    })
                }, CompactJson);
                if (identity.Length > 65536)
                {
                    throw new PrecomputationDeclined("identity-size");
                }

                var key = JsonSerializer.Serialize(new
                {
                    identity,
                    hint.ExpectedUses,
                    hint.MaxEntries,
                    hint.MaxWork,
                    hint.MaxBytes
                }, CompactJson);
                if (_plans.TryGetValue(key, out PrecomputationPlan? existing))
                {
                    return existing;
                }

                if (_tables.TryGetValue(identity, out FiniteTable? shared) && shared.Bytes <= hint.MaxBytes)
                {
                    var metadataBytes = 128 + key.Length * 2L;
                    if (_bytes + metadataBytes > MaxRetained)
                    {
                        throw new PrecomputationDeclined("cache-memory-limit");
                    }

                    _bytes += metadataBytes;
                    return _plans[key] = new PrecomputationPlan(shared, "shared", shared.Entries, work, shared.Bytes);
                }

                var bytes = 256 + identity.Length * 2L + key.Length * 2L + entries * 16L +
                            domains.Sum(domain => domain.RetainedBytes);
                var maximum = System.Math.Min(hint.MaxBytes, MaxRetained - _bytes);
                if (bytes + analysis.CapturedBytes > maximum)
                {
                    throw new PrecomputationDeclined("memory-budget");
                }

                FiniteDomain[] inputs = domains.ToArray();
                var stride = 1;
                for (var index = inputs.Length - 1; index >= 0; index--)
                {
                    inputs[index].Stride = stride;
                    stride *= inputs[index].Values.Length;
                }

                var rows = new ExprValue[entries];
                for (var row = 0; row < entries; row++)
                {
                    Dictionary<string, object?> state = analysis.Captured.ToDictionary(pair => pair.Key,
                        pair => (object?)pair.Value,
                        StringComparer.Ordinal);
                    LexicalBinding? bindings = null;
                    foreach (FiniteDomain input in inputs)
                    {
                        ExprValue value = input.Values[row / input.Stride % input.Values.Length];
                        if (input.Scope == PrecomputationScope.Local)
                        {
                            bindings = new LexicalBinding(input.Name, value, bindings);
                        }
                        else
                        {
                            state[input.Name] = value;
                        }
                    }

                    ExprValue result = ExactExpressionEvaluator.Evaluate(body, new EvalContext
                    {
                        State = state,
                        Locals = bindings
                    });
                    var nodes = 0;
                    (ExprValue Value, PrecomputationAnalysis.Bound Bound, long Bytes) snapshot =
                        PrecomputationAnalysis.Snapshot(result, ref nodes);
                    bytes += snapshot.Bytes;
                    if (bytes + analysis.CapturedBytes > maximum)
                    {
                        throw new PrecomputationDeclined("result-memory-budget");
                    }

                    rows[row] = snapshot.Value;
                }

                var table = new FiniteTable(inputs, rows, bytes);
                _tables[identity] = table;
                _bytes += bytes;
                return _plans[key] = new PrecomputationPlan(table, "admitted", entries, work, bytes);
            }
            catch (PrecomputationDeclined declined)
            {
                return new PrecomputationPlan(null, declined.Message);
            }
            catch (Exception exception) when (exception is ExpressionEvaluationException or ArgumentException
                                                  or FormatException or OverflowException or DivideByZeroException
                                                  or InvalidOperationException or JsonException)
            {
                return new PrecomputationPlan(null, "domain-error:" + exception.GetType().Name);
            }
        }
    }

    private static FieldDescriptor Descriptor(string name, PrecomputationAnalysis.Bound value) => new()
    {
        Name = name,
        Type = value.Kind,
        ArrayItem = value.Element is { } item ? Descriptor("item", item) : null,
        RecordFields = value.Fields?.Select(pair => Descriptor(pair.Key, pair.Value)).ToArray() ?? []
    };

    private static object Fingerprint(ExprValue value) => value.Kind switch
    {
        ExprType.Number => new object[]
        {
            value.Kind, value.NumberNumerator.ToString(CultureInfo.InvariantCulture),
            value.NumberDenominator.ToString(CultureInfo.InvariantCulture)
        },
        ExprType.Boolean => new object[] { value.Kind, value.BoolValue },
        ExprType.String or ExprType.Symbol => new object[] { value.Kind, value.StringValue! },
        ExprType.Array => new object[] { value.Kind, value.ArrayValue!.Select(Fingerprint).ToArray() },
        _ => new object[]
        {
            value.Kind, value.RecordValue!.Select(pair => new[] { pair.Key, Fingerprint(pair.Value) }).ToArray()
        }
    };

    private static int UniqueCost(Expression expression)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var work = 0;

        void Visit(Expression value, int depth)
        {
            if (depth > 128 || ++work > 10000)
            {
                throw new PrecomputationDeclined("cost-analysis");
            }

            nodes.Add(JsonSerializer.Serialize(value, CompactJson));
            foreach (Expression child in CollectionInputs.Children(value))
            {
                Visit(child, depth + 1);
            }
        }

        Visit(expression, 0);
        return nodes.Count;
    }
}
