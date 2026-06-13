using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Random;

namespace SlotMath.Core.Monad;

// ═══════════════════════════════════════════════════════════════════════════
//  Free monad over {Pure, Draw, GetState, PutState, FlatMap}
//
//  Generic over user state S.
//  - Pure values
//  - Weighted draws (weights may depend on state)
//  - State read/write
//  - FlatMap: lazy bind (structural, not an effect — enables stack-safe construction)
//  - LINQ query syntax (SelectMany / Select)
//  - Loop (fixpoint) via sugar over the primitives
//
//  Each node implements an internal type-erased interface so the trampoline
//  interpreter can dispatch without pattern-matching on generic type params.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// The free monad over {Pure, Draw, GetState, PutState} — the substrate
/// that both the exact and sampled interpreters consume.
/// </summary>
public abstract class Slot<S, T>
{
    private protected Slot() { }

    /// <summary>Functor map.</summary>
    public Slot<S, U> Select<U>(Func<T, U> f) =>
        SelectMany(a => Slot.Pure<S, U>(f(a)));

    /// <summary>
    /// Monadic bind. Pure short-circuits; everything else wraps in FlatMap
    /// for O(1) construction. The trampoline unwraps FlatMap iteratively.
    /// </summary>
    public Slot<S, U> SelectMany<U>(Func<T, Slot<S, U>> f) =>
        this is Pure<S, T> p
            ? f(p.Value)
            : new FlatMap<S, T, U>(this, f);

    /// <summary>LINQ query-syntax overload.</summary>
    public Slot<S, R> SelectMany<U, R>(Func<T, Slot<S, U>> bind, Func<T, U, R> project) =>
        SelectMany(t => bind(t).Select(u => project(t, u)));
}

// ═══════════════════════════════════════════════════════════════════════════
//  Internal type-erased interfaces (trampoline dispatch without reflection)
// ═══════════════════════════════════════════════════════════════════════════

internal interface IPureNode
{
    object ValueUntyped { get; }
}

internal interface IDrawNode
{
    object WeightsUntyped(object state);
    object NextUntyped(int index);
}

internal interface IGetStateNode
{
    object NextUntyped(object state);
}

internal interface IPutStateNode
{
    object ValueUntyped { get; }
    object NextUntyped { get; }
}

internal interface IModifyStateNode
{
    object ApplyUntyped(object state);
    object NextUntyped { get; }
}

internal interface IFlatMapNode
{
    object SourceUntyped { get; }
    object ApplyUntyped(object value);
}

internal interface IEmitNode
{
    string Label { get; }
    Rational AmountUntyped(object state);
    object NextUntyped { get; }
}

internal interface ILoopNode
{
    bool StopUntyped(object state);
    object BodyUntyped { get; }
    long Cap { get; }
    object DesugarUntyped { get; }
}

// ── Effect nodes ──────────────────────────────────────────────────────

public sealed class Pure<S, T> : Slot<S, T>, IPureNode
{
    public T Value { get; }
    public Pure(T value) => Value = value;
    object IPureNode.ValueUntyped => Value!;
}

public sealed class Draw<S, T> : Slot<S, T>, IDrawNode
{
    public Func<S, WeightSet> Weights { get; }
    public Func<int, Slot<S, T>> Next { get; }

    public Draw(Func<S, WeightSet> weights, Func<int, Slot<S, T>> next)
    {
        Weights = weights;
        Next = next;
    }

    object IDrawNode.WeightsUntyped(object state) => Weights((S)state);
    object IDrawNode.NextUntyped(int index) => Next(index)!;
}

public sealed class GetState<S, T> : Slot<S, T>, IGetStateNode
{
    public Func<S, Slot<S, T>> Next { get; }
    public GetState(Func<S, Slot<S, T>> next) => Next = next;
    object IGetStateNode.NextUntyped(object state) => Next((S)state)!;
}

public sealed class PutState<S, T> : Slot<S, T>, IPutStateNode
{
    public S Value { get; }
    public Slot<S, T> Next { get; }

    public PutState(S value, Slot<S, T> next)
    {
        Value = value;
        Next = next;
    }

    object IPutStateNode.ValueUntyped => Value!;
    object IPutStateNode.NextUntyped => Next!;
}

/// <summary>
/// Fused state update: state ← f(state), then continue with Next.
/// Equivalent to GetState ∘ PutState but evaluated as a single step with
/// no intermediate node allocations — the hot primitive for loop bodies.
/// </summary>
public sealed class ModifyState<S, T> : Slot<S, T>, IModifyStateNode
{
    public Func<S, S> F { get; }
    public Slot<S, T> Next { get; }

    public ModifyState(Func<S, S> f, Slot<S, T> next)
    {
        F = f;
        Next = next;
    }

    object IModifyStateNode.ApplyUntyped(object state) => F((S)state)!;
    object IModifyStateNode.NextUntyped => Next!;
}

/// <summary>
/// Emit a labeled win into the accumulator (PRD v3.1, D13). This is the ONLY
/// way value enters the accumulator (invariant 4). The amount may depend on
/// the recurrence state, but Emit NEVER mutates the recurrence state — it only
/// reads it to compute the amount and then continues unchanged.
/// </summary>
public sealed class Emit<S, T> : Slot<S, T>, IEmitNode
{
    public string Label { get; }
    public Func<S, Rational> Amount { get; }
    public Slot<S, T> Next { get; }

    public Emit(string label, Func<S, Rational> amount, Slot<S, T> next)
    {
        Label = label;
        Amount = amount;
        Next = next;
    }

    Rational IEmitNode.AmountUntyped(object state) => Amount((S)state);
    object IEmitNode.NextUntyped => Next!;
}

/// <summary>
/// First-class fixpoint loop carrying a mandatory iteration cap (D6, invariant 8).
/// Interpreters run the body until the stop condition holds or the cap is
/// reached; cap hits are accounted in provenance (loopCapHits). The exact
/// interpreter desugars this to the self-referential structure it already
/// memoises; the cap there is a finiteness guard backed by the budget (G8).
/// </summary>
public sealed class LoopNode<S> : Slot<S, Unit>, ILoopNode
{
    public Func<S, bool> Stop { get; }
    public Slot<S, Unit> Body { get; }
    public long Cap { get; }

    public LoopNode(Func<S, bool> stop, Slot<S, Unit> body, long cap)
    {
        Stop = stop;
        Body = body;
        Cap = cap;
    }

    /// <summary>The cap-free self-referential desugaring (for the exact path).</summary>
    public Slot<S, Unit> Desugar() => Slot.Loop(Stop, Body);

    bool ILoopNode.StopUntyped(object state) => Stop((S)state);
    object ILoopNode.BodyUntyped => Body!;
    long ILoopNode.Cap => Cap;
    object ILoopNode.DesugarUntyped => Desugar()!;
}

/// <summary>
/// Lazy bind — structural node that makes SelectMany O(1) during construction.
/// </summary>
public sealed class FlatMap<S, A, B> : Slot<S, B>, IFlatMapNode
{
    public Slot<S, A> Source { get; }
    public Func<A, Slot<S, B>> Func { get; }

    public FlatMap(Slot<S, A> source, Func<A, Slot<S, B>> func)
    {
        Source = source;
        Func = func;
    }

    object IFlatMapNode.SourceUntyped => Source!;
    object IFlatMapNode.ApplyUntyped(object value) => Func((A)value)!;
}

/// <summary>
/// Annotation node (G8) — transparent pass-through that carries metadata
/// about a subgraph.  Used to mark plugin-containing subtrees and name
/// subgraphs for regime reporting.  Interpreters unwrap it transparently.
/// </summary>
public sealed class ProgramAnnotation<S, T> : Slot<S, T>, IAnnotationNode
{
    public Slot<S, T> Inner { get; }
    public string? SubgraphId { get; init; }
    public bool ContainsPlugin { get; init; }

    public ProgramAnnotation(Slot<S, T> inner)
    {
        Inner = inner;
    }

    object IAnnotationNode.InnerUntyped => Inner!;
}

internal interface IAnnotationNode
{
    object InnerUntyped { get; }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Static factory methods
// ═══════════════════════════════════════════════════════════════════════════

public static class Slot
{
    public static Slot<S, T> Pure<S, T>(T value) => new Pure<S, T>(value);

    public static Slot<S, int> Draw<S>(Func<S, WeightSet> weights) =>
        new Draw<S, int>(weights, i => Pure<S, int>(i));

    public static Slot<S, T> Draw<S, T>(Func<S, WeightSet> weights, Func<int, T> map) =>
        new Draw<S, T>(weights, i => Pure<S, T>(map(i)));

    /// <summary>
    /// Weighted draw over a fixed set of pre-built outcome programs.
    /// The per-outcome programs are constructed once, so taking a branch
    /// allocates nothing and branch program identity is stable — which both
    /// interpreters exploit.
    /// </summary>
    public static Slot<S, T> DrawFrom<S, T>(Func<S, WeightSet> weights, Slot<S, T>[] outcomes) =>
        new Draw<S, T>(weights, i => outcomes[i]);

    public static Slot<S, S> GetState<S>() =>
        new GetState<S, S>(s => Pure<S, S>(s));

    public static Slot<S, T> GetState<S, T>(Func<S, T> f) =>
        new GetState<S, T>(s => Pure<S, T>(f(s)));

    public static Slot<S, Unit> PutState<S>(S value) =>
        new PutState<S, Unit>(value, Pure<S, Unit>(Unit.Value));

    public static Slot<S, Unit> Modify<S>(Func<S, S> f) =>
        new ModifyState<S, Unit>(f, PureUnitCache<S>.Instance);

    /// <summary>
    /// Emit a labeled win into the accumulator (D13). The sole payout generator;
    /// the win accumulator is held separate from the recurrence state (invariant 4).
    /// </summary>
    public static Slot<S, Unit> Emit<S>(string label, Rational amount) =>
        new Emit<S, Unit>(label, _ => amount, PureUnitCache<S>.Instance);

    /// <summary>Emit a labeled win whose amount is computed from the current state.</summary>
    public static Slot<S, Unit> Emit<S>(string label, Func<S, Rational> amount) =>
        new Emit<S, Unit>(label, amount, PureUnitCache<S>.Instance);

    private static class PureUnitCache<S>
    {
        public static readonly Slot<S, Unit> Instance = new Pure<S, Unit>(Unit.Value);
    }

    /// <summary>
    /// Fixpoint loop: run <paramref name="body"/> until <paramref name="stopCondition"/>
    /// holds on the current state.
    ///
    /// The loop is built as a self-referential structure: a single GetState node
    /// and a single FlatMap node are shared by every iteration.  Stable node
    /// identity is what lets the exact interpreter memoise recurrent
    /// (state, continuation) pairs across iterations and collapse the
    /// exponential iteration tree into a DAG.
    /// </summary>
    public static Slot<S, Unit> Loop<S>(Func<S, bool> stopCondition, Slot<S, Unit> body)
    {
        var exit = Pure<S, Unit>(Unit.Value);
        Slot<S, Unit>? loop = null;
        // Built explicitly (not via SelectMany) so construction is deferred even
        // when body is a Pure — the lambda must not run while loop is still null.
        var iterate = new FlatMap<S, Unit, Unit>(body, _ => loop!);
        loop = new GetState<S, Unit>(s => stopCondition(s) ? exit : iterate);
        return loop;
    }

    /// <summary>
    /// Fixpoint loop with a mandatory iteration cap (D6, invariant 8). The body
    /// runs until <paramref name="stopCondition"/> holds or <paramref name="cap"/>
    /// iterations have run, whichever comes first. The cap is game semantics, not
    /// approximation — cap hits are accounted in provenance (loopCapHits).
    /// </summary>
    public static Slot<S, Unit> Loop<S>(Func<S, bool> stopCondition, Slot<S, Unit> body, long cap)
    {
        if (cap <= 0)
            throw new ArgumentOutOfRangeException(nameof(cap), "Loop iteration cap must be positive (D6).");
        if (cap > SlotMathConstants.Loop.CapMax)
            throw new ArgumentOutOfRangeException(
                nameof(cap), $"Loop iteration cap must not exceed {SlotMathConstants.Loop.CapMax} (D6).");
        return new LoopNode<S>(stopCondition, body, cap);
    }

    public static Slot<S, T> Branch<S, T>(
        Func<S, bool> condition, Slot<S, T> thenBranch, Slot<S, T> elseBranch) =>
        GetState<S>().SelectMany(s => condition(s) ? thenBranch : elseBranch);

    public static Slot<S, T> Branch<S, T>(
        bool condition, Slot<S, T> thenBranch, Slot<S, T> elseBranch) =>
        condition ? thenBranch : elseBranch;

    public static Slot<S, Unit> UnitSlot<S>() => Pure<S, Unit>(Unit.Value);

    /// <summary>
    /// Wrap a sub-program with annotation metadata (G8).
    /// Does not change runtime behavior — the interpreters treat it as transparent.
    /// </summary>
    public static Slot<S, T> Annotate<S, T>(Slot<S, T> inner,
        string? subgraphId = null, bool containsPlugin = false) =>
        new ProgramAnnotation<S, T>(inner)
        {
            SubgraphId = subgraphId,
            ContainsPlugin = containsPlugin
        };
}

// ═══════════════════════════════════════════════════════════════════════════

public readonly struct Unit : IEquatable<Unit>
{
    public static Unit Value => default;
    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public override string ToString() => "()";
}
