using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Core.Compiler;

// A frame belongs to one sampler, never to a shared program. Typed arrays are
// immutable expression results; materialization is deferred until state export.
internal struct SamplingCell
{
    public bool Present;
    public bool HasRaw;
    public object? Raw;
    private ExprValue _value;

    private bool _deferred;

    // Present, converted and non-null: a read needs no further checks.
    public readonly ExprValue Value => _deferred ? StateValues.Convert(Raw) : _value;
    internal bool Readable { get; private set; }

    internal readonly ExprValue StoredValue => _value;

    internal readonly ulong StoredMask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value.MaskUnchecked;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SamplingCell Typed(ExprValue value)
    {
        if (value.ContainsSymbols)
        {
            value = NormalizeStored(value);
        }

        return new SamplingCell
        {
            Present = true,
            _value = value,
            Readable = value.Kind != ExprType.Null
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetTyped(ExprValue value)
    {
        if (value.ContainsSymbols)
        {
            value = NormalizeStored(value);
        }

        Present = true;
        HasRaw = false;
        Raw = null;
        _deferred = false;
        _value = value;
        Readable = value.Kind != ExprType.Null;
    }

    private static readonly object[] SmallIntegers = Enumerable.Range(0, 256).Select(value => (object)value).ToArray();

    // Preserve raw Int32 counter semantics while reusing immutable boxes.
    public static SamplingCell FromInt(int value) => new()
    {
        Present = true,
        HasRaw = true,
        Raw = (uint)value < SmallIntegers.Length ? SmallIntegers[value] : value,
        _value = ExprValue.Number(value),
        Readable = true
    };

    public static SamplingCell FromRaw(object? raw)
    {
        // Unused state and untaken conditional branches must not evaluate an
        // unsupported value during frame construction or binding.
        var cell = new SamplingCell
        {
            Present = true,
            HasRaw = true,
            Raw = raw
        };
        try
        {
            cell._value = StateValues.Convert(raw);
            cell.Readable = cell._value.Kind != ExprType.Null;
        }
        catch (ExpressionEvaluationException)
        {
            cell._deferred = true;
        }

        return cell;
    }

    public readonly bool TryCachedValue(out ExprValue value)
    {
        value = _value;
        return !_deferred;
    }

    public readonly ExprValue ReadValue(string location) => _deferred ? StateValues.Convert(Raw, location) : _value;

    public readonly bool IsRecord => _value.Kind == ExprType.Record ||
                                     (HasRaw && Raw is IDictionary<string, object?>
                                         or IReadOnlyDictionary<string, object?>);

    private static readonly object BoxedZero = BigInteger.Zero;

    public readonly object? Export()
    {
        if (HasRaw)
        {
            return Raw;
        }

        ExprValue value = Value;
        return value.IsInlineInteger && value.InlineInteger == 0 ? BoxedZero : value.ToStateObject();
    }

    // ToStateObject turns symbols into strings, including inside arrays.
    private static ExprValue NormalizeStored(ExprValue value)
    {
        if (!value.ContainsSymbols)
        {
            return value;
        }

        if (value.Kind == ExprType.Symbol)
        {
            return ExprValue.String(value.StringValue!);
        }

        if (value.Kind == ExprType.Record)
        {
            return ExprValue.Record(value.RecordValue!.ToDictionary(p => p.Key, p => NormalizeStored(p.Value),
                StringComparer.Ordinal));
        }

        if (value.Kind != ExprType.Array)
        {
            return value;
        }

        ExprValue[]? copy = null;
        for (var i = 0; i < value.ArrayValue!.Count; i++)
        {
            ExprValue item = value.ArrayValue[i];
            ExprValue normalized = NormalizeStored(item);
            if (!item.Equals(normalized))
            {
                copy ??= value.ArrayValue.ToArray();
                copy[i] = normalized;
            }
        }

        return copy is null ? value : ExprValue.Array(copy);
    }

    public readonly ExprValue FilterValue => Value;

    public readonly int RequireArrayCount(string key, string? location = null)
    {
        location ??= key;
        if (!Present)
        {
            throw new ExpressionEvaluationException("EVAL_MISSING_STATE", $"State array '{key}' is absent.", key);
        }

        if (HasRaw && Raw is null or ExprValue { Kind: ExprType.Null })
        {
            StateValues.RequireReadable(ExprValue.Null, location);
        }

        if (HasRaw)
        {
            if (Raw is ExprValue { Kind: ExprType.Array } typed)
            {
                return typed.ArrayValue!.Count;
            }

            if (Raw is IList list)
            {
                return list.Count;
            }

            if (Raw is IEnumerable enumerable &&
                Raw is not (string or IDictionary or IReadOnlyDictionary<string, object?>))
            {
                return enumerable.Cast<object?>().Count();
            }
        }
        else if (_value.Kind == ExprType.Array)
        {
            return _value.ArrayValue!.Count;
        }

        throw new ExpressionEvaluationException("EVAL_TYPE_ERROR", $"State field '{key}' must be an array.", location);
    }

    public readonly SamplingCell Item(int index)
    {
        if (!HasRaw)
        {
            return Typed(Value.ArrayValue![index]);
        }

        return Raw switch
        {
            ExprValue { Kind: ExprType.Array } typed => FromRaw(typed.ArrayValue![index]),
            object?[] a => FromRaw(a[index]),
            IList a => FromRaw(a[index]),
            _ => FromRaw(((IEnumerable)Raw!).Cast<object?>().ElementAt(index))
        };
    }

    public readonly ExprValue IndexedField(int index, string key, string? location = null)
    {
        location ??= $"{key}.{index}";
        var length = RequireArrayCount(key, location);
        if (index < 0 || index >= length)
        {
            throw StateValues.IndexError(index, length, location);
        }

        return StateValues.RequireReadable(Item(index).ReadValue(location), location);
    }
}

internal sealed class SamplingFrame
{
    public readonly SamplingCell[] Cells;
    private readonly Dictionary<string, object?> _extra;
    private readonly SamplingCell[] _initial;
    private readonly IReadOnlyDictionary<string, int> _layout;
    public CancellationToken CancellationToken;
    internal LexicalBinding? Locals;
    public LoopTerminationEvidence? LoopEvidence;
    public MeasurementCollector? Measurements;
    private SamplingArrayArena? _arrays;
    private int _operations;

    public SamplingFrame(IReadOnlyDictionary<string, int> layout, SamplingCell[] defaults,
        Dictionary<string, object?> initial, bool reuseArrays = false)
    {
        _layout = layout;
        _initial = (SamplingCell[])defaults.Clone();
        _extra = new Dictionary<string, object?>();
        foreach (var (key, value) in initial)
        {
            if (layout.TryGetValue(key, out var index))
            {
                _initial[index] = SamplingCell.FromRaw(value);
            }
            else
            {
                _extra[key] = value;
            }
        }

        Cells = new SamplingCell[defaults.Length];
        if (reuseArrays)
        {
            _arrays = new SamplingArrayArena();
        }
    }

    public SettlementContext? Settlement { get; set; }
    public BigInteger RawPayout { get; set; }

    public void Reset(CancellationToken token, bool[]? retained = null)
    {
        token.ThrowIfCancellationRequested();
        if (retained is not null)
        {
            _arrays = null;
        }

        _arrays?.Reset();
        if (retained is null)
        {
            Array.Copy(_initial, Cells, _initial.Length);
        }
        else
        {
            for (var i = 0; i < _initial.Length; i++)
            {
                if (!retained[i])
                {
                    Cells[i] = _initial[i];
                }
            }
        }

        CancellationToken = token;
        Settlement = null;
        Locals = null;
        _operations = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ExprValue[] NewArray(int length) => _arrays is { } arena ? arena.Rent(length) : new ExprValue[length];

    internal ExprValue[] ResizeArray(ExprValue[] values, int count)
    {
        if (count == values.Length)
        {
            return values;
        }

        ExprValue[] result = NewArray(count);
        Array.Copy(values, result, count);
        return result;
    }

    internal ExprValue Scan(ExprValue source, ScanMode mode, int maxItems, bool reuseSource)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var length = CollectionPlanData.CheckScan(source, mode, maxItems);
        ExprValue[] output = reuseSource && source.ItemsArray is { } owned ? owned : NewArray(length);
        return CollectionPlanData.Scan(source, mode, output, CancellationToken);
    }

    internal ExprValue Compact(ExprValue keep, ExprValue boundaries, SegmentPacking packing,
        int maxItems, int maxSegments)
    {
        CancellationToken.ThrowIfCancellationRequested();
        (int Items, int Segments) shape =
            CollectionPlanData.CheckCompact(keep, boundaries, packing, maxItems, maxSegments);
        return CollectionPlanData.Compact(keep, boundaries, packing, NewArray(shape.Items), NewArray(shape.Segments),
            CancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CheckCancellation()
    {
        if ((++_operations & 255) == 0)
        {
            CancellationToken.ThrowIfCancellationRequested();
        }
    }

    // Only a caller that has proved every skipped operation total may use this.
    // An uncancelable token has no poll side effects, but its logical clock remains.
    internal bool TryAdvanceCancellationCheckpoints(int count)
    {
        if (count < 0 || CancellationToken.CanBeCanceled)
        {
            return false;
        }

        _operations = unchecked(_operations + count);
        return true;
    }

    internal static void ValidateArrayAllocationLength(int length)
    {
        if (length < 0)
        {
            throw new OverflowException("Array dimensions exceeded supported range.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ExprValue Read(int index, string location)
    {
        ref readonly SamplingCell cell = ref Cells[index];
        return cell.Readable ? cell.StoredValue : ReadChecked(index, location);
    }

    /// <summary>
    ///     Assigns a field to itself. A cell that already holds a typed
    ///     value is left as it is; any other cell is read and stored as usual.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Touch(int index, string location)
    {
        ref SamplingCell cell = ref Cells[index];
        if (!cell.Readable || cell.HasRaw)
        {
            cell.SetTyped(Read(index, location));
        }
    }

    private ExprValue ReadChecked(int index, string location)
    {
        if (!Cells[index].Present)
        {
            throw new ExpressionEvaluationException("EVAL_MISSING_STATE", $"State field '{location}' is absent.",
                location);
        }

        return StateValues.RequireReadable(Cells[index].ReadValue(location), location);
    }

    public Dictionary<string, object?> Export()
    {
        var state = new Dictionary<string, object?>(_extra);
        foreach (var (key, index) in _layout)
        {
            if (Cells[index].Present)
            {
                state[key] = Snapshot(Cells[index].Export());
            }
        }

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
        _ => value
    };
}

// Arena rentals stay distinct during a round. Compiler-proven transfers of dead
// private buffers are separate. State export copies recursively; persistence
// disables the arena. Both retained buffer count and element capacity are bounded.
internal sealed class SamplingArrayArena
{
    private const int MaxLength = 64;
    private const int MaxCachedLength = CollectionInputs.MaxItems;
    private const int MaxBuffers = 256;
    private const int MaxElements = MaxLength * MaxBuffers;
    private readonly List<ExprValue[]>?[] _buffers = new List<ExprValue[]>?[MaxLength + 1];
    private readonly int[] _cursors = new int[MaxLength + 1];
    private int _elements;
    private Dictionary<int, LargeBucket>? _large;
    private int _retained;

    internal void Reset()
    {
        Array.Clear(_cursors);
        if (_large is not null)
        {
            foreach (LargeBucket bucket in _large.Values)
            {
                bucket.Cursor = 0;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ExprValue[] Rent(int length)
    {
        if (length == 0)
        {
            return [];
        }

        if ((uint)length > MaxLength)
        {
            return RentLarge(length);
        }

        var position = _cursors[length]++;
        List<ExprValue[]>? buffers = _buffers[length];
        if (buffers is not null && position < buffers.Count)
        {
            return buffers[position];
        }

        var values = new ExprValue[length];
        if (_retained < MaxBuffers && length <= MaxElements - _elements)
        {
            buffers ??= _buffers[length] = new List<ExprValue[]>(4);
            buffers.Add(values);
            _retained++;
            _elements += length;
        }

        return values;
    }

    private ExprValue[] RentLarge(int length)
    {
        if ((uint)length > MaxCachedLength)
        {
            return new ExprValue[length];
        }

        LargeBucket? bucket = _large?.GetValueOrDefault(length);
        if (bucket is not null && bucket.Cursor < bucket.Values.Count)
        {
            return bucket.Values[bucket.Cursor++];
        }

        var values = new ExprValue[length];
        if (_retained < MaxBuffers && length <= MaxElements - _elements)
        {
            _large ??= new Dictionary<int, LargeBucket>();
            if (bucket is null)
            {
                bucket = new LargeBucket();
                _large.Add(length, bucket);
            }

            bucket.Values.Add(values);
            bucket.Cursor++;
            _retained++;
            _elements += length;
        }

        return values;
    }

    private sealed class LargeBucket
    {
        internal readonly List<ExprValue[]> Values = [];
        internal int Cursor;
    }
}
