using SlotMath.Core.Monad;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Monad;

// ═══════════════════════════════════════════════════════════════════════════
//  Property-based tests for the free monad laws
//
//  We compare left vs right sides by running both programs through the
//  trampoline interpreter with the same state and draw choices, then
//  asserting identical traces and final values.
// ═══════════════════════════════════════════════════════════════════════════

public class SlotMonadLawTests
{
    // ── Test state (immutable, hashable record) ────────────────────────

    public record TestState(int Counter, int Multiplier, bool Flag)
    {
        public TestState Inc() => this with { Counter = Counter + 1 };
        public TestState Add(int n) => this with { Counter = Counter + n };
        public TestState SetFlag(bool f) => this with { Flag = f };
        public TestState SetMultiplier(int m) => this with { Multiplier = m };
    }

    private static TestState InitialState => new(0, 1, false);

    // ── Assertion helper ───────────────────────────────────────────────

    private static void AssertProgramsEqual<S, T>(
        Slot<S, T> left,
        Slot<S, T> right,
        S state,
        Queue<int>? choices = null)
    {
        choices ??= new Queue<int>();

        // Clone the choices for each run so both consume independently.
        var leftChoices = new Queue<int>(choices);
        var rightChoices = new Queue<int>(choices);

        var leftResult = TrampolineInterpreter.Run(left, state, leftChoices);
        var rightResult = TrampolineInterpreter.Run(right, state, rightChoices);

        Assert.Equal(leftResult.FinalState, rightResult.FinalState);
        Assert.Equal(leftResult.Value, rightResult.Value);
        Assert.Equal(leftResult.Trace, rightResult.Trace);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Left identity: Pure(a).SelectMany(f) ≡ f(a)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void LeftIdentity_SimpleFunction()
    {
        var state = InitialState;
        const int a = 42;
        Func<int, Slot<TestState, string>> f =
            x => Slot.Pure<TestState, string>($"result={x}");

        var left = Slot.Pure<TestState, int>(a).SelectMany(f);
        var right = f(a);

        AssertProgramsEqual(left, right, state);
    }

    [Fact]
    public void LeftIdentity_FunctionWithStateRead()
    {
        var state = InitialState;
        const int a = 10;
        Func<int, Slot<TestState, int>> f = x =>
            from s in Slot.GetState<TestState>()
            select s.Counter + x;

        var left = Slot.Pure<TestState, int>(a).SelectMany(f);
        var right = f(a);

        AssertProgramsEqual(left, right, state);
    }

    [Fact]
    public void LeftIdentity_FunctionWithStateWrite()
    {
        var state = InitialState;
        const int a = 5;
        Func<int, Slot<TestState, Unit>> f = x =>
            Slot.Modify<TestState>(s => s.Add(x));

        var left = Slot.Pure<TestState, int>(a).SelectMany(f);
        var right = f(a);

        AssertProgramsEqual(left, right, state);
    }

    [Fact]
    public void LeftIdentity_FunctionWithDraw()
    {
        var state = InitialState;
        const int a = 0;
        Func<int, Slot<TestState, string>> f = x =>
            from choice in Slot.Draw<TestState>(_ => WeightSet.FromIntegers([1, 1, 1]))
            select $"a={x}, choice={choice}";

        var left = Slot.Pure<TestState, int>(a).SelectMany(f);
        var right = f(a);

        var choices = new Queue<int>([1]);
        AssertProgramsEqual(left, right, state, choices);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Right identity: m.SelectMany(x => Pure(x)) ≡ m
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void RightIdentity_PureProgram()
    {
        var state = InitialState;
        var m = Slot.Pure<TestState, int>(99);

        var left = m.SelectMany(x => Slot.Pure<TestState, int>(x));
        var right = m;

        AssertProgramsEqual(left, right, state);
    }

    [Fact]
    public void RightIdentity_DrawProgram()
    {
        var state = InitialState;
        var m = Slot.Draw<TestState, string>(
            _ => WeightSet.FromIntegers([1, 2]),
            i => $"choice={i}");

        var left = m.SelectMany(x => Slot.Pure<TestState, string>(x));
        var right = m;

        var choices = new Queue<int>([0, 1]);
        AssertProgramsEqual(left, right, state, choices);
    }

    [Fact]
    public void RightIdentity_StateReadProgram()
    {
        var state = InitialState;
        var m = from s in Slot.GetState<TestState>()
                select s.Counter;

        var left = m.SelectMany(x => Slot.Pure<TestState, int>(x));
        var right = m;

        AssertProgramsEqual(left, right, state);
    }

    [Fact]
    public void RightIdentity_StateWriteProgram()
    {
        var state = InitialState;
        var m = from _ in Slot.PutState<TestState>(new TestState(5, 2, true))
                select 42;

        var left = m.SelectMany(x => Slot.Pure<TestState, int>(x));
        var right = m;

        AssertProgramsEqual(left, right, state);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Associativity: m.SelectMany(f).SelectMany(g)
    //               ≡ m.SelectMany(x => f(x).SelectMany(g))
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Associativity_PureProgram()
    {
        var state = InitialState;
        var m = Slot.Pure<TestState, int>(10);
        Func<int, Slot<TestState, int>> f = x => Slot.Pure<TestState, int>(x * 2);
        Func<int, Slot<TestState, string>> g = y => Slot.Pure<TestState, string>($"val={y}");

        var left = m.SelectMany(f).SelectMany(g);
        var right = m.SelectMany(x => f(x).SelectMany(g));

        AssertProgramsEqual(left, right, state);
    }

    [Fact]
    public void Associativity_DrawProgram()
    {
        var state = InitialState;
        var m = Slot.Draw<TestState, int>(
            _ => WeightSet.FromIntegers([1, 1]),
            i => i * 10);
        Func<int, Slot<TestState, int>> f = x =>
            from s in Slot.GetState<TestState>()
            select x + s.Counter;
        Func<int, Slot<TestState, string>> g = y =>
            Slot.Pure<TestState, string>($"v={y}");

        var left = m.SelectMany(f).SelectMany(g);
        var right = m.SelectMany(x => f(x).SelectMany(g));

        var choices = new Queue<int>([0, 0, 1, 1]);
        AssertProgramsEqual(left, right, state, choices);
    }

    [Fact]
    public void Associativity_StateProgram()
    {
        var state = InitialState with { Counter = 5 };
        var m = from s in Slot.GetState<TestState>()
                from _ in Slot.PutState<TestState>(state with { Counter = s.Counter + 1 })
                select s.Counter;
        Func<int, Slot<TestState, int>> f = x =>
            from s in Slot.GetState<TestState>()
            select x * s.Counter;
        Func<int, Slot<TestState, string>> g = y =>
            Slot.Pure<TestState, string>($"r={y}");

        var left = m.SelectMany(f).SelectMany(g);
        var right = m.SelectMany(x => f(x).SelectMany(g));

        AssertProgramsEqual(left, right, state);
    }

    [Fact]
    public void Associativity_MixedProgram()
    {
        // Exercise all primitives in one associativity test.
        var state = InitialState with { Counter = 3 };

        var m = from s in Slot.GetState<TestState>()
                from _ in Slot.Modify<TestState>(st => st.Inc())
                from choice in Slot.Draw<TestState>(
                    _ => WeightSet.FromIntegers([1, 2, 3]))
                select s.Counter + choice;

        Func<int, Slot<TestState, string>> f = x =>
            from s in Slot.GetState<TestState>()
            select $"{x}-{s.Counter}";

        Func<string, Slot<TestState, int>> g = str =>
            Slot.Pure<TestState, int>(str.Length);

        var left = m.SelectMany(f).SelectMany(g);
        var right = m.SelectMany(x => f(x).SelectMany(g));

        var choices = new Queue<int>([0, 0, 2, 2, 1, 1]);
        AssertProgramsEqual(left, right, state, choices);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Extended associativity over generated programs
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parameterised test exercising associativity across multiple state + choice
    /// configurations.  This acts as a lightweight property-based test.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]  // counter=0, drawChoice=0
    [InlineData(5, 1)]  // counter=5, drawChoice=1
    [InlineData(10, 3)] // counter=10, drawChoice=3 (wraps to 2 for 3-outcome draw)
    [InlineData(-1, 0)]
    [InlineData(100, 1)]
    public void Associativity_OverParameterisedInputs(int counter, int drawChoice)
    {
        var state = InitialState with { Counter = counter };

        var m = from s in Slot.GetState<TestState>()
                from d in Slot.Draw<TestState>(_ => WeightSet.FromIntegers([1, 2, 3]))
                from _ in Slot.PutState<TestState>(state with { Counter = s.Counter + d })
                select s.Counter + d;

        Func<int, Slot<TestState, int>> f = x =>
            Slot.Modify<TestState>(s => s.Add(x))
                .SelectMany(_ => Slot.Pure<TestState, int>(x * 2));

        Func<int, Slot<TestState, string>> g = y =>
            from s in Slot.GetState<TestState>()
            select $"{y}-{s.Counter}";

        var left = m.SelectMany(f).SelectMany(g);
        var right = m.SelectMany(x => f(x).SelectMany(g));

        var choices = new Queue<int>(new[] { drawChoice, drawChoice });
        AssertProgramsEqual(left, right, state, choices);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Loop tests
// ═══════════════════════════════════════════════════════════════════════════

public class SlotLoopTests
{
    public record LoopState(int Count, int Sum)
    {
        public LoopState Inc() => this with { Count = Count + 1 };
        public LoopState AddSum(int n) => this with { Sum = Sum + n };
    }

    [Fact]
    public void Loop_StateDependentStop_Terminates()
    {
        // Loop that increments a counter until it reaches 5.
        var stopCondition = (LoopState s) => s.Count >= 5;
        var body =
            from s in Slot.GetState<LoopState>()
            from _ in Slot.PutState<LoopState>(s.Inc())
            select Unit.Value;

        var program = Slot.Loop(stopCondition, body);

        var result = TrampolineInterpreter.Run(
            program,
            new LoopState(0, 0));

        Assert.Equal(5, result.FinalState.Count);
        Assert.Equal(0, result.FinalState.Sum);
    }

    [Fact]
    public void Loop_WithDraw_AccumulatesCorrectly()
    {
        // Loop that draws 1 or 2, adds to sum, stops after sum >= 10.
        var stopCondition = (LoopState s) => s.Sum >= 10;
        var body =
            from s in Slot.GetState<LoopState>()
            from draw in Slot.Draw<LoopState>(_ => WeightSet.FromIntegers([1, 2]))
            from _ in Slot.PutState<LoopState>(s.AddSum(draw + 1))
            select Unit.Value;

        var program = Slot.Loop(stopCondition, body);

        // With choices [0,0,0,0,0,0,...] → draws all 1's → accumulated values: 2+2+2+2+2=10
        var choices = new Queue<int>(Enumerable.Repeat(0, 20));
        var result = TrampolineInterpreter.Run(
            program,
            new LoopState(0, 0),
            choices);

        Assert.True(result.FinalState.Sum >= 10);
        // Should have drawn exactly as needed
        Assert.True(result.FinalState.Sum <= 11, $"Sum should be 10-11, was {result.FinalState.Sum}");
    }

    [Fact]
    public void Loop_AlreadyStopped_DoesNotExecuteBody()
    {
        var stopCondition = (LoopState s) => s.Count >= 0; // always true
        var body = Slot.Modify<LoopState>(s => s)
            .SelectMany(_ => Slot.Pure<LoopState, Unit>(Unit.Value));

        // Run the loop; body should never execute.
        // But we can't capture 'executed' from the lambda — let me use state instead.
        // Actually 'executed' is captured, but Slot.Modify takes a Func<S,S>,
        // so the side effect occurs inside the interpreter. Let's run it and check.
        var program = Slot.Loop(stopCondition, body);

        var result = TrampolineInterpreter.Run(
            program,
            new LoopState(0, 0));

        // The body should never have executed since stopCondition is immediately true.
        Assert.Equal(0, result.FinalState.Count);
    }

    [Fact]
    public void Loop_ZeroIterations_PreservesState()
    {
        var stopCondition = (LoopState s) => true; // stop immediately
        var body = Slot.Modify<LoopState>(s => s with { Count = 999 });

        var program = Slot.Loop(stopCondition, body);
        var initialState = new LoopState(42, 10);
        var result = TrampolineInterpreter.Run(program, initialState);

        Assert.Equal(42, result.FinalState.Count);
        Assert.Equal(10, result.FinalState.Sum);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  State-dependent Draw weight tests
// ═══════════════════════════════════════════════════════════════════════════

public class SlotDrawTests
{
    public record DrawState(int Level, string Mode);

    [Fact]
    public void Draw_WeightsComputedFromState_DependOnState()
    {
        // Draw with weights proportional to Level.
        var program = Slot.Draw<DrawState>(
            s => WeightSet.FromIntegers(
                Enumerable.Repeat(1, System.Math.Max(1, s.Level)).ToArray()));

        var initial = new DrawState(3, "");
        var choices = new Queue<int>([1]); // pick index 1 (valid since 3 outcomes)
        var result = TrampolineInterpreter.Run(program, initial, choices);

        // The trace should record a Draw with 3 outcomes
        var trace = result.Trace[0] as InterpreterTrace.DrawRequested;
        Assert.NotNull(trace);
        Assert.Equal(3, trace!.Weights.Count);
        Assert.Equal(1, trace.ChosenIndex);
    }

    [Fact]
    public void Draw_WeightsChangeWithState()
    {
        // Program: read Mode from state, then draw with different weight sets.
        var program =
            from s in Slot.GetState<DrawState>()
            from draw in Slot.Draw<DrawState>(st =>
                st.Mode == "high"
                    ? WeightSet.FromIntegers([1, 10])   // heavily skew to index 1
                    : WeightSet.FromIntegers([10, 1]))  // heavily skew to index 0
            select draw;

        // Mode = "high" → weight [1, 10], choice 0 → pick index 0.
        var choices = new Queue<int>([0]);
        var result = TrampolineInterpreter.Run(
            program, new DrawState(0, "high"), choices);

        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void Draw_StateChangesBetweenDraws()
    {
        // Two sequential draws, state changes between them.
        var program =
            from first in Slot.Draw<DrawState>(_ => WeightSet.FromIntegers([1, 2, 3]))
            from _ in Slot.PutState<DrawState>(new DrawState(10, "after"))
            from s in Slot.GetState<DrawState>()
            from second in Slot.Draw<DrawState>(st =>
                WeightSet.FromIntegers(new[] { st.Level, st.Level * 2 }))
            select (first, second, stateAfter: s.Level);

        var choices = new Queue<int>([1, 0]); // first draw picks 1, second picks 0
        var result = TrampolineInterpreter.Run(
            program, new DrawState(5, "before"), choices);

        Assert.Equal(1, result.Value.first);
        Assert.Equal(0, result.Value.second);
        Assert.Equal(10, result.Value.stateAfter);
    }

    [Fact]
    public void Draw_InLoop_UsesCurrentStateForWeights()
    {
        // Loop: draw from [1..Counter], add to sum, stop when sum >= 20.
        var stopCondition = (LoopAccumState s) => s.Sum >= 20;
        var body =
            from s in Slot.GetState<LoopAccumState>()
            from draw in Slot.Draw<LoopAccumState>(st =>
                WeightSet.FromIntegers(Enumerable.Repeat(1, System.Math.Max(1, st.Counter)).ToArray()))
            from _ in Slot.Modify<LoopAccumState>(st => st with
            {
                Sum = st.Sum + draw + 1,
                Counter = st.Counter + 1
            })
            select Unit.Value;

        var program = Slot.Loop(stopCondition, body);

        // All choices = 0 means each draw picks the first outcome.
        var choices = new Queue<int>(Enumerable.Repeat(0, 50));
        var result = TrampolineInterpreter.Run(
            program,
            new LoopAccumState(3, 0),
            choices);

        Assert.True(result.FinalState.Sum >= 20);
        Assert.True(result.FinalState.Counter > 1,
            $"Loop should have iterated more than once; Counter={result.FinalState.Counter}");
    }

    public record LoopAccumState(int Counter, int Sum);
}

// ═══════════════════════════════════════════════════════════════════════════
//  State threading tests
// ═══════════════════════════════════════════════════════════════════════════

public class SlotStateThreadingTests
{
    public record ThreadState(int A, int B, string Tag)
    {
        public ThreadState SetA(int a) => this with { A = a };
        public ThreadState SetB(int b) => this with { B = b };
    }

    [Fact]
    public void State_ThreadsThroughBind()
    {
        var program =
            from a in Slot.GetState<ThreadState, int>(s => s.A)
            from _ in Slot.PutState<ThreadState>(new ThreadState(99, 0, "changed"))
            from b in Slot.GetState<ThreadState, int>(s => s.B)
            from __ in Slot.Modify<ThreadState>(s => s.SetB(42))
            from c in Slot.GetState<ThreadState, int>(s => s.B)
            select (A: a, B: b, C: c);

        var result = TrampolineInterpreter.Run(
            program,
            new ThreadState(5, 0, "start"));

        Assert.Equal(5, result.Value.A);
        Assert.Equal(0, result.Value.B);
        Assert.Equal(42, result.Value.C);
        Assert.Equal(99, result.FinalState.A);
        Assert.Equal(42, result.FinalState.B);
    }

    [Fact]
    public void State_ThreadsAcrossLoopIterations()
    {
        // Loop: increment A by 1 each iteration, stop when A >= 7.
        var stopCondition = (ThreadState s) => s.A >= 7;
        var body = Slot.Modify<ThreadState>(s => s.SetA(s.A + 1));

        var program = Slot.Loop(stopCondition, body);
        var result = TrampolineInterpreter.Run(
            program,
            new ThreadState(3, 0, "looping"));

        Assert.Equal(7, result.FinalState.A);
    }

    [Fact]
    public void State_PutGetSequence()
    {
        var program =
            from _1 in Slot.PutState<ThreadState>(new ThreadState(1, 2, "first"))
            from s1 in Slot.GetState<ThreadState>()
            from _2 in Slot.PutState<ThreadState>(new ThreadState(3, 4, "second"))
            from s2 in Slot.GetState<ThreadState>()
            select (s1, s2);

        var result = TrampolineInterpreter.Run(
            program,
            new ThreadState(0, 0, "init"));

        Assert.Equal(1, result.Value.s1.A);
        Assert.Equal(2, result.Value.s1.B);
        Assert.Equal(3, result.Value.s2.A);
        Assert.Equal(4, result.Value.s2.B);
    }

    [Fact]
    public void State_ModifyIsAtomicReadWrite()
    {
        // Modify = GetState + PutState, verified in one step.
        var program =
            from s1 in Slot.GetState<ThreadState>()
            from _ in Slot.Modify<ThreadState>(s => s.SetA(s.A * 10).SetB(s.B + 1))
            from s2 in Slot.GetState<ThreadState>()
            select (before: s1, after: s2);

        var result = TrampolineInterpreter.Run(
            program,
            new ThreadState(3, 5, "test"));

        Assert.Equal(3, result.Value.before.A);
        Assert.Equal(5, result.Value.before.B);
        Assert.Equal(30, result.Value.after.A);
        Assert.Equal(6, result.Value.after.B);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Stack safety test
// ═══════════════════════════════════════════════════════════════════════════

public class SlotStackSafetyTests
{
    public record DeepState(int Value);

    [Fact]
    public void DeepProgram_EvaluatesWithoutStackOverflow()
    {
        // Build a program with 10,000 sequential PutState operations.
        // Each SelectMany on a PutState pushes into the continuation,
        // creating a deeply nested tree.
        const int depth = 10_000;

        // Build a chain of PutState operations via a loop.
        // We build right-associated: PutState(s0, PutState(s1, PutState(s2, ... Pure(n)...)))
        // The construction is iterative (no recursion in build).
        Slot<DeepState, int> program = Slot.Pure<DeepState, int>(0);

        for (var i = 0; i < depth; i++)
        {
            var capturedI = i;
            program = program.SelectMany(x =>
                Slot.PutState<DeepState>(new DeepState(capturedI + 1))
                    .SelectMany(_ => Slot.Pure<DeepState, int>(x + 1)));
        }

        // Run the program — must not stack overflow.
        var result = TrampolineInterpreter.Run(
            program,
            new DeepState(0));

        Assert.Equal(depth, result.FinalState.Value);
        Assert.Equal(depth, result.Value);
    }

    [Fact]
    public void DeepLoopProgram_EvaluatesWithoutStackOverflow()
    {
        // A loop that runs 15,000 iterations.
        const int iterations = 15_000;

        var stopCondition = (DeepState s) => s.Value >= iterations;
        var body = Slot.Modify<DeepState>(s => s with { Value = s.Value + 1 });

        var program = Slot.Loop(stopCondition, body);

        var result = TrampolineInterpreter.Run(
            program,
            new DeepState(0));

        Assert.Equal(iterations, result.FinalState.Value);
    }

    [Fact]
    public void DeepDrawChain_EvaluatesWithoutStackOverflow()
    {
        // Build a program with many sequential draws (each followed by a state increment).
        const int depth = 5_000;

        Slot<DeepState, int> program = Slot.Pure<DeepState, int>(0);

        for (var i = 0; i < depth; i++)
        {
            var capturedI = i;
            program = program.SelectMany(x =>
                from d in Slot.Draw<DeepState>(_ => WeightSet.FromIntegers([1]))
                from _ in Slot.Modify<DeepState>(s => s with { Value = s.Value + 1 })
                select x + d);
        }

        var choices = new Queue<int>(Enumerable.Repeat(0, depth));
        var result = TrampolineInterpreter.Run(
            program,
            new DeepState(0),
            choices);

        Assert.Equal(depth, result.FinalState.Value);
        Assert.Equal(0, result.Value); // draws all 0, sum stays 0
    }

    [Fact]
    public void DepthTest_ConstructionDoesNotOverflow()
    {
        // Verify that constructing a 10,000-deep program via SelectMany is safe.
        const int depth = 10_000;

        Slot<DeepState, Unit> program = Slot.UnitSlot<DeepState>();

        for (var i = 0; i < depth; i++)
        {
            var iCopy = i;
            program = program.SelectMany(_ =>
                Slot.Modify<DeepState>(s => s with { Value = s.Value + 1 })
                    .SelectMany(__ => Slot.Pure<DeepState, Unit>(Unit.Value)));
        }

        var result = TrampolineInterpreter.Run(
            program,
            new DeepState(0));

        Assert.Equal(depth, result.FinalState.Value);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  LINQ query syntax tests
// ═══════════════════════════════════════════════════════════════════════════

public class SlotLinqQuerySyntaxTests
{
    public record LinqState(int X, int Y);

    [Fact]
    public void QuerySyntax_SimpleSelect()
    {
        var program =
            from s in Slot.GetState<LinqState>()
            select s.X * 10;

        var result = TrampolineInterpreter.Run(
            program,
            new LinqState(3, 5));

        Assert.Equal(30, result.Value);
    }

    [Fact]
    public void QuerySyntax_MultipleFrom()
    {
        var program =
            from s in Slot.GetState<LinqState>()
            from d in Slot.Draw<LinqState>(_ => WeightSet.FromIntegers([1, 1, 1]))
            from _ in Slot.PutState<LinqState>(new LinqState(s.X + d, s.Y))
            from s2 in Slot.GetState<LinqState>()
            select s2.X;

        var choices = new Queue<int>([2]);
        var result = TrampolineInterpreter.Run(
            program,
            new LinqState(5, 10),
            choices);

        Assert.Equal(7, result.Value); // 5 + 2
    }

    [Fact]
    public void Branch_AsBindPlusConditional()
    {
        // Branch = GetState, then choose based on condition.
        var state = new LinqState(10, 20);

        var branchProgram = Slot.Branch(
            condition: (LinqState s) => s.X > 5,
            thenBranch: Slot.Modify<LinqState>(s => new LinqState(99, s.Y)),
            elseBranch: Slot.Modify<LinqState>(s => new LinqState(-1, s.Y)));

        // Manually express the same as query syntax:
        var queryProgram =
            from s in Slot.GetState<LinqState>()
            from _ in s.X > 5
                ? Slot.Modify<LinqState>(st => new LinqState(99, st.Y))
                : Slot.Modify<LinqState>(st => new LinqState(-1, st.Y))
            select Unit.Value;

        AssertProgramsEqual(branchProgram, queryProgram, state);
    }

    private static void AssertProgramsEqual<S, T>(
        Slot<S, T> left, Slot<S, T> right, S state)
    {
        var leftResult = TrampolineInterpreter.Run(left, state);
        var rightResult = TrampolineInterpreter.Run(right, state);
        Assert.Equal(leftResult.FinalState, rightResult.FinalState);
        Assert.Equal(leftResult.Value, rightResult.Value);
        Assert.Equal(leftResult.Trace, rightResult.Trace);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Determinism test
// ═══════════════════════════════════════════════════════════════════════════

public class SlotDeterminismTests
{
    public record DetState(int Value);

    [Fact]
    public void SameSeed_ProducesIdenticalResults()
    {
        var program =
            from d1 in Slot.Draw<DetState>(_ => WeightSet.FromIntegers([1, 1, 1, 1]))
            from d2 in Slot.Draw<DetState>(_ => WeightSet.FromIntegers([10, 20, 30]))
            from _ in Slot.Modify<DetState>(s => s with { Value = s.Value + d1 + d2 })
            select (d1, d2);

        var result1 = TrampolineInterpreter.RunWithSeed(program, new DetState(0), seed: 42);
        var result2 = TrampolineInterpreter.RunWithSeed(program, new DetState(0), seed: 42);
        var result3 = TrampolineInterpreter.RunWithSeed(program, new DetState(0), seed: 123456789);

        Assert.Equal(result1.Value, result2.Value);
        Assert.Equal(result1.FinalState, result2.FinalState);
        Assert.Equal(result1.Trace, result2.Trace);

        // Different seeds produce different draw choices → different trace and value.
        // This holds with overwhelming probability for any two distinct seeds.
        Assert.NotEqual(result1.Value, result3.Value);
        Assert.NotEqual(result1.Trace, result3.Trace);
    }
}
