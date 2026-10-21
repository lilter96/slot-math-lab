using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Math.Structured;

// ═══════════════════════════════════════════════════════════════════════════
//  Terms — the symbolic value language of the structured exact evaluator
//
//  A term is a function of independent discrete random variables (one per
//  executed draw). Terms are hash-consed: structurally equal terms are the
//  same object, so equal sub-computations are evaluated once and "these two
//  states are the same" is a reference comparison.
//
//  Every operator has one definition, the reference expression evaluator's.
//  The constructors here only decide WHEN an operator is applied: at once
//  (constant operands), per outcome of a single variable (a Table), or later.
//  Evaluation errors are values: an operator that fails yields Err, strict
//  operators propagate it and If/And/Or stay lazy, exactly as the reference
//  evaluator behaves. A constructor never drops an operand that can be Err.
// ═══════════════════════════════════════════════════════════════════════════

internal class StructuredDeclinedException(string reason) : Exception(reason);

/// <summary>
///     The quantity is known by its moments only (the amount a summarized loop accumulates), and its law was asked
///     for. Everything derived from moments stays available.
/// </summary>
internal sealed class StructuredLawUnavailableException(string reason) : StructuredDeclinedException(reason);

/// <summary>
///     A field a summarized loop writes without carrying it in its state was read after the loop. The same graph is
///     then evaluated with that loop carrying every field.
/// </summary>
internal sealed class StructuredPoisonException(string reason) : StructuredDeclinedException(reason);

/// <summary>A loop cannot be summarized as a chain; it is unrolled instead. Never leaves the executor.</summary>
internal sealed class LoopChainDeclinedException(string reason) : Exception(reason);

/// <summary>
///     Deliberate defects, one per reduction the evaluator relies on. They exist so that the equivalence gate can
///     show it fails on each of them; production code never sets one.
/// </summary>
internal enum StructuredFault
{
    None,
    MergeKeepsThenArm,
    DependentSumsConvolved,
    PseudoVariablesShared,
    ErrorsIgnored,
    CapIgnored,
    HitFromFirstPart,
    ChainRewardsUnconditional,
    ChainCrossTermDropped,
    ChainCapOffByOne,
    ChainZeroMassUnconditional,
    ProductsTakenIndependent,
    PrunedMassDropped
}

/// <summary>
///     Two independent derivations of one exact quantity disagree: a defect in the evaluator, never a property of the
///     graph.
/// </summary>
public sealed class StructuredInconsistencyException(string message) : Exception(message);

internal sealed class StructuredBudgetException(string limit)
    : Exception($"Structured exact evaluation exceeded its {limit} budget.")
{
    /// <summary>The work counter ran out, as opposed to the wall clock: a smaller attempt may still fit.</summary>
    public bool Work { get; } = limit == "work";
}

/// <summary>Deterministic work counter with an optional wall-clock guard.</summary>
internal sealed class WorkBudget(long limit, TimeSpan? maxTime, CancellationToken cancellation)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    public long Used { get; private set; }
    public long Limit { get; set; } = limit;

    public void Tick(long units = 1)
    {
        var before = Used;
        Used += units;
        if (Used > Limit)
        {
            throw new StructuredBudgetException("work");
        }

        if (before >> 12 == Used >> 12)
        {
            return;
        }

        Check();
    }

    public void Check()
    {
        cancellation.ThrowIfCancellationRequested();
        if (maxTime is { } time && _clock.Elapsed > time)
        {
            throw new StructuredBudgetException("time");
        }
    }
}

/// <summary>An independent discrete random variable: outcome i has probability Weights[i] / Total.</summary>
internal sealed class Rv
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required BigInteger[] Weights { get; init; }
    public required BigInteger Total { get; init; }

    /// <summary>The variable stands for the joint law of an independent module (see <see cref="Inference" />).</summary>
    public bool Lumped { get; init; }

    public int Count => Weights.Length;
}

internal static class V
{
    public const byte Num = 1, Bool = 2, Str = 4, Arr = 8, Rec = 16, Null = 32, All = 63;

    // No expression produces a Symbol, so one serves as the error value.
    public static readonly ExprValue Err = ExprValue.Symbol("⊥");
    public static readonly ExprValue True = ExprValue.Bool(true);
    public static readonly ExprValue False = ExprValue.Bool(false);
    public static readonly ExprValue Zero = ExprValue.Number(0);
    public static readonly ExprValue One = ExprValue.Number(1);

    public static bool IsErr(in ExprValue v) => v.Kind == ExprType.Symbol;

    public static bool IsZero(in ExprValue v) => v.IsInlineInteger
        ? v.InlineInteger == 0
        : v.Kind == ExprType.Number && v.NumberNumerator.IsZero;

    public static byte KindBit(in ExprValue v) => v.Kind switch
    {
        ExprType.Number => Num,
        ExprType.Boolean => Bool,
        ExprType.String => Str,
        ExprType.Array => Arr,
        ExprType.Record => Rec,
        ExprType.Null => Null,
        _ => 0
    };

    /// <summary>
    ///     A hash of the value alone: the same in every process, so that the listing order of a law, and with it the
    ///     order in which outcomes are visited and the work counted, does not vary from run to run.
    /// </summary>
    public static int Hash(in ExprValue v)
    {
        unchecked
        {
            if (v.IsInlineInteger)
            {
                return (int)(((ulong)v.InlineInteger * 0x9E3779B97F4A7C15UL) >> 32);
            }

            if (v.IsBoolean)
            {
                return v.BoolValue ? 0x2f1b : 0x11c3;
            }

            if (v.IsText)
            {
                var hash = v.Kind == ExprType.Symbol ? 0x51ed270b : unchecked((int)0x811c9dc5);
                foreach (var character in v.TextUnchecked)
                {
                    hash = (hash ^ character) * 16777619;
                }

                return hash;
            }

            switch (v.Kind)
            {
                case ExprType.Array:
                {
                    IReadOnlyList<ExprValue>? items = v.ArrayValue!;
                    var hash = 0x7a3f1c29 + items.Count;
                    if (items is ExprValue[] array)
                    {
                        foreach (ref readonly ExprValue item in array.AsSpan())
                        {
                            hash = (BitOperations.RotateLeft((uint)hash, 5).GetHashCode() ^ Hash(item)) * 16777619;
                        }
                    }
                    else
                    {
                        for (var i = 0; i < items.Count; i++)
                        {
                            hash = (BitOperations.RotateLeft((uint)hash, 5).GetHashCode() ^ Hash(items[i])) * 16777619;
                        }
                    }

                    return hash;
                }
                case ExprType.Number:
                    return Fold(v.NumberNumerator) * 31 + Fold(v.NumberDenominator);
                case ExprType.Record:
                {
                    // Field order is not part of a record's identity, so the fields are combined commutatively.
                    var hash = 0x1b873593;
                    foreach ((var name, ExprValue value) in v.RecordValue!)
                    {
                        hash += (Hash(ExprValue.String(name)) * 31) ^ Hash(value);
                    }

                    return hash;
                }
                default: return 0x5bd1e995 + (int)v.Kind;
            }
        }
    }

    private static int Fold(BigInteger number) =>
        unchecked((int)(ulong)(number & ulong.MaxValue) * 16777619 + (int)number.GetBitLength() + number.Sign);

    /// <summary>A total order on values, used only to give a law one canonical listing.</summary>
    public static int Order(ExprValue a, ExprValue b)
    {
        if (a.Kind != b.Kind)
        {
            return ((int)a.Kind).CompareTo((int)b.Kind);
        }

        switch (a.Kind)
        {
            case ExprType.Number: return ExprValue.CompareNumbers(a, b);
            case ExprType.Boolean: return a.BoolValue.CompareTo(b.BoolValue);
            case ExprType.String or ExprType.Symbol: return string.CompareOrdinal(a.StringValue, b.StringValue);
            case ExprType.Array:
                IReadOnlyList<ExprValue>? x = a.ArrayValue!;
                IReadOnlyList<ExprValue>? y = b.ArrayValue!;
                for (var i = 0; i < x.Count && i < y.Count; i++)
                {
                    var c = Order(x[i], y[i]);
                    if (c != 0)
                    {
                        return c;
                    }
                }

                return x.Count.CompareTo(y.Count);
            case ExprType.Record: return string.CompareOrdinal(a.ToString(), b.ToString());
            default: return 0;
        }
    }

    public sealed class ValueComparer : IEqualityComparer<ExprValue>
    {
        public static readonly ValueComparer Instance = new();
        public bool Equals(ExprValue x, ExprValue y) => x.Equals(y);
        public int GetHashCode(ExprValue v) => Hash(v);
    }
}

/// <summary>Sets of random-variable ids as bit arrays; a shorter array has zeros beyond its end.</summary>
internal static class Bits
{
    public static readonly ulong[] Empty = [];

    public static ulong[] Single(int id)
    {
        var bits = new ulong[(id >> 6) + 1];
        bits[id >> 6] = 1UL << (id & 63);
        return bits;
    }

    public static bool Has(ulong[] set, int id) => id >> 6 < set.Length && (set[id >> 6] & (1UL << (id & 63))) != 0;

    public static bool IsEmpty(ulong[] set)
    {
        foreach (var word in set)
        {
            if (word != 0)
            {
                return false;
            }
        }

        return true;
    }

    public static bool Intersects(ulong[] a, ulong[] b)
    {
        var n = System.Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            if ((a[i] & b[i]) != 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsSubset(ulong[] a, ulong[] of)
    {
        for (var i = 0; i < a.Length; i++)
        {
            if ((a[i] & ~(i < of.Length ? of[i] : 0UL)) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Union that returns one of its arguments when that argument already is the union.</summary>
    public static ulong[] Union(ulong[] a, ulong[] b)
    {
        if (ReferenceEquals(a, b) || IsSubset(b, a))
        {
            return a;
        }

        if (IsSubset(a, b))
        {
            return b;
        }

        var result = new ulong[System.Math.Max(a.Length, b.Length)];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (i < a.Length ? a[i] : 0UL) | (i < b.Length ? b[i] : 0UL);
        }

        return result;
    }

    public static int Count(ulong[] set)
    {
        var count = 0;
        foreach (var word in set)
        {
            count += BitOperations.PopCount(word);
        }

        return count;
    }

    public static int First(ulong[] set)
    {
        for (var i = 0; i < set.Length; i++)
        {
            if (set[i] != 0)
            {
                return (i << 6) + BitOperations.TrailingZeroCount(set[i]);
            }
        }

        return -1;
    }

    public static IEnumerable<int> Enumerate(ulong[] set)
    {
        for (var i = 0; i < set.Length; i++)
        {
            for (var word = set[i]; word != 0; word &= word - 1)
            {
                yield return (i << 6) + BitOperations.TrailingZeroCount(word);
            }
        }
    }
}

internal enum Op : byte
{
    Arith,
    Cmp,
    Not,
    Call,
    Extract,
    Path,
    Arr,
    Tuple,
    Opaque,
    Index
}

internal abstract class Term
{
    /// <summary>The term can evaluate to Err.</summary>
    public bool CanErr;

    public int Hash;
    public int Id = -1;

    /// <summary>Kinds a non-error value can have (bits of <see cref="V" />).</summary>
    public byte Kinds;

    /// <summary>The term's own operation can fail on operands that are not themselves errors.</summary>
    public bool OwnFail;

    /// <summary>What an already-interned structurally equal request resolves to.</summary>
    public Term? Resolved;

    /// <summary>The random variables the value depends on.</summary>
    public ulong[] Support = Bits.Empty;

    /// <summary>Every non-error value the term can take, when that set is small; null when unknown.</summary>
    public ExprValue[]? Vals;

    public bool Pure => !CanErr;
    public bool IsNum => Kinds == V.Num;
    public bool IsBool => Kinds == V.Bool;
    public bool PureBool => Kinds == V.Bool && !CanErr;
}

internal sealed class ConstTerm : Term
{
    public bool IsErr;
    public ExprValue Value;
}

/// <summary>
///     A value the term is still to be given: a state field as a loop iteration finds it. Its kinds, its value set
///     when one is declared, and whether it can be an error hold for every value later substituted.
/// </summary>
internal sealed class ParamTerm : Term
{
    public required string Name;
}

/// <summary>
///     The amount a summarized loop adds to an accumulator, known through its conditional moments given the loop's
///     entry and exit (ordinary terms), not through its law. Given those, it is independent of everything else.
/// </summary>
internal sealed class MomentTerm : Term
{
    /// <summary>Whether <see cref="Lo" /> and <see cref="Hi" /> are proven.</summary>
    public bool Bounded;

    /// <summary>The accumulator's position among those of its loop.</summary>
    public required int Index;

    public ExprValue Lo, Hi;

    /// <summary>The loop instance; moment terms of one loop are dependent.</summary>
    public required int Loop;

    /// <summary>E[this | entry, exit].</summary>
    public required Term Mean;

    /// <summary>
    ///     "this &gt; 0" as a term that is, given entry and exit, an independent Bernoulli; null when the sign is not
    ///     known.
    /// </summary>
    public Func<Term>? Positive;

    /// <summary>E[this · other | entry, exit] for each accumulator of the same loop, by <see cref="Index" />.</summary>
    public Term[] Second = [];

    /// <summary>A step every value is a whole multiple of; zero when the value is always zero.</summary>
    public Rational Step = Rational.Zero;
}

/// <summary>A function of one random variable, tabulated per outcome.</summary>
internal sealed class TableTerm : Term
{
    public required Rv Rv;
    public required ExprValue[] Values;
}

/// <summary>C0 + Σ Coefs[i]·Terms[i] over numeric terms, at most one Table per variable, sorted by term id.</summary>
internal sealed class LinTerm : Term
{
    public ExprValue C0;
    public required ExprValue[] Coefs;
    public required Term[] Terms;
}

internal sealed class IfTerm : Term
{
    public required Term C, A, B;
}

internal sealed class LogicTerm : Term
{
    public required Term A, B;
    public bool IsOr;
}

/// <summary>Children[Selector]; lazy in the children. Selector is an integer in range, otherwise Err.</summary>
internal sealed class SelTerm : Term
{
    public required Term[] Children;
    public required Term Selector;
}

/// <summary>A strict operator applied to its arguments (Tuple alone keeps an Err argument as an element).</summary>
internal sealed class OpTerm : Term
{
    public required Term[] Args;
    public int Aux;
    public Op Code;
    public string[]? Names;
    public object? Payload;
}

internal sealed class TermFactory
{
    private const int MaxVals = 64;
    private const int ComboLimit = 512;
    public const int TabulateLimit = 1 << 20;

    // Most tables built during substitution turn out to exist already; their arrays are reused for the next ones.
    private const int SpareCells = 1 << 21;
    public readonly WorkBudget Budget;

    public readonly ConstTerm ErrT, TrueT, FalseT, ZeroT, OneT, NullT;

    public readonly List<Rv> Rvs = [];

    private readonly Dictionary<Term, Term> _interned = new(new Structural());

    private readonly ExprValue[] _seen = new ExprValue[MaxVals + 1];
    private readonly Dictionary<int, Stack<ExprValue[]>> _spare = new();

    /// <summary>
    ///     Set whenever a constructor yields a term whose own operation can fail. The executor clears it
    ///     before translating a statement, so afterwards it tells whether that statement can newly fail.
    /// </summary>
    public bool FailSeen;

    public StructuredFault Fault;

    /// <summary>Slots of moment terms, of parameters and of the poison value: what has no law.</summary>
    public ulong[] MomentSlots = Bits.Empty, ParamSlots = Bits.Empty, PoisonSlots = Bits.Empty;

    private ParamTerm? _poison;
    private int _spareCells;

    public TermFactory(WorkBudget budget)
    {
        Budget = budget;
        ErrT = (ConstTerm)Const(V.Err);
        TrueT = (ConstTerm)Const(V.True);
        FalseT = (ConstTerm)Const(V.False);
        ZeroT = (ConstTerm)Const(V.Zero);
        OneT = (ConstTerm)Const(V.One);
        NullT = (ConstTerm)Const(ExprValue.Null);
    }

    public int TermCount { get; private set; }

    /// <summary>What a field holds after a summarized loop that wrote it without carrying it: any use is refused.</summary>
    public ParamTerm Poison
    {
        get
        {
            if (_poison is null)
            {
                _poison = NewParam("poison", V.All, null, true);
                PoisonSlots = _poison.Support;
            }

            return _poison;
        }
    }

    public Rv NewRv(string name, BigInteger[] weights, bool lumped = false)
    {
        BigInteger total = BigInteger.Zero;
        foreach (BigInteger weight in weights)
        {
            total += weight;
        }

        var rv = new Rv
        {
            Id = Rvs.Count,
            Name = name,
            Weights = weights,
            Total = total,
            Lumped = lumped
        };
        Rvs.Add(rv);
        return rv;
    }

    /// <summary>A parameter: a leaf with a slot of its own, so that what depends on it is known from supports.</summary>
    public ParamTerm NewParam(string name, byte kinds, ExprValue[]? values, bool canErr)
    {
        Rv slot = NewRv("param:" + name, [BigInteger.One], true);
        var term = new ParamTerm
        {
            Name = name,
            Hash = HashCode.Combine(slot.Id, 8),
            Kinds = kinds,
            Vals = values,
            CanErr = canErr,
            Support = Bits.Single(slot.Id)
        };
        ParamSlots = Bits.Union(ParamSlots, term.Support);
        return (ParamTerm)Add(term);
    }

    public int NewLoopSlot(string name)
    {
        Rv slot = NewRv("loop:" + name, [BigInteger.One], true);
        MomentSlots = Bits.Union(MomentSlots, Bits.Single(slot.Id));
        return slot.Id;
    }

    public MomentTerm NewMoment(int loop, int index, Term mean)
    {
        var term = new MomentTerm
        {
            Loop = loop,
            Index = index,
            Mean = mean,
            Hash = HashCode.Combine(loop, index, 9),
            Kinds = V.Num,
            CanErr = mean.CanErr,
            Support = Bits.Union(Bits.Single(loop), mean.Support)
        };
        return (MomentTerm)Add(term);
    }

    // ── Interning ────────────────────────────────────────────────────────

    private bool TryGet(Term candidate, out Term existing)
    {
        if (_interned.TryGetValue(candidate, out Term? found))
        {
            existing = found.Resolved ?? found;
            if (existing.OwnFail)
            {
                FailSeen = true;
            }

            return true;
        }

        existing = candidate;
        return false;
    }

    private Term Add(Term term, Term? resolved = null)
    {
        Budget.Tick();
        term.Id = TermCount++;
        term.Resolved = resolved;
        _interned.Add(term, term);
        Term result = resolved ?? term;
        if (result.OwnFail)
        {
            FailSeen = true;
        }

        return result;
    }

    public Term Const(ExprValue value)
    {
        var term = new ConstTerm
        {
            Value = value,
            Hash = V.Hash(value) * 31 + 1
        };
        if (TryGet(term, out Term existing))
        {
            return existing;
        }

        term.IsErr = V.IsErr(value);
        term.CanErr = term.IsErr;
        term.Kinds = V.KindBit(value);
        term.Vals = term.IsErr ? [] : [value];
        return Add(term);
    }

    public Term Bool(bool value) => value ? TrueT : FalseT;

    public Term Table(Rv rv, ExprValue[] values)
    {
        Debug.Assert(values.Length == rv.Count);
        var same = true;
        for (var i = 1; i < values.Length && same; i++)
        {
            same = values[i].Equals(values[0]);
        }

        if (same)
        {
            Term constant = Const(values[0]);
            Release(values);
            return constant;
        }

        var keys = ArrayPool<int>.Shared.Rent(values.Length);
        try
        {
            var hash = new HashCode();
            hash.Add(rv.Id);
            for (var i = 0; i < values.Length; i++)
            {
                hash.Add(keys[i] = V.Hash(values[i]));
            }

            var term = new TableTerm
            {
                Rv = rv,
                Values = values,
                Hash = hash.ToHashCode() * 31 + 2
            };
            if (TryGet(term, out Term existing))
            {
                Release(values);
                return existing;
            }

            term.Support = Bits.Single(rv.Id);

            // The distinct values in first-seen order, tracked only while they are few.
            Span<int> seenKeys = stackalloc int[MaxVals + 1];
            var seen = 0;
            for (var i = 0; i < values.Length; i++)
            {
                ref readonly ExprValue value = ref values[i];
                if (V.IsErr(value))
                {
                    term.CanErr = true;
                    continue;
                }

                term.Kinds |= V.KindBit(value);
                if (seen > MaxVals)
                {
                    continue;
                }

                var known = false;
                for (var at = 0; at < seen && !known;)
                {
                    var next = seenKeys[at..seen].IndexOf(keys[i]);
                    if (next < 0)
                    {
                        break;
                    }

                    at += next;
                    known = _seen[at++].Equals(value);
                }

                if (known)
                {
                    continue;
                }

                seenKeys[seen] = keys[i];
                _seen[seen++] = value;
            }

            term.Vals = seen <= MaxVals ? _seen.AsSpan(0, seen).ToArray() : null;
            Budget.Tick(values.Length);
            return Add(term);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(keys);
        }
    }

    /// <summary>
    ///     An array for a table over a variable with that many outcomes. The caller writes every cell and passes it to
    ///     Table.
    /// </summary>
    public ExprValue[] Rent(int count)
    {
        if (!_spare.TryGetValue(count, out Stack<ExprValue[]>? stack) || stack.Count == 0)
        {
            return new ExprValue[count];
        }

        _spareCells -= count;
        return stack.Pop();
    }

    private void Release(ExprValue[] values)
    {
        if (_spareCells + values.Length > SpareCells)
        {
            return;
        }

        if (!_spare.TryGetValue(values.Length, out Stack<ExprValue[]>? stack))
        {
            _spare[values.Length] = stack = new Stack<ExprValue[]>();
        }

        stack.Push(values);
        _spareCells += values.Length;
    }

    // ── Operator application on values ───────────────────────────────────

    public static ExprValue Apply(Op code, int aux, object? payload, string[]? names, ReadOnlySpan<ExprValue> a)
    {
        try
        {
            switch (code)
            {
                case Op.Arith:
                    if (a[0].Kind != ExprType.Number || a[1].Kind != ExprType.Number)
                    {
                        return V.Err;
                    }

                    if ((BinaryOp)aux == BinaryOp.Div && V.IsZero(a[1]))
                    {
                        return V.Err;
                    }

                    return ExactExpressionEvaluator.Arithmetic((BinaryOp)aux, a[0], a[1]);
                case Op.Cmp:
                    return ExactExpressionEvaluator.Compare((CompareOp)aux, a[0], a[1]);
                case Op.Not:
                    return a[0].Kind == ExprType.Boolean ? ExprValue.Bool(!a[0].BoolValue) : V.Err;
                case Op.Extract:
                    // GraphCompiler.ExtractBigInteger: an integer amount is itself, anything else is zero.
                    return a[0].Kind == ExprType.Number && a[0].NumberDenominator.IsOne ? a[0] : V.Zero;
                case Op.Arr or Op.Tuple:
                    return ExprValue.ArrayOwned(a.ToArray(), false);
                case Op.Call:
                    var function = (string)payload!;
                    // The two failures a guarded expression commonly leaves unreachable are answered without an exception.
                    if (a.Length == 2 && function == "index" && a[0].ItemsArray is { } items && a[1].IsInlineInteger)
                    {
                        return (ulong)a[1].InlineInteger < (ulong)items.Length ? items[a[1].InlineInteger] : V.Err;
                    }

                    if (a.Length == 1 && function == "tonumber")
                    {
                        if (a[0].TryGetNumericText(out var parsed))
                        {
                            return ExprValue.Number(parsed);
                        }

                        if (!a[0].IsText)
                        {
                            return V.Err;
                        }
                    }

                    return CollectionFunctions.Call(function, a.Length > 0 ? a[0] : default,
                        a.Length > 1 ? a[1] : default, a.Length);
                case Op.Index:
                    return ((Dictionary<ExprValue, int>)payload!).TryGetValue(a[0], out var position)
                        ? ExprValue.Number(position)
                        : V.Err;
                case Op.Path:
                    var segments = (string[])payload!;
                    if (segments.Length == 0)
                    {
                        return a[0].Kind == ExprType.Null ? V.Err : a[0];
                    }

                    var nestedState = new Dictionary<string, object?>
                    {
                        ["value"] = a[0].ToStateObject()
                    };
                    return StateValues.Read(nestedState, ["value", .. segments]);
                default:
                    var state = new Dictionary<string, object?>(names!.Length);
                    for (var i = 0; i < names.Length; i++)
                    {
                        state[names[i]] = a[i].ToStateObject();
                    }

                    return ExactExpressionEvaluator.Evaluate((Expression)payload!, new EvalContext
                    {
                        State = state
                    });
            }
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            return V.Err;
        }
    }

    private static Rv? CommonRv(ReadOnlySpan<Term> args)
    {
        Rv? rv = null;
        foreach (Term arg in args)
        {
            if (arg is ConstTerm)
            {
                continue;
            }

            if (arg is not TableTerm table || (rv is not null && table.Rv != rv))
            {
                return null;
            }

            rv = table.Rv;
        }

        return rv is { Count: <= TabulateLimit } ? rv : null;
    }

    private static ExprValue At(Term term, int outcome) =>
        term is TableTerm table ? table.Values[outcome] : ((ConstTerm)term).Value;

    // ── Strict operators ─────────────────────────────────────────────────

    public Term MakeOp(Op code, int aux, object? payload, string[]? names, Term[] args)
    {
        var strict = code != Op.Tuple;
        var allConst = true;
        foreach (Term arg in args)
        {
            if (arg is not ConstTerm constant)
            {
                allConst = false;
                continue;
            }

            if (strict && constant.IsErr)
            {
                return ErrT;
            }
        }

        if (allConst)
        {
            var values = new ExprValue[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                values[i] = ((ConstTerm)args[i]).Value;
            }

            ExprValue value = Apply(code, aux, payload, names, values);
            if (V.IsErr(value))
            {
                FailSeen = true;
            }

            return Const(value);
        }

        // An array of terms keeps its elements visible; everything else over one variable is tabulated.
        if (code != Op.Arr && CommonRv(args) is { } rv)
        {
            ExprValue[] table = Rent(rv.Count);
            var buffer = new ExprValue[args.Length];
            for (var outcome = 0; outcome < table.Length; outcome++)
            {
                var inherited = false;
                for (var i = 0; i < args.Length; i++)
                {
                    buffer[i] = At(args[i], outcome);
                    inherited |= strict && V.IsErr(buffer[i]);
                }

                if (inherited)
                {
                    table[outcome] = V.Err;
                    continue;
                }

                table[outcome] = Apply(code, aux, payload, names, buffer);
                if (V.IsErr(table[outcome]))
                {
                    FailSeen = true;
                }
            }

            return Table(rv, table);
        }

        if (code == Op.Not && args[0] is OpTerm { Code: Op.Not } inner && inner.Args[0].IsBool)
        {
            return inner.Args[0];
        }

        var hash = new HashCode();
        hash.Add((int)code);
        hash.Add(aux);
        hash.Add(payload is string text ? text.GetHashCode() :
            payload is string[] path ? path.Length :
            payload is null ? 0 : RuntimeHelpers.GetHashCode(payload));
        foreach (Term arg in args)
        {
            hash.Add(arg.Id);
        }

        var term = new OpTerm
        {
            Code = code,
            Aux = aux,
            Payload = payload,
            Names = names,
            Args = args,
            Hash = hash.ToHashCode() * 31 + 3
        };
        if (TryGet(term, out Term existing))
        {
            return existing;
        }

        foreach (Term arg in args)
        {
            term.Support = Bits.Union(term.Support, arg.Support);
            term.CanErr |= arg.CanErr;
        }

        if (!Abstract(term))
        {
            AbstractByRule(term);
        }

        term.CanErr |= term.OwnFail;
        // One possible value and no way to fail: the term is that constant.
        return Add(term, term is { Vals.Length: 1, CanErr: false } ? Const(term.Vals[0]) : null);
    }

    /// <summary>Exact abstraction: apply the operator to every combination of the arguments' known values.</summary>
    private bool Abstract(OpTerm term)
    {
        if (term.Code is Op.Arr or Op.Tuple)
        {
            return false;
        }

        long combos = 1;
        foreach (Term arg in term.Args)
        {
            if (arg.Vals is null)
            {
                return false;
            }

            combos *= arg.Vals.Length;
            if (combos > ComboLimit)
            {
                return false;
            }
        }

        var seen = new HashSet<ExprValue>(V.ValueComparer.Instance);
        var overflow = false;
        var buffer = new ExprValue[term.Args.Length];
        var index = new int[term.Args.Length];
        for (long combo = 0; combo < combos; combo++)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = term.Args[i].Vals![index[i]];
            }

            ExprValue value = Apply(term.Code, term.Aux, term.Payload, term.Names, buffer);
            if (V.IsErr(value))
            {
                term.OwnFail = true;
            }
            else
            {
                term.Kinds |= V.KindBit(value);
                if (!overflow && seen.Add(value) && seen.Count > MaxVals)
                {
                    overflow = true;
                }
            }

            for (var i = 0; i < index.Length; i++)
            {
                if (++index[i] < term.Args[i].Vals!.Length)
                {
                    break;
                }

                index[i] = 0;
            }
        }

        Budget.Tick(combos);
        term.Vals = overflow ? null : [.. seen];
        return true;
    }

    /// <summary>Sound fallback when the arguments' values are not enumerable: what can the result be, can the operator fail.</summary>
    private static void AbstractByRule(OpTerm term)
    {
        Term[] a = term.Args;

        bool Numeric(int i)
        {
            return a.Length > i && a[i].IsNum;
        }

        switch (term.Code)
        {
            case Op.Arith:
                term.Kinds = V.Num;
                term.OwnFail = !(Numeric(0) && Numeric(1)) || (BinaryOp)term.Aux == BinaryOp.Div;
                break;
            case Op.Cmp: term.Kinds = V.Bool; break;
            case Op.Not:
                term.Kinds = V.Bool;
                term.OwnFail = !a[0].IsBool;
                break;
            case Op.Extract: term.Kinds = V.Num; break;
            case Op.Index:
                // A table that lists every value its argument takes cannot miss; a partial one is marked by ~count.
                term.Kinds = V.Num;
                term.OwnFail = term.Aux < 0;
                var listed = term.Aux < 0 ? ~term.Aux : term.Aux;
                if (listed <= MaxVals)
                {
                    term.Vals = new ExprValue[listed];
                    for (var i = 0; i < listed; i++)
                    {
                        term.Vals[i] = ExprValue.Number(i);
                    }
                }

                break;
            case Op.Arr or Op.Tuple: term.Kinds = V.Arr; break;
            case Op.Call:
                var function = (string)term.Payload!;
                term.OwnFail = true;
                term.Kinds = V.All;
                switch (function)
                {
                    case "tonumber": term.Kinds = V.Num; break;
                    case "tostring":
                        term.Kinds = V.Str;
                        term.OwnFail = !(a.Length == 1 && Numeric(0));
                        break;
                    case "length":
                        term.Kinds = V.Num;
                        term.OwnFail = !(a.Length == 1 && a[0].Kinds != 0 && (a[0].Kinds & ~(V.Arr | V.Str)) == 0);
                        break;
                    case "abs" or "floor" or "ceil" or "round":
                        term.Kinds = V.Num;
                        term.OwnFail = !(a.Length == 1 && Numeric(0));
                        break;
                    case "min" or "max":
                        term.Kinds = V.Num;
                        term.OwnFail = !(a.Length == 2 && Numeric(0) && Numeric(1));
                        break;
                    case "contains":
                        term.Kinds = V.Bool;
                        term.OwnFail = !(a.Length == 2 &&
                                         (a[0].Kinds == V.Arr || (a[0].Kinds == V.Str && a[1].Kinds == V.Str)));
                        break;
                    case "append":
                        term.Kinds = V.Arr;
                        term.OwnFail = !(a.Length == 2 && a[0].Kinds == V.Arr);
                        break;
                    case "index" when a.Length == 2: AbstractIndex(term); break;
                }

                break;
            default:
                term.Kinds = V.All;
                term.OwnFail = true;
                break;
        }
    }

    private static void AbstractIndex(OpTerm term)
    {
        var length = -1;
        byte kinds = 0;
        HashSet<ExprValue>? values = new(V.ValueComparer.Instance);
        if (term.Args[0] is OpTerm { Code: Op.Arr } array)
        {
            length = array.Args.Length;
            foreach (Term element in array.Args)
            {
                kinds |= element.Kinds;
                if (element.Vals is null)
                {
                    values = null;
                }
                else if (values is not null)
                {
                    foreach (ExprValue value in element.Vals)
                    {
                        values.Add(value);
                    }
                }
            }
        }
        else if (term.Args[0] is ConstTerm { Value.Kind: ExprType.Array } constant)
        {
            IReadOnlyList<ExprValue>? items = constant.Value.ArrayValue!;
            length = items.Count;
            foreach (ExprValue item in items)
            {
                kinds |= V.KindBit(item);
                values.Add(item);
            }
        }

        if (length < 0)
        {
            return;
        }

        term.Kinds = kinds;
        term.Vals = values is { Count: <= MaxVals } ? [.. values] : null;
        if (term.Args[1].Vals is not { } positions)
        {
            return;
        }

        foreach (ExprValue position in positions)
        {
            if (!position.IsInlineInteger || (ulong)position.InlineInteger >= (ulong)length)
            {
                return;
            }
        }

        term.OwnFail = false;
    }

    public Term Arith(BinaryOp op, Term a, Term b)
    {
        if (a.IsNum && b.IsNum)
        {
            switch (op)
            {
                case BinaryOp.Add: return Lin(V.Zero, [(V.One, a), (V.One, b)]);
                case BinaryOp.Sub: return Lin(V.Zero, [(V.One, a), (ExprValue.Number(-1), b)]);
                case BinaryOp.Mul when a is ConstTerm ca: return Lin(V.Zero, [(ca.Value, b)]);
                case BinaryOp.Mul when b is ConstTerm cb: return Lin(V.Zero, [(cb.Value, a)]);
                case BinaryOp.Div
                    when b is ConstTerm cb && !V.IsZero(cb.Value):
                    return Lin(V.Zero, [(ExprValue.Div(V.One, cb.Value), a)]);
            }
        }

        return MakeOp(Op.Arith, (int)op, null, null, [a, b]);
    }

    public Term Cmp(CompareOp op, Term a, Term b) => MakeOp(Op.Cmp, (int)op, null, null, [a, b]);

    public Term Not(Term a) => MakeOp(Op.Not, 0, null, null, [a]);

    public Term Extract(Term a) => MakeOp(Op.Extract, 0, null, null, [a]);

    public Term Tuple(Term[] elements) => MakeOp(Op.Tuple, 0, null, null, elements);

    public Term Arr(Term[] elements) => MakeOp(Op.Arr, 0, null, null, elements);

    public Term Path(Term value, string[] segments) =>
        segments.Length == 0 && (value.Kinds & V.Null) == 0 ? value : MakeOp(Op.Path, 0, segments, null, [value]);

    public Term Call(string function, Term[] args)
    {
        if (args.Length == 2)
        {
            (Term first, Term second) = (args[0], args[1]);
            if (function == "append" && second is not ConstTerm { IsErr: true })
            {
                if (first is OpTerm { Code: Op.Arr } array)
                {
                    return Arr([.. array.Args, second]);
                }

                if (first is ConstTerm { Value.Kind: ExprType.Array } constant && second is not ConstTerm)
                {
                    return Arr([.. constant.Value.ArrayValue!.Select(Const), second]);
                }
            }

            if (function == "index" && first is OpTerm { Code: Op.Arr } elements && second is ConstTerm
                {
                    Value.IsInlineInteger: true
                } position
                && (ulong)position.Value.InlineInteger < (ulong)elements.Args.Length)
            {
                // Dropping the other elements is sound only when none of them can be an error.
                var others = true;
                for (var i = 0; i < elements.Args.Length && others; i++)
                {
                    others = i == position.Value.InlineInteger || elements.Args[i].Pure;
                }

                if (others)
                {
                    return elements.Args[position.Value.InlineInteger];
                }
            }
        }

        if (args.Length == 1 && function == "length" && args[0] is OpTerm { Code: Op.Arr, CanErr: false } sized)
        {
            return Const(ExprValue.Number(sized.Args.Length));
        }

        return MakeOp(Op.Call, 0, function, null, args);
    }


    public Term Opaque(Expression expression, string[] names, Term[] args) =>
        MakeOp(Op.Opaque, 0, expression, names, args);

    /// <summary>
    ///     The position of the term's value in a table. A total table lists every value the term takes; a partial one
    ///     lists those it takes where it matters, and the lookup is an error elsewhere.
    /// </summary>
    public Term Index(Term term, Dictionary<ExprValue, int> positions, bool total) =>
        MakeOp(Op.Index, total ? positions.Count : ~positions.Count, positions, null, [term]);

    // ── Linear combinations ──────────────────────────────────────────────

    public Term Lin(ExprValue c0, IReadOnlyList<(ExprValue Coef, Term Term)> parts)
    {
        var numeric = true;
        foreach ((ExprValue _, Term term) in parts)
        {
            if (term is ConstTerm { IsErr: true })
            {
                return ErrT;
            }

            numeric &= term.IsNum;
        }

        if (numeric)
        {
            return LinCore(c0, parts);
        }

        // A summand that is not provably a number keeps the plain operators, which report the type error.
        Term sum = Const(c0);
        foreach ((ExprValue coef, Term term) in parts)
        {
            sum = MakeOp(Op.Arith, (int)BinaryOp.Add, null, null,
                [sum, MakeOp(Op.Arith, (int)BinaryOp.Mul, null, null, [Const(coef), term])]);
        }

        return sum;
    }

    private Term LinCore(ExprValue c0, IReadOnlyList<(ExprValue Coef, Term Term)> parts)
    {
        var coefs = new Dictionary<Term, ExprValue>(ReferenceEqualityComparer.Instance);
        Dictionary<Rv, ExprValue[]>? tables = null;

        void AddPart(ExprValue coef, Term term)
        {
            switch (term)
            {
                case ConstTerm constant:
                    c0 = ExprValue.Add(c0, ExprValue.Mul(coef, constant.Value));
                    break;
                case LinTerm lin:
                    c0 = ExprValue.Add(c0, ExprValue.Mul(coef, lin.C0));
                    for (var i = 0; i < lin.Terms.Length; i++)
                    {
                        AddPart(ExprValue.Mul(coef, lin.Coefs[i]), lin.Terms[i]);
                    }

                    break;
                case TableTerm table:
                    tables ??= new Dictionary<Rv, ExprValue[]>();
                    if (!tables.TryGetValue(table.Rv, out ExprValue[]? sum))
                    {
                        tables[table.Rv] = sum = Rent(table.Rv.Count);
                        Array.Fill(sum, V.Zero);
                    }

                    for (var i = 0; i < sum.Length; i++)
                    {
                        sum[i] = V.IsErr(sum[i]) || V.IsErr(table.Values[i])
                            ? V.Err
                            : ExprValue.Add(sum[i], ExprValue.Mul(coef, table.Values[i]));
                    }

                    break;
                default:
                    coefs[term] = coefs.TryGetValue(term, out ExprValue existing)
                        ? ExprValue.Add(existing, coef)
                        : coef;
                    break;
            }
        }

        foreach ((ExprValue coef, Term term) in parts)
        {
            AddPart(coef, term);
        }

        var terms = new List<(Term Term, ExprValue Coef)>(coefs.Count + (tables?.Count ?? 0));
        foreach ((Term term, ExprValue coef) in coefs)
        {
            if (!(V.IsZero(coef) && term.Pure))
            {
                terms.Add((term, coef));
            }
        }

        if (tables is not null)
        {
            if (terms.Count == 0 && tables.Count == 1)
            {
                (Rv rv, ExprValue[] values) = tables.Single();
                for (var i = 0; i < values.Length; i++)
                {
                    if (!V.IsErr(values[i]))
                    {
                        values[i] = ExprValue.Add(values[i], c0);
                    }
                }

                return Table(rv, values);
            }

            foreach ((Rv rv, ExprValue[] values) in tables)
            {
                Term table = Table(rv, values);
                if (table is ConstTerm constant)
                {
                    if (constant.IsErr)
                    {
                        return ErrT;
                    }

                    c0 = ExprValue.Add(c0, constant.Value);
                }
                else
                {
                    terms.Add((table, V.One));
                }
            }
        }

        if (terms.Count == 0)
        {
            return Const(c0);
        }

        if (terms.Count == 1 && V.IsZero(c0) && terms[0].Coef.Equals(V.One))
        {
            return terms[0].Term;
        }

        terms.Sort(static (x, y) => x.Term.Id.CompareTo(y.Term.Id));

        var hash = new HashCode();
        hash.Add(V.Hash(c0));
        foreach ((Term term, ExprValue coef) in terms)
        {
            hash.Add(term.Id);
            hash.Add(V.Hash(coef));
        }

        var result = new LinTerm
        {
            C0 = c0,
            Terms = terms.Select(p => p.Term).ToArray(),
            Coefs = terms.Select(p => p.Coef).ToArray(),
            Hash = hash.ToHashCode() * 31 + 4
        };
        if (TryGet(result, out Term interned))
        {
            return interned;
        }

        result.Kinds = V.Num;
        HashSet<ExprValue>? sums = new(V.ValueComparer.Instance)
        {
            c0
        };
        for (var i = 0; i < result.Terms.Length; i++)
        {
            Term term = result.Terms[i];
            result.Support = Bits.Union(result.Support, term.Support);
            result.CanErr |= term.CanErr;
            if (sums is null)
            {
                continue;
            }

            if (term.Vals is null || (long)sums.Count * term.Vals.Length > ComboLimit)
            {
                sums = null;
                continue;
            }

            var next = new HashSet<ExprValue>(V.ValueComparer.Instance);
            foreach (ExprValue sum in sums)
            {
                foreach (ExprValue value in term.Vals)
                {
                    next.Add(ExprValue.Add(sum, ExprValue.Mul(result.Coefs[i], value)));
                }
            }

            sums = next.Count <= MaxVals ? next : null;
        }

        result.Vals = sums is null ? null : [.. sums];
        return Add(result, result is { Vals.Length: 1, CanErr: false } ? Const(result.Vals[0]) : null);
    }

    public Term Scale(ExprValue coef, Term term) => Lin(V.Zero, [(coef, term)]);

    private static (ExprValue C0, Term[] Terms, ExprValue[] Coefs) LinView(Term term) => term switch
    {
        LinTerm lin => (lin.C0, lin.Terms, lin.Coefs),
        ConstTerm constant => (constant.Value, [], []),
        _ => (V.Zero, [term], [V.One])
    };

    // ── Lazy operators ───────────────────────────────────────────────────

    public Term If(Term c, Term a, Term b)
    {
        if (c is ConstTerm condition)
        {
            if (condition.Value.Kind == ExprType.Boolean)
            {
                return condition.Value.BoolValue ? a : b;
            }

            if (!condition.IsErr)
            {
                FailSeen = true;
            }

            return ErrT;
        }

        if (a == b && c.PureBool)
        {
            return a;
        }

        if (c is OpTerm { Code: Op.Not } negated && negated.Args[0].IsBool)
        {
            return If(negated.Args[0], b, a);
        }

        if (c.PureBool && a == TrueT && b == FalseT)
        {
            return c;
        }

        if (c.PureBool && a == FalseT && b == TrueT)
        {
            return Not(c);
        }

        if (CommonRv([c, a, b]) is { } rv)
        {
            ExprValue[] table = Rent(rv.Count);
            for (var i = 0; i < table.Length; i++)
            {
                ExprValue test = At(c, i);
                if (test.Kind == ExprType.Boolean)
                {
                    table[i] = At(test.BoolValue ? a : b, i);
                }
                else
                {
                    if (!V.IsErr(test))
                    {
                        FailSeen = true;
                    }

                    table[i] = V.Err;
                }
            }

            return Table(rv, table);
        }

        // If(c, X + P, X + Q) = X + If(c, P, Q): additive parts shared by both branches leave the conditional.
        if (a.IsNum && b.IsNum && (a is LinTerm || b is LinTerm))
        {
            (ExprValue a0, Term[] aTerms, ExprValue[] aCoefs) = LinView(a);
            (ExprValue b0, Term[] bTerms, ExprValue[] bCoefs) = LinView(b);
            var shared = new List<(ExprValue, Term)>();
            var onlyA = new List<(ExprValue, Term)>();
            var onlyB = new List<(ExprValue, Term)>();
            int i = 0, j = 0;
            while (i < aTerms.Length || j < bTerms.Length)
            {
                if (j == bTerms.Length || (i < aTerms.Length && aTerms[i].Id < bTerms[j].Id))
                {
                    onlyA.Add((aCoefs[i], aTerms[i]));
                    i++;
                }
                else if (i == aTerms.Length || bTerms[j].Id < aTerms[i].Id)
                {
                    onlyB.Add((bCoefs[j], bTerms[j]));
                    j++;
                }
                else
                {
                    if (aCoefs[i].Equals(bCoefs[j]))
                    {
                        shared.Add((aCoefs[i], aTerms[i]));
                    }
                    else
                    {
                        onlyA.Add((aCoefs[i], aTerms[i]));
                        onlyB.Add((bCoefs[j], bTerms[j]));
                    }

                    i++;
                    j++;
                }
            }

            ExprValue constant = a0.Equals(b0) ? a0 : V.Zero;
            if (shared.Count > 0 || !V.IsZero(constant))
            {
                Term rest = IfCore(c, Lin(ExprValue.Sub(a0, constant), onlyA), Lin(ExprValue.Sub(b0, constant), onlyB));
                shared.Add((V.One, rest));
                return rest.IsNum ? Lin(constant, shared) : IfCore(c, a, b);
            }
        }

        return IfCore(c, a, b);
    }

    private Term IfCore(Term c, Term a, Term b)
    {
        if (a == b && c.PureBool)
        {
            return a;
        }

        var term = new IfTerm
        {
            C = c,
            A = a,
            B = b,
            Hash = HashCode.Combine(c.Id, a.Id, b.Id) * 31 + 5
        };
        if (TryGet(term, out Term existing))
        {
            return existing;
        }

        term.Support = Bits.Union(Bits.Union(c.Support, a.Support), b.Support);
        term.Kinds = (byte)(a.Kinds | b.Kinds);
        term.OwnFail = !c.IsBool;
        term.CanErr = term.OwnFail || c.CanErr || a.CanErr || b.CanErr;
        term.Vals = UnionVals(a.Vals, b.Vals);
        return Add(term);
    }

    private static ExprValue[]? UnionVals(ExprValue[]? a, ExprValue[]? b)
    {
        if (a is null || b is null)
        {
            return null;
        }

        var union = new HashSet<ExprValue>(a, V.ValueComparer.Instance);
        union.UnionWith(b);
        return union.Count <= MaxVals ? [.. union] : null;
    }

    public Term And(Term a, Term b) => Logic(false, a, b);

    public Term Or(Term a, Term b) => Logic(true, a, b);

    public Term Logic(bool isOr, Term a, Term b)
    {
        if (a is ConstTerm left)
        {
            if (left.IsErr)
            {
                return ErrT;
            }

            if (left.Value.Kind != ExprType.Boolean)
            {
                FailSeen = true;
                return ErrT;
            }

            if (left.Value.BoolValue == isOr)
            {
                return left;
            }

            if (b.IsBool)
            {
                return b;
            }

            if (b is ConstTerm right)
            {
                FailSeen |= !right.IsErr;
                return ErrT;
            }
        }
        else if (b is ConstTerm { IsErr: false, Value.Kind: ExprType.Boolean } decided)
        {
            if (decided.Value.BoolValue == isOr)
            {
                if (a.PureBool)
                {
                    return decided;
                }
            }
            else if (a.IsBool)
            {
                return a;
            }
        }

        if (a == b && a.IsBool)
        {
            return a;
        }

        if (CommonRv([a, b]) is { } rv)
        {
            ExprValue[] table = Rent(rv.Count);
            for (var i = 0; i < table.Length; i++)
            {
                ExprValue x = At(a, i);
                if (x.Kind != ExprType.Boolean)
                {
                    if (!V.IsErr(x))
                    {
                        FailSeen = true;
                    }

                    table[i] = V.Err;
                    continue;
                }

                if (x.BoolValue == isOr)
                {
                    table[i] = x;
                    continue;
                }

                ExprValue y = At(b, i);
                if (y.Kind != ExprType.Boolean)
                {
                    if (!V.IsErr(y))
                    {
                        FailSeen = true;
                    }

                    table[i] = V.Err;
                    continue;
                }

                table[i] = y;
            }

            return Table(rv, table);
        }

        var term = new LogicTerm
        {
            IsOr = isOr,
            A = a,
            B = b,
            Hash = HashCode.Combine(isOr, a.Id, b.Id) * 31 + 6
        };
        if (TryGet(term, out Term existing))
        {
            return existing;
        }

        term.Support = Bits.Union(a.Support, b.Support);
        term.Kinds = V.Bool;
        term.OwnFail = !a.IsBool || !b.IsBool;
        term.CanErr = term.OwnFail || a.CanErr || b.CanErr;
        return Add(term);
    }

    public Term Sel(Term selector, Term[] children)
    {
        if (selector is ConstTerm chosen)
        {
            return chosen.Value.IsInlineInteger && (ulong)chosen.Value.InlineInteger < (ulong)children.Length
                ? children[chosen.Value.InlineInteger]
                : ErrT;
        }

        var same = selector.Pure && selector.Vals is not null;
        for (var i = 1; i < children.Length && same; i++)
        {
            same = children[i] == children[0];
        }

        if (same && InRange(selector, children.Length))
        {
            return children[0];
        }

        if (CommonRv([selector, .. children]) is { } rv)
        {
            ExprValue[] table = Rent(rv.Count);
            for (var i = 0; i < table.Length; i++)
            {
                ExprValue index = At(selector, i);
                table[i] = index.IsInlineInteger && (ulong)index.InlineInteger < (ulong)children.Length
                    ? At(children[index.InlineInteger], i)
                    : V.Err;
            }

            return Table(rv, table);
        }

        var hash = new HashCode();
        hash.Add(selector.Id);
        foreach (Term child in children)
        {
            hash.Add(child.Id);
        }

        var term = new SelTerm
        {
            Selector = selector,
            Children = children,
            Hash = hash.ToHashCode() * 31 + 7
        };
        if (TryGet(term, out Term existing))
        {
            return existing;
        }

        term.Support = selector.Support;
        term.OwnFail = !InRange(selector, children.Length);
        term.CanErr = term.OwnFail || selector.CanErr;
        ExprValue[]? values = [];
        foreach (Term child in children)
        {
            term.Support = Bits.Union(term.Support, child.Support);
            term.Kinds |= child.Kinds;
            term.CanErr |= child.CanErr;
            values = UnionVals(values, child.Vals);
        }

        term.Vals = values;
        return Add(term);
    }

    private static bool InRange(Term selector, int count)
    {
        // A lookup that does not fail yields a position in its table.
        if (selector is OpTerm { Code: Op.Index } index && (index.Aux < 0 ? ~index.Aux : index.Aux) <= count)
        {
            return true;
        }

        if (selector.Vals is not { } values)
        {
            return false;
        }

        foreach (ExprValue value in values)
        {
            if (!value.IsInlineInteger || (ulong)value.InlineInteger >= (ulong)count)
            {
                return false;
            }
        }

        return true;
    }

    // ── Structure access for the inference engine ────────────────────────

    public static void Children(Term term, List<Term> into)
    {
        switch (term)
        {
            case LinTerm lin: into.AddRange(lin.Terms); break;
            case IfTerm conditional:
                into.Add(conditional.C);
                into.Add(conditional.A);
                into.Add(conditional.B);
                break;
            case LogicTerm logic:
                into.Add(logic.A);
                into.Add(logic.B);
                break;
            case SelTerm select:
                into.Add(select.Selector);
                into.AddRange(select.Children);
                break;
            case OpTerm op: into.AddRange(op.Args); break;
        }
    }

    private sealed class Structural : IEqualityComparer<Term>
    {
        public int GetHashCode(Term term) => term.Hash;

        public bool Equals(Term? x, Term? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.Hash != y.Hash)
            {
                return false;
            }

            switch (x, y)
            {
                case (ConstTerm a, ConstTerm b): return a.Value.Equals(b.Value);
                case (TableTerm a, TableTerm b):
                    if (a.Rv != b.Rv)
                    {
                        return false;
                    }

                    for (var i = 0; i < a.Values.Length; i++)
                    {
                        if (!a.Values[i].Equals(b.Values[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                case (LinTerm a, LinTerm b):
                    if (a.Terms.Length != b.Terms.Length || !a.C0.Equals(b.C0))
                    {
                        return false;
                    }

                    for (var i = 0; i < a.Terms.Length; i++)
                    {
                        if (a.Terms[i] != b.Terms[i] || !a.Coefs[i].Equals(b.Coefs[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                case (IfTerm a, IfTerm b): return a.C == b.C && a.A == b.A && a.B == b.B;
                case (LogicTerm a, LogicTerm b): return a.IsOr == b.IsOr && a.A == b.A && a.B == b.B;
                case (SelTerm a, SelTerm b):
                    return a.Selector == b.Selector && a.Children.AsSpan().SequenceEqual(b.Children);
                case (OpTerm a, OpTerm b):
                    if (a.Code != b.Code || a.Aux != b.Aux || !a.Args.AsSpan().SequenceEqual(b.Args))
                    {
                        return false;
                    }

                    if (a.Names is null != b.Names is null ||
                        (a.Names is not null && !a.Names.AsSpan().SequenceEqual(b.Names)))
                    {
                        return false;
                    }

                    return (a.Payload, b.Payload) switch
                    {
                        (null, null) => true,
                        (string s, string t) => s == t,
                        (string[] s, string[] t) => s.AsSpan().SequenceEqual(t),
                        _ => ReferenceEquals(a.Payload, b.Payload)
                    };
                default: return false;
            }
        }
    }
}
