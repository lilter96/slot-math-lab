using SlotMath.Core.Math;
using SlotMath.Core.Random;

namespace SlotMath.Core.Monad;

// ═══════════════════════════════════════════════════════════════════════════
//  Interpreter trace — records effects for testing monad laws
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>A single effect step observed during interpretation.</summary>
public abstract record InterpreterTrace
{
    public sealed record DrawRequested(WeightSet Weights, int ChosenIndex) : InterpreterTrace;
    public sealed record StateRead(object State) : InterpreterTrace;
    public sealed record StateWritten(object NewState) : InterpreterTrace;
}

/// <summary>Result of interpreting a Slot program.</summary>
public sealed record InterpreterResult<S, T>(
    S FinalState,
    T Value,
    IReadOnlyList<InterpreterTrace> Trace
)
{
    private static readonly IReadOnlyDictionary<string, Rational> EmptyEmits =
        new Dictionary<string, Rational>();

    /// <summary>
    /// Per-label emitted win totals (D13). Empty when the program does not Emit.
    /// </summary>
    public IReadOnlyDictionary<string, Rational> Emits { get; init; } = EmptyEmits;

    /// <summary>Total emitted win across all labels (D13). Zero when nothing is emitted.</summary>
    public Rational TotalWin { get; init; } = Rational.Zero;

    /// <summary>True when a <see cref="LoopNode{S}"/> hit its iteration cap (D6).</summary>
    public bool LoopCapHit { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Trampoline interpreter — stack-safe evaluation
//
//  Dispatches via the internal type-erased interfaces (IPureNode, IDrawNode,
//  IGetStateNode, IPutStateNode, IFlatMapNode) so the interpreter never
//  pattern-matches on generic type parameters and never uses reflection/dynamic.
//
//  A continuation stack of Func&lt;object, object&gt; handles FlatMap unwind.
//  Each iteration of the trampoline processes exactly one node, keeping the
//  call-stack depth constant regardless of program depth or loop iterations.
// ═══════════════════════════════════════════════════════════════════════════

public static class TrampolineInterpreter
{
    /// <summary>
    /// Run a program with explicit draw choices (for deterministic testing).
    /// </summary>
    public static InterpreterResult<S, T> Run<S, T>(
        Slot<S, T> program,
        S initialState,
        Queue<int>? drawChoices = null)
    {
        var choices = drawChoices ?? new Queue<int>();
        var trace = new List<InterpreterTrace>();
        return RunImpl(program, initialState, choices, trace, rng: null);
    }

    /// <summary>
    /// Run a program with a fixed seed for draw decisions.
    /// </summary>
    public static InterpreterResult<S, T> RunWithSeed<S, T>(
        Slot<S, T> program,
        S initialState,
        long seed)
    {
        var rng = new SeededRandom(seed);
        var trace = new List<InterpreterTrace>();
        return RunImpl(program, initialState, choices: null, trace, rng);
    }

    /// <summary>
    /// Count the number of Draw nodes reachable from the program root (estimate).
    /// </summary>
    public static int CountDraws<S, T>(Slot<S, T> program)
    {
        var count = 0;
        var queue = new Queue<object>();
        var visited = new HashSet<object>();

        queue.Enqueue(program);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (!visited.Add(p)) continue;

            if (p is IFlatMapNode fm)
            {
                queue.Enqueue(fm.SourceUntyped);
            }
            else if (p is IAnnotationNode ann)
            {
                queue.Enqueue(ann.InnerUntyped);
            }
            else if (p is IDrawNode d)
            {
                count++;
                var ws = (WeightSet)d.WeightsUntyped(null!);
                for (var i = 0; i < ws.Count; i++)
                    queue.Enqueue(d.NextUntyped(i));
            }
            else if (p is IGetStateNode g)
            {
                queue.Enqueue(g.NextUntyped(null!));
            }
            else if (p is IPutStateNode ps)
            {
                queue.Enqueue(ps.NextUntyped);
            }
            else if (p is IModifyStateNode ms)
            {
                queue.Enqueue(ms.NextUntyped);
            }
            else if (p is IEmitNode em)
            {
                queue.Enqueue(em.NextUntyped);
            }
            else if (p is ILoopNode loop)
            {
                queue.Enqueue(loop.BodyUntyped);
            }
        }

        return count;
    }

    // ── Core implementation ─────────────────────────────────────────────

    /// <summary>
    /// Shared, mutable evaluation context. Draws, state, traces, and the win
    /// accumulator (D13) all live here so a loop body — run as a sub-program —
    /// threads them through transparently.
    /// </summary>
    private sealed class RunCtx<S>
    {
        public required S State;
        public required List<InterpreterTrace> Trace;
        public Queue<int>? Choices;
        public SeededRandom? Rng;
        public readonly Dictionary<string, Rational> Emits = new();
        public Rational Total = Rational.Zero;
        public bool LoopCapHit;

        public void AddEmit(string label, Rational amount)
        {
            Emits[label] = Emits.TryGetValue(label, out var cur) ? cur + amount : amount;
            Total += amount;
        }

        public int DrawChoice(int count)
        {
            var max = System.Math.Max(1, count);
            if (Rng != null)
                return Rng.Next(max);
            if (Choices != null && Choices.Count > 0)
                return Choices.Dequeue() % max;
            throw new InvalidOperationException(
                "Draw evaluation requires a draw choice, but none are available.");
        }
    }

    private static InterpreterResult<S, T> RunImpl<S, T>(
        Slot<S, T> program,
        S initialState,
        Queue<int>? choices,
        List<InterpreterTrace> trace,
        SeededRandom? rng)
    {
        var ctx = new RunCtx<S> { State = initialState, Trace = trace, Choices = choices, Rng = rng };
        var value = RunProgram<S, T>(program, ctx);
        return new InterpreterResult<S, T>(ctx.State, value, trace)
        {
            Emits = ctx.Emits,
            TotalWin = ctx.Total,
            LoopCapHit = ctx.LoopCapHit,
        };
    }

    /// <summary>
    /// Run a (sub-)program to its terminal value, threading the shared context.
    /// Draw chains are handled iteratively (constant call-stack depth); only a
    /// loop body recurses, so call-stack depth is bounded by loop nesting, never
    /// by iteration count or draw-chain length.
    /// </summary>
    private static T RunProgram<S, T>(object program, RunCtx<S> ctx)
    {
        object current = program;
        var stack = new Stack<Func<object, object>>();

        while (true)
        {
            // ── FlatMap + Annotation: unwind both until stable ──────
            bool unwound;
            do
            {
                unwound = false;
                if (current is IFlatMapNode fm)
                {
                    stack.Push(v => fm.ApplyUntyped(v));
                    current = fm.SourceUntyped;
                    unwound = true;
                }
                if (current is IAnnotationNode ann)
                {
                    current = ann.InnerUntyped;
                    unwound = true;
                }
            } while (unwound);

            // ── Pure: terminal value ──────────────────────────────────
            if (current is IPureNode pure)
            {
                if (stack.Count == 0)
                    return (T)pure.ValueUntyped;

                var cont = stack.Pop();
                current = cont(pure.ValueUntyped);
                continue;
            }

            // ── Draw: weighted choice ─────────────────────────────────
            if (current is IDrawNode draw)
            {
                var weightSet = (WeightSet)draw.WeightsUntyped(ctx.State!);
                var choice = ctx.DrawChoice(weightSet.Count);
                ctx.Trace.Add(new InterpreterTrace.DrawRequested(weightSet, choice));
                current = draw.NextUntyped(choice);
                continue;
            }

            // ── GetState: read state, continue ────────────────────────
            if (current is IGetStateNode getState)
            {
                ctx.Trace.Add(new InterpreterTrace.StateRead(ctx.State!));
                current = getState.NextUntyped(ctx.State!);
                continue;
            }

            // ── PutState: write state, continue ───────────────────────
            if (current is IPutStateNode putState)
            {
                ctx.Trace.Add(new InterpreterTrace.StateWritten(putState.ValueUntyped));
                ctx.State = (S)putState.ValueUntyped;
                current = putState.NextUntyped;
                continue;
            }

            // ── ModifyState: fused get+put, traced as read + write ─────
            if (current is IModifyStateNode modify)
            {
                ctx.Trace.Add(new InterpreterTrace.StateRead(ctx.State!));
                ctx.State = (S)modify.ApplyUntyped(ctx.State!);
                ctx.Trace.Add(new InterpreterTrace.StateWritten(ctx.State!));
                current = modify.NextUntyped;
                continue;
            }

            // ── Emit: labeled win into the accumulator (D13) ───────────
            if (current is IEmitNode emit)
            {
                ctx.AddEmit(emit.Label, emit.AmountUntyped(ctx.State!));
                current = emit.NextUntyped;
                continue;
            }

            // ── Truncate: end the path (D6 bounded unrolling) ──────────
            if (current is ITruncateNode)
            {
                ctx.LoopCapHit = true;
                return (T)(object)Unit.Value;
            }

            // ── Loop: run body until stop or cap (D6) ──────────────────
            if (current is ILoopNode loop)
            {
                long iter = 0;
                while (!loop.StopUntyped(ctx.State!) && iter < loop.Cap)
                {
                    RunProgram<S, Unit>(loop.BodyUntyped, ctx);
                    iter++;
                }
                if (iter >= loop.Cap && !loop.StopUntyped(ctx.State!))
                    ctx.LoopCapHit = true;

                if (stack.Count == 0)
                    return (T)(object)Unit.Value;
                var cont = stack.Pop();
                current = cont(Unit.Value);
                continue;
            }

            throw new InvalidOperationException(
                $"Unknown Slot variant: {current.GetType().FullName}");
        }
    }
}
