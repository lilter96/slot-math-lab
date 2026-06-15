using System.Numerics;
using System.Reflection;
using CsCheck;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SlotMath.Codegen.Emit;
using SlotMath.Codegen.Runtime;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;
using SlotMath.Core.Serialization;

namespace SlotMath.Core.Tests.Codegen;

using Dict = Dictionary<string, object?>;
using PortMap = Dictionary<string, SlotMath.Core.Model.Port>;

// ═══════════════════════════════════════════════════════════════════════════
//  G7 — High-Assurance C# Codegen: Distribution Equivalence (D25 / invariant 11)
//
//  The CSharpEmitter turns a GraphConfig into real C# source.  Here we COMPILE
//  that source in-memory with Roslyn, load it, and prove its EXACT PMF (via the
//  re-execution enumerator) is identical — by exact rational equality — to the
//  exact interpreter, over thousands of random states with CsCheck shrinking.
//  We also prove the sampled hot path allocates O(1) per spin.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class CodegenEquivalenceTests
{
    private static Port StatePort => new() { Name = "state", Type = PortType.State };

    private const string H = "H";
    private const string L = "L";
    private const string W = "W";

    // ── Expression builders (scalar subset) ──────────────────────────────
    private static FieldAccessExpr Field(string key) => new() { Target = "state", Path = [key] };
    private static ConstantExpr Str(string v) => new() { Kind = ConstantKind.String, Value = v };
    private static ConstantExpr Int(long v) => new() { Kind = ConstantKind.Integer, Value = v.ToString() };
    private static CompareExpr Eq(Expression l, Expression r) => new() { Op = CompareOp.Eq, Left = l, Right = r };
    private static BinaryExpr And(Expression l, Expression r) => new() { Op = BinaryOp.And, Left = l, Right = r };
    private static BinaryExpr Or(Expression l, Expression r) => new() { Op = BinaryOp.Or, Left = l, Right = r };
    private static BinaryExpr Mul(Expression l, Expression r) => new() { Op = BinaryOp.Mul, Left = l, Right = r };
    private static BinaryExpr Add(Expression l, Expression r) => new() { Op = BinaryOp.Add, Left = l, Right = r };

    // base 1×3 win: allWild→3, allH→5, allL→2, else 0 (wilds wild).
    private static Expression IsH(int i) => Or(Eq(Field($"c{i}"), Str(H)), Eq(Field($"c{i}"), Str(W)));
    private static Expression IsL(int i) => Or(Eq(Field($"c{i}"), Str(L)), Eq(Field($"c{i}"), Str(W)));
    private static Expression IsW(int i) => Eq(Field($"c{i}"), Str(W));
    private static Expression All(Func<int, Expression> p) => And(And(p(0), p(1)), p(2));

    private static Expression BaseWin() => new IfExpr
    {
        Condition = All(IsW),
        ThenExpr = Int(3),
        ElseExpr = new IfExpr
        {
            Condition = All(IsH),
            ThenExpr = Int(5),
            ElseExpr = new IfExpr { Condition = All(IsL), ThenExpr = Int(2), ElseExpr = Int(0) },
        },
    };

    // ── Config builders ──────────────────────────────────────────────────

    private static DrawNode DrawCell(string id, string writeKey, bool entry) => new()
    {
        Id = id,
        DrawWeights =
        [
            new DrawWeight { OutcomeId = H, Weight = 3, Value = 0 },
            new DrawWeight { OutcomeId = L, Weight = 3, Value = 0 },
            new DrawWeight { OutcomeId = W, Weight = 1, Value = 0 },
        ],
        StateWriteKey = writeKey,
        Inputs = entry ? new PortMap() : new PortMap { ["state"] = StatePort },
        Outputs = new PortMap { ["state"] = StatePort },
    };

    /// <summary>REF-A "Coin": single weighted draw, win via data-flow value (no WinStateKey).</summary>
    private static GraphConfig RefA() => new()
    {
        SchemaVersion = "1.0.0",
        Id = "ref-a",
        Nodes =
        [
            new DrawNode
            {
                Id = "draw",
                DrawWeights =
                [
                    new DrawWeight { OutcomeId = "p3", Weight = 1, Value = 3 },
                    new DrawWeight { OutcomeId = "p1", Weight = 3, Value = 1 },
                    new DrawWeight { OutcomeId = "p0", Weight = 4, Value = 0 },
                ],
                Outputs = new PortMap { ["state"] = StatePort },
            },
            new MetricsSinkNode { Id = "sink", WinCap = 1000, Inputs = new PortMap { ["state"] = StatePort } },
        ],
        Edges = [new Edge { Id = "e0", SourceNodeId = "draw", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" }],
    };

    /// <summary>
    /// 1×3 wild game.  When <paramref name="stateDependent"/>, the payout is
    /// <c>mult · base + bonus</c> with mult/bonus seeded from the initial state,
    /// so random states drive distinct PMFs.
    /// </summary>
    private static GraphConfig Atomic1x3(bool stateDependent)
    {
        var winExpr = stateDependent
            ? Add(Mul(Field("mult"), BaseWin()), Field("bonus"))
            : BaseWin();

        var schema = new List<StateFieldSchema>
        {
            new() { Name = "c0", Type = "string" },
            new() { Name = "c1", Type = "string" },
            new() { Name = "c2", Type = "string" },
            new() { Name = "win", Type = "number" },
        };
        if (stateDependent)
        {
            schema.Add(new StateFieldSchema { Name = "mult", Type = "number" });
            schema.Add(new StateFieldSchema { Name = "bonus", Type = "number" });
        }

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = stateDependent ? "atomic-1x3-statedep" : "atomic-1x3",
            StateSchema = schema.ToArray(),
            Expressions = new Dictionary<string, Expression> { ["winExpr"] = winExpr },
            Nodes =
            [
                DrawCell("d0", "c0", entry: true),
                DrawCell("d1", "c1", entry: false),
                DrawCell("d2", "c2", entry: false),
                new ModifyStateNode
                {
                    Id = "win", ExpressionId = "winExpr", OutputKey = "win",
                    Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StatePort },
                },
                new MetricsSinkNode { Id = "sink", WinCap = 1_000_000, WinStateKey = "win", Inputs = new PortMap { ["state"] = StatePort } },
            ],
            Edges =
            [
                new Edge { Id = "e0", SourceNodeId = "d0", SourcePort = "state", TargetNodeId = "d1", TargetPort = "state" },
                new Edge { Id = "e1", SourceNodeId = "d1", SourcePort = "state", TargetNodeId = "d2", TargetPort = "state" },
                new Edge { Id = "e2", SourceNodeId = "d2", SourcePort = "state", TargetNodeId = "win", TargetPort = "state" },
                new Edge { Id = "e3", SourceNodeId = "win", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            ],
        };
    }

    // ── Reference exact PMF (the canonical oracle) ───────────────────────

    private static IReadOnlyDictionary<BigInteger, Rational> InterpreterPmf(
        Slot<Dict, BigInteger> program, Dict initial)
    {
        var dist = ExactInterpreter.Evaluate(program, initial, StateHasher.CanonicalHash).ValueDistribution();
        var map = new Dictionary<BigInteger, Rational>();
        foreach (var e in dist.Entries)
        {
            var p = new Rational(e.Numerator, dist.Denominator);
            map[e.Value] = map.TryGetValue(e.Value, out var x) ? x + p : p;
        }
        return map;
    }

    private static void AssertPmfEqual(
        IReadOnlyDictionary<BigInteger, Rational> expected,
        IReadOnlyDictionary<BigInteger, Rational> actual,
        string ctx)
    {
        Assert.True(expected.Count == actual.Count,
            $"PMF support size {expected.Count} (interpreter) vs {actual.Count} (generated) [{ctx}]");
        foreach (var kv in expected)
        {
            Assert.True(actual.TryGetValue(kv.Key, out var got),
                $"generated PMF missing win {kv.Key} [{ctx}]");
            Assert.True(kv.Value == got,
                $"P(win={kv.Key}) {kv.Value} (interpreter) != {got} (generated) [{ctx}]");
        }
    }

    // ── Compile generated C# in-memory with Roslyn ───────────────────────

    private static Type CompileGenerated(EmitResult emit)
    {
        Assert.True(emit.Supported, "emit unsupported: " + string.Join("; ", emit.Diagnostics));
        var tree = CSharpSyntaxTree.ParseText(emit.Source!, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "SlotMath.Generated.Dyn." + Guid.NewGuid().ToString("N"),
            [tree],
            ReferenceSet(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new Xunit.Sdk.XunitException(
                "Generated C# failed to compile:\n" + errors + "\n\n=== SOURCE ===\n" + emit.Source);
        }

        ms.Position = 0;
        var asm = Assembly.Load(ms.ToArray());
        return asm.GetType(emit.FullTypeName)
            ?? throw new Xunit.Sdk.XunitException($"Generated type '{emit.FullTypeName}' not found.");
    }

    private static IReadOnlyList<MetadataReference> ReferenceSet()
    {
        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        foreach (var path in tpa.Split(Path.PathSeparator))
        {
            if (path.Length == 0 || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(Path.GetFileName(path))) refs.Add(MetadataReference.CreateFromFile(path));
        }

        void Ensure(Type t)
        {
            var loc = t.Assembly.Location;
            if (loc.Length > 0 && seen.Add(Path.GetFileName(loc)))
                refs.Add(MetadataReference.CreateFromFile(loc));
        }

        Ensure(typeof(ICompiledGame));      // SlotMath.Codegen
        Ensure(typeof(Rational));           // SlotMath.Core
        return refs;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Equivalence proofs
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void RefA_GeneratedExactPmf_EqualsInterpreter()
    {
        var config = RefA();
        var program = new GraphCompiler().Compile(config).Program!;
        var type = CompileGenerated(new CSharpEmitter().Emit(config));

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));

        AssertPmfEqual(InterpreterPmf(program, new Dict()), genPmf, "REF-A");

        // Spot-check the closed form: P(3)=1/8, P(1)=3/8, P(0)=4/8; RTP=3/4.
        Assert.Equal(new Rational(1, 8), genPmf[3]);
        Assert.Equal(new Rational(3, 8), genPmf[1]);
        Assert.Equal(new Rational(4, 8), genPmf[0]);
    }

    [Fact]
    public void Atomic1x3_GeneratedExactPmf_EqualsInterpreter()
    {
        var config = Atomic1x3(stateDependent: false);
        var program = new GraphCompiler().Compile(config).Program!;
        var type = CompileGenerated(new CSharpEmitter().Emit(config));

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));

        AssertPmfEqual(InterpreterPmf(program, new Dict()), genPmf, "atomic-1x3");
    }

    [Fact]
    public void StateDependent_GeneratedExactPmf_EqualsInterpreter_Over1000RandomStates()
    {
        var config = Atomic1x3(stateDependent: true);
        var program = new GraphCompiler().Compile(config).Program!;
        var type = CompileGenerated(new CSharpEmitter().Emit(config));

        // D25 PBT: 1,000 random initial states; exact PMF of compiled C# must
        // equal the interpreter's, by exact rational equality.  CsCheck shrinks
        // any divergence to the minimal failing (mult, bonus).
        Gen.Select(Gen.Int[1, 5], Gen.Int[0, 50])
           .Sample(
               (mult, bonus) =>
               {
                   var s0 = new Dict { ["mult"] = (BigInteger)mult, ["bonus"] = (BigInteger)bonus };
                   var game = (ICompiledGame)Activator.CreateInstance(type)!;
                   game.SetInitial(s0);
                   var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));
                   AssertPmfEqual(InterpreterPmf(program, s0), genPmf, $"mult={mult},bonus={bonus}");
               },
               seed: "g7-statedep-pmf-v1",
               iter: 1000);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Hot path: O(1) allocations per spin (G7 DoD)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SampledHotPath_AllocatesO1_PerSpin()
    {
        var type = CompileGenerated(new CSharpEmitter().Emit(RefA()));
        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var driver = new SampledDrawDriver(0xC0FFEE);

        // Warm up JIT so steady-state allocation is measured.
        long sink = 0;
        for (var i = 0; i < 10_000; i++) sink += game.RunSpin(driver);

        const int n = 200_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < n; i++) sink += game.RunSpin(driver);
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(sink >= 0); // keep the loop from being optimized away
        var perSpin = (after - before) / (double)n;
        Assert.True(perSpin < 1.0,
            $"sampled hot path allocated {perSpin:F4} bytes/spin (expected ~0, O(1)).");
    }

    // ════════════════════════════════════════════════════════════════════
    //  The emitted artifact is real, well-formed C#
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmittedSource_IsWellFormed_AndPersistedAsArtifact()
    {
        var emit = new CSharpEmitter().Emit(Atomic1x3(stateDependent: true));
        Assert.True(emit.Supported, string.Join("; ", emit.Diagnostics));
        Assert.Contains("public long RunSpin(IDrawDriver", emit.Source);
        Assert.Contains(": ICompiledGame", emit.Source);
        Assert.Contains("private static readonly long[] __w0", emit.Source); // pre-allocated weight table
        Assert.DoesNotContain("new ", emit.Source!.Replace("new long[]", "").Replace("new string[]", "")); // no per-spin heap news

        var dir = Path.Combine(AppContext.BaseDirectory, "generated");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "GeneratedGame.Atomic1x3.cs"), emit.Source);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Honesty: unsupported constructs yield a diagnostic, never wrong code
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnsupportedGraph_ReturnsDiagnostic_NotWrongCode()
    {
        // A reel draw (board publication) is outside the scalar subset.
        var config = RefA() with
        {
            Nodes =
            [
                new DrawNode
                {
                    Id = "draw",
                    BoardStateKey = "board",
                    DrawWeights = [new DrawWeight { OutcomeId = "x", Weight = 1, Value = 0 }],
                    Outputs = new PortMap { ["state"] = StatePort },
                },
                new MetricsSinkNode { Id = "sink", WinCap = 1000, Inputs = new PortMap { ["state"] = StatePort } },
            ],
        };

        var emit = new CSharpEmitter().Emit(config);
        Assert.False(emit.Supported);
        Assert.Null(emit.Source);
        Assert.NotEmpty(emit.Diagnostics);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Property-based verification over RANDOM GRAPHS (G7 DoD)
    //
    //  The deliverable is a "property-based verification harness (CsCheck)".
    //  Above we vary states on fixed graphs; here we vary the GRAPH ITSELF —
    //  random draw counts, outcome sets, weights, and a random well-typed
    //  scalar payout expression — then compile each generated graph to C# and
    //  prove its exact PMF equals the interpreter's across random states.
    //  CsCheck auto-shrinks any divergence to the minimal failing graph.
    // ════════════════════════════════════════════════════════════════════

    private static readonly string[] Syms = ["A", "B", "C"];
    private static readonly string[] NumFields = ["mult", "bonus"];
    private static readonly BinaryOp[] AddSubMul = [BinaryOp.Add, BinaryOp.Sub, BinaryOp.Mul];
    private static readonly CompareOp[] EqNeq = [CompareOp.Eq, CompareOp.Neq];
    private static readonly CompareOp[] AllCmp =
        [CompareOp.Eq, CompareOp.Neq, CompareOp.Lt, CompareOp.Gt, CompareOp.Lte, CompareOp.Gte];

    private sealed record DrawSpec(int M, int[] Weights);
    private sealed record GraphSpec(DrawSpec[] Draws, Expression WinExpr);

    private static string[] CellKeys(int k) => Enumerable.Range(0, k).Select(i => $"c{i}").ToArray();

    private static Gen<Expression> GenNumber(string[] cells, int depth)
    {
        var leaf = Gen.OneOf(
            Gen.Int[0, 5].Select(v => (Expression)Int(v)),
            Gen.OneOfConst(NumFields).Select(f => (Expression)Field(f)));
        if (depth <= 0) return leaf;

        return Gen.OneOf(
            leaf,
            Gen.Select(Gen.OneOfConst(AddSubMul), GenNumber(cells, depth - 1), GenNumber(cells, depth - 1),
                (op, l, r) => (Expression)new BinaryExpr { Op = op, Left = l, Right = r }),
            Gen.Select(GenBool(cells, depth - 1), GenNumber(cells, depth - 1), GenNumber(cells, depth - 1),
                (c, t, e) => (Expression)new IfExpr { Condition = c, ThenExpr = t, ElseExpr = e }));
    }

    private static Gen<Expression> GenBool(string[] cells, int depth)
    {
        var cellEq = Gen.Select(Gen.OneOfConst(cells), Gen.OneOfConst(Syms), Gen.OneOfConst(EqNeq),
            (cell, sym, op) => (Expression)new CompareExpr { Op = op, Left = Field(cell), Right = Str(sym) });
        var numCmp = Gen.Select(Gen.OneOfConst(AllCmp), GenNumber(cells, System.Math.Max(0, depth - 1)),
            GenNumber(cells, System.Math.Max(0, depth - 1)),
            (op, l, r) => (Expression)new CompareExpr { Op = op, Left = l, Right = r });
        if (depth <= 0) return Gen.OneOf(cellEq, numCmp);

        return Gen.OneOf(
            cellEq, numCmp,
            Gen.Select(GenBool(cells, depth - 1), GenBool(cells, depth - 1),
                (l, r) => (Expression)new BinaryExpr { Op = BinaryOp.And, Left = l, Right = r }),
            Gen.Select(GenBool(cells, depth - 1), GenBool(cells, depth - 1),
                (l, r) => (Expression)new BinaryExpr { Op = BinaryOp.Or, Left = l, Right = r }),
            GenBool(cells, depth - 1).Select(e => (Expression)new NotExpr { Expr = e }));
    }

    private static readonly Gen<GraphSpec> GenGraphSpec =
        from k in Gen.Int[1, 3]
        from draws in (from m in Gen.Int[2, 3]
                       from w in Gen.Int[1, 5].Array[m]
                       select new DrawSpec(m, w)).Array[k]
        from win in GenNumber(CellKeys(k), 3)
        select new GraphSpec(draws, win);

    private static GraphConfig BuildRandomConfig(GraphSpec spec)
    {
        var k = spec.Draws.Length;
        var nodes = new List<Node>();
        var edges = new List<Edge>();

        for (var i = 0; i < k; i++)
        {
            var d = spec.Draws[i];
            nodes.Add(new DrawNode
            {
                Id = $"d{i}",
                StateWriteKey = $"c{i}",
                DrawWeights = Enumerable.Range(0, d.M)
                    .Select(j => new DrawWeight { OutcomeId = Syms[j], Weight = d.Weights[j], Value = 0 })
                    .ToArray(),
                Inputs = i == 0 ? new PortMap() : new PortMap { ["state"] = StatePort },
                Outputs = new PortMap { ["state"] = StatePort },
            });
            if (i > 0)
                edges.Add(new Edge { Id = $"e{i}", SourceNodeId = $"d{i - 1}", SourcePort = "state", TargetNodeId = $"d{i}", TargetPort = "state" });
        }

        nodes.Add(new ModifyStateNode
        {
            Id = "win",
            ExpressionId = "winExpr",
            OutputKey = "win",
            Inputs = new PortMap { ["state"] = StatePort },
            Outputs = new PortMap { ["state"] = StatePort },
        });
        edges.Add(new Edge { Id = "ew", SourceNodeId = $"d{k - 1}", SourcePort = "state", TargetNodeId = "win", TargetPort = "state" });

        nodes.Add(new MetricsSinkNode { Id = "sink", WinCap = 1_000_000, WinStateKey = "win", Inputs = new PortMap { ["state"] = StatePort } });
        edges.Add(new Edge { Id = "es", SourceNodeId = "win", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" });

        var schema = new List<StateFieldSchema> { new() { Name = "mult", Type = "number" }, new() { Name = "bonus", Type = "number" }, new() { Name = "win", Type = "number" } };
        for (var i = 0; i < k; i++) schema.Add(new StateFieldSchema { Name = $"c{i}", Type = "string" });

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "rand-graph",
            StateSchema = schema.ToArray(),
            Expressions = new Dictionary<string, Expression> { ["winExpr"] = spec.WinExpr },
            Nodes = nodes.ToArray(),
            Edges = edges.ToArray(),
        };
    }

    [Fact]
    public void RandomGraphs_GeneratedExactPmf_EqualInterpreter_WithShrinking()
    {
        // 120 random graphs × 9 random states = 1,080 (graph, state) equivalence
        // checks — each compiled to C# with Roslyn and compared to the
        // interpreter by exact rational equality.
        GenGraphSpec.Sample(
            spec =>
            {
                var config = BuildRandomConfig(spec);

                // DoD: "compiles valid graphs" — the random graph must validate.
                var compile = new GraphCompiler().Compile(config);
                Assert.True(compile.IsValid,
                    "random graph failed to compile: " +
                    string.Join("; ", compile.Errors.Select(e => $"[{e.Code}] {e.Message}")));

                var emit = new CSharpEmitter().Emit(config);
                Assert.True(emit.Supported, "emit unsupported: " + string.Join("; ", emit.Diagnostics));
                var type = CompileGenerated(emit);
                var program = compile.Program!;

                // Random states, deterministic per graph (seeded by its configHash).
                var seed = unchecked((int)Convert.ToUInt32(ConfigHash.Compute(config)[..8], 16));
                var rnd = new System.Random(seed);
                for (var s = 0; s < 9; s++)
                {
                    var s0 = new Dict
                    {
                        ["mult"] = (BigInteger)rnd.Next(1, 4),
                        ["bonus"] = (BigInteger)rnd.Next(0, 11),
                    };
                    var game = (ICompiledGame)Activator.CreateInstance(type)!;
                    game.SetInitial(s0);
                    var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));
                    AssertPmfEqual(InterpreterPmf(program, s0), genPmf, $"graph={config.Id} state[{s}]");
                }
            },
            seed: "g7-random-graphs-v1",
            iter: 120);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Branch (graph-level if/else, true/false ports)
    // ════════════════════════════════════════════════════════════════════

    private static Port StateOut => new() { Name = "state", Type = PortType.State };

    private static GraphConfig BranchGame(int wA, int wB, int wC, long winTrue, long winFalse, string trigger) => new()
    {
        SchemaVersion = "1.0.0",
        Id = "branch-game",
        StateSchema =
        [
            new StateFieldSchema { Name = "sym", Type = "string" },
            new StateFieldSchema { Name = "win", Type = "number" },
        ],
        Expressions = new Dictionary<string, Expression>
        {
            ["cond"] = Eq(Field("sym"), Str(trigger)),
            ["wT"] = Int(winTrue),
            ["wF"] = Int(winFalse),
        },
        Nodes =
        [
            new DrawNode
            {
                Id = "d0",
                StateWriteKey = "sym",
                DrawWeights =
                [
                    new DrawWeight { OutcomeId = "A", Weight = wA, Value = 0 },
                    new DrawWeight { OutcomeId = "B", Weight = wB, Value = 0 },
                    new DrawWeight { OutcomeId = "C", Weight = wC, Value = 0 },
                ],
                Outputs = new PortMap { ["state"] = StateOut },
            },
            new BranchNode
            {
                Id = "br", ConditionId = "cond",
                Inputs = new PortMap { ["state"] = StatePort },
                Outputs = new PortMap { ["true"] = StateOut, ["false"] = StateOut },
            },
            new ModifyStateNode { Id = "winT", ExpressionId = "wT", OutputKey = "win", Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StateOut } },
            new ModifyStateNode { Id = "winF", ExpressionId = "wF", OutputKey = "win", Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StateOut } },
            new MetricsSinkNode { Id = "sink", WinCap = 1_000_000, WinStateKey = "win", Inputs = new PortMap { ["state"] = StatePort } },
        ],
        Edges =
        [
            new Edge { Id = "e0", SourceNodeId = "d0", SourcePort = "state", TargetNodeId = "br", TargetPort = "state" },
            new Edge { Id = "e1", SourceNodeId = "br", SourcePort = "true", TargetNodeId = "winT", TargetPort = "state" },
            new Edge { Id = "e2", SourceNodeId = "br", SourcePort = "false", TargetNodeId = "winF", TargetPort = "state" },
            new Edge { Id = "e3", SourceNodeId = "winT", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            new Edge { Id = "e4", SourceNodeId = "winF", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
        ],
    };

    [Fact]
    public void Branch_GeneratedExactPmf_EqualsInterpreter()
    {
        var config = BranchGame(2, 3, 5, winTrue: 10, winFalse: 1, trigger: "A");
        var program = new GraphCompiler().Compile(config).Program!;
        var type = CompileGenerated(new CSharpEmitter().Emit(config));

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));

        AssertPmfEqual(InterpreterPmf(program, new Dict()), genPmf, "branch");
        // sym=A (w=2/10) → win 10; else (8/10) → win 1.
        Assert.Equal(new Rational(2, 10), genPmf[10]);
        Assert.Equal(new Rational(8, 10), genPmf[1]);
    }

    [Fact]
    public void Branch_PBT_RandomConfigs_EqualInterpreter()
    {
        Gen.Select(Gen.Int[1, 5], Gen.Int[1, 5], Gen.Int[1, 5], Gen.Int[0, 20], Gen.Int[0, 20], Gen.OneOfConst("A", "B", "C"))
           .Sample(
               (wA, wB, wC, wt, wf, trig) =>
               {
                   var config = BranchGame(wA, wB, wC, wt, wf, trig);
                   var compile = new GraphCompiler().Compile(config);
                   Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => e.Message)));
                   var type = CompileGenerated(new CSharpEmitter().Emit(config));

                   var game = (ICompiledGame)Activator.CreateInstance(type)!;
                   game.SetInitial(new Dict());
                   var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));
                   AssertPmfEqual(InterpreterPmf(compile.Program!, new Dict()), genPmf,
                       $"branch w=({wA},{wB},{wC}) win=({wt},{wf}) trig={trig}");
               },
               seed: "g7-branch-pbt-v1",
               iter: 200);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Loop (bounded, body/exit ports) — accumulates the body win per iteration
    // ════════════════════════════════════════════════════════════════════

    private static Port NumPort => new() { Name = "n", Type = PortType.Number };

    private static GraphConfig LoopGame(int maxIter, (int W, long V)[] body) => new()
    {
        SchemaVersion = "1.0.0",
        Id = "loop-game",
        Nodes =
        [
            new LoopNode
            {
                Id = "loop", MaxIterations = maxIter,
                Outputs = new PortMap { ["body"] = NumPort, ["exit"] = NumPort },
            },
            new DrawNode
            {
                Id = "body",
                DrawWeights = body.Select((o, i) => new DrawWeight { OutcomeId = $"o{i}", Weight = o.W, Value = o.V }).ToArray(),
                Inputs = new PortMap { ["in"] = NumPort },
                // no Outputs → terminal body node
            },
            new MetricsSinkNode { Id = "sink", WinCap = 1_000_000, Inputs = new PortMap { ["wins"] = NumPort } },
        ],
        Edges =
        [
            new Edge { Id = "e1", SourceNodeId = "loop", SourcePort = "body", TargetNodeId = "body", TargetPort = "in" },
            new Edge { Id = "e2", SourceNodeId = "loop", SourcePort = "exit", TargetNodeId = "sink", TargetPort = "wins" },
        ],
    };

    [Fact]
    public void Loop_FixedCount_Deterministic_EqualsInterpreter()
    {
        var config = LoopGame(3, [(1, 10)]);
        var program = new GraphCompiler().Compile(config).Program!;
        var type = CompileGenerated(new CSharpEmitter().Emit(config));

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));

        AssertPmfEqual(InterpreterPmf(program, new Dict()), genPmf, "loop-fixed");
        Assert.Equal(Rational.One, genPmf[30]); // 3 × 10, deterministic
    }

    [Fact]
    public void Loop_MultiOutcomeBody_EqualsInterpreter()
    {
        // 2 iterations, body draws {0,10} equally → sum ∈ {0,10,20} with 1:2:1.
        var config = LoopGame(2, [(1, 0), (1, 10)]);
        var program = new GraphCompiler().Compile(config).Program!;
        var type = CompileGenerated(new CSharpEmitter().Emit(config));

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));

        AssertPmfEqual(InterpreterPmf(program, new Dict()), genPmf, "loop-multi");
        Assert.Equal(new Rational(1, 4), genPmf[0]);
        Assert.Equal(new Rational(2, 4), genPmf[10]);
        Assert.Equal(new Rational(1, 4), genPmf[20]);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Fast-path evaluator over a reel draw (Lines) — dict-state mode
    //
    //  The emitted code builds the board from reel strips, then calls the SAME
    //  registered IFastPathEvaluator the interpreter uses, so equivalence holds
    //  by construction — and the PMF harness still proves the reel-draw + board
    //  + win-sum wiring is byte-for-byte the interpreter's.
    // ════════════════════════════════════════════════════════════════════

    private static Port BoardPort => new() { Name = "board", Type = PortType.Board };
    private static Port WinsPort => new() { Name = "wins", Type = PortType.Wins };

    private static void EnsureEvaluator(string name, IFastPathEvaluator ev)
    {
        if (EvaluatorRegistry.TryGet(name) is null)
        {
            try { EvaluatorRegistry.Register(name, ev); }
            catch (InvalidOperationException) { /* registered by a parallel test */ }
        }
    }

    private static GraphConfig LinesReelGame()
    {
        EnsureEvaluator("lines_cg", new LinesEvaluator(
            new Paytable
            {
                Id = "pt",
                Entries =
                [
                    new PaytableEntry { SymbolId = "sym-a", Counts = [3], Payouts = ["10"] },
                    new PaytableEntry { SymbolId = "sym-b", Counts = [3], Payouts = ["5"] },
                ],
            },
            new PaylineSet { Id = "ps", Paylines = [new Payline { Positions = [0, 0, 0] }] },
            wildSymbolId: null));

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "lines-reel-cg",
            ReelStrips =
            [
                new ReelStrip { Id = "r1", Name = "R1", Symbols = ["sym-a", "sym-b", "sym-a"] },
                new ReelStrip { Id = "r2", Name = "R2", Symbols = ["sym-a", "sym-b", "sym-a"] },
                new ReelStrip { Id = "r3", Name = "R3", Symbols = ["sym-a", "sym-b", "sym-a"] },
            ],
            ReelSets = [new ReelSet { Id = "rs", Name = "Main", StripIds = ["r1", "r2", "r3"] }],
            BoardConfig = new BoardConfig { Rows = 3, Columns = 3 },
            Nodes =
            [
                new DrawNode { Id = "draw", Outputs = new PortMap { ["board"] = BoardPort } },
                new MapNode { Id = "eval", TransformId = "lines_cg", Inputs = new PortMap { ["board"] = BoardPort }, Outputs = new PortMap { ["wins"] = WinsPort } },
                new MetricsSinkNode { Id = "sink", WinCap = 1_000_000, Inputs = new PortMap { ["wins"] = WinsPort } },
            ],
            Edges =
            [
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            ],
        };
    }

    private static GraphConfig ReelMapGame(string id, string mech, int rows, string[][] stripSymbols)
    {
        var strips = stripSymbols.Select((s, i) => new ReelStrip { Id = $"r{i}", Name = $"R{i}", Symbols = s }).ToArray();
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = id,
            ReelStrips = strips,
            ReelSets = [new ReelSet { Id = "rs", Name = "Main", StripIds = strips.Select(s => s.Id).ToArray() }],
            BoardConfig = new BoardConfig { Rows = rows, Columns = stripSymbols.Length },
            Nodes =
            [
                new DrawNode { Id = "draw", Outputs = new PortMap { ["board"] = BoardPort } },
                new MapNode { Id = "eval", TransformId = mech, Inputs = new PortMap { ["board"] = BoardPort }, Outputs = new PortMap { ["wins"] = WinsPort } },
                new MetricsSinkNode { Id = "sink", WinCap = 1_000_000, Inputs = new PortMap { ["wins"] = WinsPort } },
            ],
            Edges =
            [
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            ],
        };
    }

    private static void VerifyFastPath(GraphConfig config, string ctx)
    {
        var compile = new GraphCompiler().Compile(config);
        Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        var emit = new CSharpEmitter().Emit(config);
        Assert.True(emit.Supported, string.Join("; ", emit.Diagnostics));
        Assert.Contains("EvaluatorRegistry.TryGet", emit.Source);
        var type = CompileGenerated(emit);

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));
        AssertPmfEqual(InterpreterPmf(compile.Program!, new Dict()), genPmf, ctx);
    }

    [Fact]
    public void FastPathLines_ReelDraw_GeneratedExactPmf_EqualsInterpreter()
    {
        VerifyFastPath(LinesReelGame(), "lines-reel");
    }

    [Fact]
    public void FastPathWays_ReelDraw_GeneratedExactPmf_EqualsInterpreter()
    {
        EnsureEvaluator("ways_cg", new WaysEvaluator(
            new Paytable
            {
                Id = "pt",
                Entries =
                [
                    new PaytableEntry { SymbolId = "H", Counts = [2], Payouts = ["4"] },
                    new PaytableEntry { SymbolId = "L", Counts = [2], Payouts = ["2"] },
                ],
            },
            wildSymbolId: null));
        VerifyFastPath(ReelMapGame("ways-reel-cg", "ways_cg", rows: 2, [["H", "L"], ["H", "L"]]), "ways-reel");
    }

    [Fact]
    public void FastPathCluster_ReelDraw_GeneratedExactPmf_EqualsInterpreter()
    {
        EnsureEvaluator("cluster_cg", new ClusterEvaluator(
            new Paytable
            {
                Id = "pt",
                Entries =
                [
                    new PaytableEntry { SymbolId = "H", Counts = [2, 3, 4], Payouts = ["2", "3", "4"] },
                    new PaytableEntry { SymbolId = "L", Counts = [2, 3, 4], Payouts = ["1", "2", "3"] },
                ],
            },
            minClusterSize: 2, wildSymbolId: null));
        VerifyFastPath(ReelMapGame("cluster-reel-cg", "cluster_cg", rows: 2, [["H", "L"], ["H", "L"]]), "cluster-reel");
    }

    [Fact]
    public void Loop_PBT_RandomConfigs_EqualInterpreter()
    {
        Gen.Select(Gen.Int[1, 4], Gen.Int[1, 5], Gen.Int[0, 10], Gen.Int[1, 5], Gen.Int[0, 10])
           .Sample(
               (maxIter, w0, v0, w1, v1) =>
               {
                   var config = LoopGame(maxIter, [(w0, v0), (w1, v1)]);
                   var compile = new GraphCompiler().Compile(config);
                   Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => e.Message)));
                   var type = CompileGenerated(new CSharpEmitter().Emit(config));

                   var game = (ICompiledGame)Activator.CreateInstance(type)!;
                   game.SetInitial(new Dict());
                   var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));
                   AssertPmfEqual(InterpreterPmf(compile.Program!, new Dict()), genPmf,
                       $"loop maxIter={maxIter} body=({w0}:{v0},{w1}:{v1})");
               },
               seed: "g7-loop-pbt-v1",
               iter: 100);
    }
}
