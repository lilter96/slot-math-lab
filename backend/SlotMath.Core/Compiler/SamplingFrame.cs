using System.Collections;
using System.Globalization;
using System.Numerics;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Compiler;

// A frame belongs to one sampler, never to a shared program. Typed arrays are
// immutable expression results; materialization is deferred until state export.
internal struct SamplingCell
{
    public bool Present;
    public bool HasRaw;
    public object? Raw;
    public ExprValue Value;

    public static SamplingCell Typed(ExprValue value) => new() { Present = true, Value = NormalizeStored(value) };
    public static SamplingCell FromRaw(object? raw) => new() { Present = true, HasRaw = true, Raw = raw, Value = Convert(raw) };
    public readonly object? Export() => HasRaw ? Raw : Value.ToStateObject();

    private static ExprValue Convert(object? raw) => raw switch
    {
        BigInteger n => ExprValue.Number(n), int n => ExprValue.Number(n), long n => ExprValue.Number(n),
        bool b => ExprValue.Bool(b), string s => ExprValue.String(s), ExprValue v => v,
        IEnumerable a => ExprValue.Array(a.Cast<object?>().Select(Convert).ToArray()),
        _ => ExprValue.Number(0),
    };

    // ToStateObject turns symbols into strings, including inside arrays.
    private static ExprValue NormalizeStored(ExprValue value)
    {
        if (!value.ContainsSymbols) return value;
        if (value.Kind == ExprType.Symbol) return ExprValue.String(value.StringValue!);
        if (value.Kind != ExprType.Array) return value;
        ExprValue[]? copy = null;
        for (var i = 0; i < value.ArrayValue!.Count; i++)
        {
            var item = value.ArrayValue[i]; var normalized = NormalizeStored(item);
            if (!item.Equals(normalized))
            {
                copy ??= value.ArrayValue.ToArray(); copy[i] = normalized;
            }
        }
        return copy is null ? value : ExprValue.Array(copy);
    }

    public readonly ExprValue FilterValue => HasRaw
        ? Raw is string or BigInteger or int or long or bool or ExprValue ? Value : ExprValue.Number(0)
        : Value.Kind == ExprType.Array ? ExprValue.Number(0) : Value;

    public readonly int RequireArrayCount(string key)
    {
        if (!Present) throw new ExpressionEvaluationException("EVAL_MISSING_STATE", $"State array '{key}' is absent.", key);
        if (Value.Kind != ExprType.Array || HasRaw && (Raw is string or IDictionary || Raw is not IEnumerable && Raw is not ExprValue { Kind: ExprType.Array }))
            throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"State field '{key}' must be an array.", key);
        return Value.ArrayValue!.Count;
    }
    public readonly SamplingCell Item(int index)
    {
        if (!HasRaw) return Typed(Value.ArrayValue![index]);
        return Raw switch
        {
            ExprValue { Kind: ExprType.Array } typed => FromRaw(typed.ArrayValue![index]),
            object?[] a => FromRaw(a[index]),
            IList a => FromRaw(a[index]),
            _ => FromRaw(((IEnumerable)Raw!).Cast<object?>().ElementAt(index)),
        };
    }

    public readonly ExprValue IndexedField(int index, string key)
    {
        var length = RequireArrayCount(key);
        if (index < 0 || index >= length)
            throw new ExpressionEvaluationException(EvalErrorCodes.IndexOutOfRange,
                $"Index {index} is out of range [0, {length}) for state array '{key}' (D1).", $"{key}[{index}]");
        return Item(index).Value;
    }
}

internal sealed class SamplingFrame
{
    public SlotMath.Core.Measurements.SettlementContext? Settlement { get; set; }
    public SlotMath.Core.Math.LoopTerminationEvidence? LoopEvidence;
    public BigInteger RawPayout { get; set; }

    public readonly SamplingCell[] Cells;
    private readonly SamplingCell[] _initial;
    private readonly IReadOnlyDictionary<string, int> _layout;
    private readonly Dictionary<string, object?> _extra;
    public SlotMath.Core.Measurements.MeasurementCollector? Measurements;
    public CancellationToken CancellationToken;
    private int _operations;

    public SamplingFrame(IReadOnlyDictionary<string, int> layout, SamplingCell[] defaults, Dictionary<string, object?> initial)
    {
        _layout = layout;
        _initial = (SamplingCell[])defaults.Clone();
        _extra = new();
        foreach (var (key, value) in initial)
            if (layout.TryGetValue(key, out var index)) _initial[index] = SamplingCell.FromRaw(value);
            else _extra[key] = value;
        Cells = new SamplingCell[defaults.Length];
    }

    public void Reset(CancellationToken token, bool[]? retained = null)
    {
        token.ThrowIfCancellationRequested();
        if (retained is null) Array.Copy(_initial, Cells, Cells.Length);
        else for (var i = 0; i < Cells.Length; i++) if (!retained[i]) Cells[i] = _initial[i];
        CancellationToken = token;
        Settlement = null;
        _operations = 0;
    }

    public void CheckCancellation()
    {
        if ((++_operations & 255) == 0) CancellationToken.ThrowIfCancellationRequested();
    }

    public ExprValue Read(int index, string location)
    {
        if (!Cells[index].Present)
            throw new ExpressionEvaluationException("EVAL_MISSING_STATE", $"State field '{location}' is absent.", location);
        return Cells[index].Value;
    }

    public Dictionary<string, object?> Export()
    {
        var state = new Dictionary<string, object?>(_extra);
        foreach (var (key, index) in _layout)
            if (Cells[index].Present) state[key] = Snapshot(Cells[index].Export());
        return state;
    }

    // Cached defaults must never be exposed as mutable arrays/records.
    private static object? Snapshot(object? value) => value switch
    {
        IDictionary<string, object?> d => d.ToDictionary(p => p.Key, p => Snapshot(p.Value)),
        string[] a => a.ToArray(),
        object?[] a => a.Select(Snapshot).ToArray(),
        Array a => a.Clone(),
        List<object?> a => a.Select(Snapshot).ToList(),
        _ => value,
    };
}
