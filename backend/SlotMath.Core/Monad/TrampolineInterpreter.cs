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
);

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
        }

        return count;
    }

    // ── Core implementation ─────────────────────────────────────────────

    private static InterpreterResult<S, T> RunImpl<S, T>(
        Slot<S, T> program,
        S initialState,
        Queue<int>? choices,
        List<InterpreterTrace> trace,
        SeededRandom? rng)
    {
        var state = initialState;
        object current = program;

        // Continuation stack: (value) → next program (as object).
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
                    return new InterpreterResult<S, T>(state, (T)pure.ValueUntyped, trace);

                var cont = stack.Pop();
                current = cont(pure.ValueUntyped);
                continue;
            }

            // ── Draw: weighted choice ─────────────────────────────────
            if (current is IDrawNode draw)
            {
                var weightSet = (WeightSet)draw.WeightsUntyped(state!);

                int choice;
                if (rng != null)
                    choice = rng.Next(System.Math.Max(1, weightSet.Count));
                else if (choices != null && choices.Count > 0)
                    choice = choices.Dequeue() % System.Math.Max(1, weightSet.Count);
                else
                    throw new InvalidOperationException(
                        "Draw evaluation requires a draw choice, but none are available.");

                trace.Add(new InterpreterTrace.DrawRequested(weightSet, choice));
                current = draw.NextUntyped(choice);
                continue;
            }

            // ── GetState: read state, continue ────────────────────────
            if (current is IGetStateNode getState)
            {
                trace.Add(new InterpreterTrace.StateRead(state!));
                current = getState.NextUntyped(state!);
                continue;
            }

            // ── PutState: write state, continue ───────────────────────
            if (current is IPutStateNode putState)
            {
                trace.Add(new InterpreterTrace.StateWritten(putState.ValueUntyped));
                state = (S)putState.ValueUntyped;
                current = putState.NextUntyped;
                continue;
            }

            // ── ModifyState: fused get+put, traced as read + write ─────
            if (current is IModifyStateNode modify)
            {
                trace.Add(new InterpreterTrace.StateRead(state!));
                state = (S)modify.ApplyUntyped(state!);
                trace.Add(new InterpreterTrace.StateWritten(state!));
                current = modify.NextUntyped;
                continue;
            }

            throw new InvalidOperationException(
                $"Unknown Slot variant: {current.GetType().FullName}");
        }
    }
}
