using System.Numerics;
using System.Reflection;
using CsCheck;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SlotMath.Codegen.Emit;
using SlotMath.Codegen.Runtime;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Mechanics.Transforms;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Plugins;
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

// Serialized with all EvaluatorRegistry-touching tests: the fast-path fixtures
// register evaluators the generated code resolves from the global registry, so
// they must not race a parallel EvaluatorRegistry.Clear().
[Collection("Registry")]
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
        // An array-valued MapExpr output is outside the supported subset
        // (array-valued folds/maps are still pending — see diagnostics).
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "array-map-unsupported",
            Expressions = new Dictionary<string, Expression>
            {
                ["mp"] = new MapExpr { StateKey = "src", ItemName = "x", ItemType = ExprType.Number, Body = Field("x") },
            },
            Nodes =
            [
                new DataNode { Id = "data", StateKey = "src", Values = ["1", "2"], Outputs = new PortMap { ["state"] = StateOut } },
                new ModifyStateNode { Id = "m", ExpressionId = "mp", OutputKey = "out", Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StateOut } },
                new MetricsSinkNode { Id = "sink", WinCap = 1000, Inputs = new PortMap { ["state"] = StatePort } },
            ],
            Edges =
            [
                new Edge { Id = "e0", SourceNodeId = "data", SourcePort = "state", TargetNodeId = "m", TargetPort = "state" },
                new Edge { Id = "e1", SourceNodeId = "m", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
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
    //  Fan-out — a node with multiple successors; per-path wins are summed
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void FanOut_TwoPaths_GeneratedExactPmf_EqualsInterpreter()
    {
        // entry (GetState, pass-through) fans out to two independent draws whose
        // values are summed: a∈{0,10}, b∈{0,20} ⇒ win ∈ {0,10,20,30} each 1/4.
        var num = new Port { Name = "value", Type = PortType.Number };
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "fanout-cg",
            Nodes =
            [
                new GetStateNode { Id = "g", Outputs = new PortMap { ["value"] = num } },
                new DrawNode
                {
                    Id = "a",
                    DrawWeights = [new DrawWeight { OutcomeId = "x", Weight = 1, Value = 0 }, new DrawWeight { OutcomeId = "y", Weight = 1, Value = 10 }],
                    Inputs = new PortMap { ["in"] = num }, Outputs = new PortMap { ["value"] = num },
                },
                new DrawNode
                {
                    Id = "b",
                    DrawWeights = [new DrawWeight { OutcomeId = "x", Weight = 1, Value = 0 }, new DrawWeight { OutcomeId = "y", Weight = 1, Value = 20 }],
                    Inputs = new PortMap { ["in"] = num }, Outputs = new PortMap { ["value"] = num },
                },
                new MetricsSinkNode { Id = "sink", WinCap = 1_000_000, Inputs = new PortMap { ["wins"] = num } },
            ],
            Edges =
            [
                new Edge { Id = "e0", SourceNodeId = "g", SourcePort = "value", TargetNodeId = "a", TargetPort = "in" },
                new Edge { Id = "e1", SourceNodeId = "g", SourcePort = "value", TargetNodeId = "b", TargetPort = "in" },
                new Edge { Id = "e2", SourceNodeId = "a", SourcePort = "value", TargetNodeId = "sink", TargetPort = "wins" },
                new Edge { Id = "e3", SourceNodeId = "b", SourcePort = "value", TargetNodeId = "sink", TargetPort = "wins" },
            ],
        };

        var compile = new GraphCompiler().Compile(config);
        Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        var type = CompileGenerated(new CSharpEmitter().Emit(config));

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));

        AssertPmfEqual(InterpreterPmf(compile.Program!, new Dict()), genPmf, "fan-out");
        Assert.Equal(new Rational(1, 4), genPmf[0]);
        Assert.Equal(new Rational(1, 4), genPmf[30]);
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

    // ════════════════════════════════════════════════════════════════════
    //  Array/fold: scatter mechanic — reel board → aggregate count → payout
    // ════════════════════════════════════════════════════════════════════

    private static GraphConfig ScatterReelGame()
    {
        // 1×3 reel of {S,X}; pay 5 when at least 2 scatters land.
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "scatter-reel-cg",
            StateSchema =
            [
                new StateFieldSchema { Name = "scatter_count", Type = "number" },
                new StateFieldSchema { Name = "scatter_win", Type = "number" },
            ],
            ReelStrips =
            [
                new ReelStrip { Id = "r1", Name = "R1", Symbols = ["S", "X"] },
                new ReelStrip { Id = "r2", Name = "R2", Symbols = ["S", "X"] },
                new ReelStrip { Id = "r3", Name = "R3", Symbols = ["S", "X"] },
            ],
            ReelSets = [new ReelSet { Id = "rs", Name = "Main", StripIds = ["r1", "r2", "r3"] }],
            BoardConfig = new BoardConfig { Rows = 1, Columns = 3 },
            Expressions = new Dictionary<string, Expression>
            {
                ["count"] = new AggregateExpr
                {
                    Func = AggregateFunc.Count,
                    StateKey = "board",
                    ItemName = "cell",
                    ItemType = ExprType.String,
                    Predicate = Eq(Field("cell"), Str("S")),
                },
                ["payout"] = new IfExpr
                {
                    Condition = new CompareExpr { Op = CompareOp.Gte, Left = Field("scatter_count"), Right = Int(2) },
                    ThenExpr = Int(5),
                    ElseExpr = Int(0),
                },
            },
            Nodes =
            [
                new DrawNode { Id = "draw", Outputs = new PortMap { ["state"] = StateOut } },
                new ModifyStateNode { Id = "cnt", ExpressionId = "count", OutputKey = "scatter_count", Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StateOut } },
                new ModifyStateNode { Id = "pay", ExpressionId = "payout", OutputKey = "scatter_win", Inputs = new PortMap { ["state"] = StatePort }, Outputs = new PortMap { ["state"] = StateOut } },
                new MetricsSinkNode { Id = "sink", WinCap = 1_000_000, WinStateKey = "scatter_win", Inputs = new PortMap { ["state"] = StatePort } },
            ],
            Edges =
            [
                new Edge { Id = "e0", SourceNodeId = "draw", SourcePort = "state", TargetNodeId = "cnt", TargetPort = "state" },
                new Edge { Id = "e1", SourceNodeId = "cnt", SourcePort = "state", TargetNodeId = "pay", TargetPort = "state" },
                new Edge { Id = "e2", SourceNodeId = "pay", SourcePort = "state", TargetNodeId = "sink", TargetPort = "state" },
            ],
        };
    }

    // ════════════════════════════════════════════════════════════════════
    //  Plugin (level-c) evaluator over a reel board — injected PluginHost
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Plugin_Evaluator_ReelBoard_GeneratedExactPmf_EqualsInterpreter()
    {
        var host = new PluginHost();
        host.RegisterEvaluator("scatterplug", new CodegenScatterPlugin(), new ConformanceResult
        {
            Passed = true,
            Failures = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
        });

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "plugin-reel-cg",
            ReelStrips =
            [
                new ReelStrip { Id = "r1", Name = "R1", Symbols = ["S", "X"] },
                new ReelStrip { Id = "r2", Name = "R2", Symbols = ["S", "X"] },
                new ReelStrip { Id = "r3", Name = "R3", Symbols = ["S", "X"] },
            ],
            ReelSets = [new ReelSet { Id = "rs", Name = "Main", StripIds = ["r1", "r2", "r3"] }],
            BoardConfig = new BoardConfig { Rows = 1, Columns = 3 },
            Nodes =
            [
                new DrawNode { Id = "draw", Outputs = new PortMap { ["board"] = BoardPort } },
                new MapNode { Id = "eval", TransformId = "plugin:scatterplug", Inputs = new PortMap { ["board"] = BoardPort }, Outputs = new PortMap { ["wins"] = WinsPort } },
                new MetricsSinkNode { Id = "sink", WinCap = 1_000_000, Inputs = new PortMap { ["wins"] = WinsPort } },
            ],
            Edges =
            [
                new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "eval", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
            ],
            Plugins = [new PluginReference { PluginId = "scatterplug", Contract = PluginContract.IEvaluator, Version = "1.0.0" }],
        };

        var compile = new GraphCompiler(host).Compile(config);
        Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => $"[{e.Code}] {e.Message}")));

        var emit = new CSharpEmitter().Emit(config);
        Assert.True(emit.Supported, string.Join("; ", emit.Diagnostics));
        Assert.Contains("IPluginHostAware", emit.Source);
        var type = CompileGenerated(emit);

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        ((IPluginHostAware)game).SetPluginHost(host);
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));

        AssertPmfEqual(InterpreterPmf(compile.Program!, new Dict()), genPmf, "plugin");
        Assert.Equal(new Rational(4, 8), genPmf[5]); // ≥2 scatters ⇒ pay 5, P = 4/8
    }

    [Fact]
    public void Scatter_ReelBoard_AggregateCount_GeneratedExactPmf_EqualsInterpreter()
    {
        var config = ScatterReelGame();
        var compile = new GraphCompiler().Compile(config);
        Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => $"[{e.Code}] {e.Message}")));

        var emit = new CSharpEmitter().Emit(config);
        Assert.True(emit.Supported, string.Join("; ", emit.Diagnostics));
        Assert.Contains("for (int", emit.Source); // an emitted aggregate loop
        var type = CompileGenerated(emit);

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));

        AssertPmfEqual(InterpreterPmf(compile.Program!, new Dict()), genPmf, "scatter-reel");
        // count ~ Binomial(3, 1/2); pay 5 when count ≥ 2 ⇒ P(5) = 4/8.
        Assert.Equal(new Rational(4, 8), genPmf[5]);
        Assert.Equal(new Rational(4, 8), genPmf[0]);
    }

    [Fact]
    public void Scatter_ReelBoard_FoldCount_GeneratedExactPmf_EqualsInterpreter()
    {
        // Same scatter via a FoldExpr (acc + if cell=="S" then 1 else 0) —
        // exercises the EmitNumberFold path (AccName/Init/Body), not Aggregate.
        var config = ScatterReelGame() with
        {
            Expressions = new Dictionary<string, Expression>
            {
                ["count"] = new FoldExpr
                {
                    StateKey = "board",
                    AccName = "acc",
                    ItemName = "cell",
                    ItemType = ExprType.String,
                    Init = Int(0),
                    Body = new IfExpr
                    {
                        Condition = Eq(Field("cell"), Str("S")),
                        ThenExpr = new BinaryExpr { Op = BinaryOp.Add, Left = Field("acc"), Right = Int(1) },
                        ElseExpr = Field("acc"),
                    },
                },
                ["payout"] = new IfExpr
                {
                    Condition = new CompareExpr { Op = CompareOp.Gte, Left = Field("scatter_count"), Right = Int(2) },
                    ThenExpr = Int(5),
                    ElseExpr = Int(0),
                },
            },
        };
        var compile = new GraphCompiler().Compile(config);
        Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => $"[{e.Code}] {e.Message}")));
        var type = CompileGenerated(new CSharpEmitter().Emit(config));

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));
        AssertPmfEqual(InterpreterPmf(compile.Program!, new Dict()), genPmf, "scatter-fold");
        Assert.Equal(new Rational(4, 8), genPmf[5]);
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

    // ════════════════════════════════════════════════════════════════════
    //  The Dog House — the whole graph: reel → fan-out[ lines ;
    //  bonus-branch(Count≥2) → fs-count → loop( draw → accumulate-wilds →
    //  apply-wilds → lines×2 ) ] → sink.  Tiny scale so the exact PMF (across
    //  fan-out + branch(aggregate) + loop + sticky-wild transforms + ×2
    //  multiplier) is enumerable and provably equal to the interpreter.
    // ════════════════════════════════════════════════════════════════════

    private static void EnsureTransform(string name, IFastPathTransform t)
    {
        if (TransformRegistry.TryGet(name) is null)
        {
            try { TransformRegistry.Register(name, t); }
            catch (InvalidOperationException) { /* registered by a parallel test */ }
        }
    }

    private static GraphConfig MiniDogHouse()
    {
        EnsureEvaluator("lines_dh", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = [new PaytableEntry { SymbolId = "A", Counts = [2], Payouts = ["4"] }] },
            new PaylineSet { Id = "ps", Paylines = [new Payline { Positions = [0, 0] }] },
            wildSymbolId: null));
        EnsureTransform("acc_dh", new BoardCellAccumulatorTransform(
            symbolFilter: "S", extractMode: CellExtractMode.Position, stateKey: "stickyPositions", mergeMode: CellMergeMode.Union));
        EnsureTransform("apply_dh", new BoardCellApplyTransform(
            stateKey: "stickyPositions", applyMode: CellApplyMode.OverlaySymbol, symbolId: "S"));

        var mulPort = new Port { Name = "multiplier", Type = PortType.Number, DefaultValue = Int(2) };

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "mini-doghouse",
            ReelStrips = [new ReelStrip { Id = "r0", Name = "R0", Symbols = ["S", "A"] }, new ReelStrip { Id = "r1", Name = "R1", Symbols = ["S", "A"] }],
            ReelSets = [new ReelSet { Id = "rs", Name = "Main", StripIds = ["r0", "r1"] }],
            BoardConfig = new BoardConfig { Rows = 1, Columns = 2 },
            StateSchema =
            [
                new StateFieldSchema { Name = "fsLeft", Type = "number" },
                new StateFieldSchema { Name = "stickyPositions", Type = "string[]" },
                new StateFieldSchema { Name = "__iter_loopFS__", Type = "number" },
                new StateFieldSchema { Name = "__wins_loopFS__", Type = "number" },
            ],
            Expressions = new Dictionary<string, Expression>
            {
                ["bonus-trigger"] = new CompareExpr
                {
                    Op = CompareOp.Gte,
                    Left = new AggregateExpr { Func = AggregateFunc.Count, StateKey = "board", ItemName = "cell", ItemType = ExprType.String, Predicate = Eq(Field("cell"), Str("S")) },
                    Right = Int(2),
                },
                ["fs-stop"] = new CompareExpr { Op = CompareOp.Gte, Left = Field("__iter_loopFS__"), Right = Field("fsLeft") },
            },
            Nodes =
            [
                new DrawNode { Id = "draw-spin", BoardStateKey = "board", Outputs = new PortMap { ["board"] = BoardPort } },
                new MapNode { Id = "eval-lines", TransformId = "lines_dh", Inputs = new PortMap { ["board"] = BoardPort }, Outputs = new PortMap { ["wins"] = WinsPort } },
                new BranchNode { Id = "branch-bonus", ConditionId = "bonus-trigger", Inputs = new PortMap { ["board"] = BoardPort }, Outputs = new PortMap { ["true"] = BoardPort, ["false"] = BoardPort } },
                new DrawNode { Id = "draw-fs-count", DrawWeights = [new DrawWeight { OutcomeId = "fs2", Weight = 1, Value = 2 }], Inputs = new PortMap { ["in"] = BoardPort }, Outputs = new PortMap { ["out"] = NumPort } },
                new PutStateNode { Id = "put-fs-left", StateKey = "fsLeft", Inputs = new PortMap { ["in"] = NumPort }, Outputs = new PortMap { ["out"] = NumPort } },
                new LoopNode { Id = "loopFS", MaxIterations = 10, StopConditionId = "fs-stop", Inputs = new PortMap { ["in"] = NumPort }, Outputs = new PortMap { ["body"] = NumPort, ["exit"] = WinsPort } },
                new DrawNode { Id = "draw-free-spin", Inputs = new PortMap { ["in"] = NumPort }, Outputs = new PortMap { ["board"] = BoardPort } },
                new MapNode { Id = "map-accumulate-wilds", TransformId = "acc_dh", Inputs = new PortMap { ["board"] = BoardPort }, Outputs = new PortMap { ["board"] = BoardPort } },
                new MapNode { Id = "map-apply-wilds", TransformId = "apply_dh", Inputs = new PortMap { ["board"] = BoardPort }, Outputs = new PortMap { ["board"] = BoardPort } },
                new MapNode { Id = "eval-free-lines", TransformId = "lines_dh", Inputs = new PortMap { ["board"] = BoardPort, ["multiplier"] = mulPort } },
                new MetricsSinkNode { Id = "sink", WinCap = 10_000, Inputs = new PortMap { ["wins"] = WinsPort } },
            ],
            Edges =
            [
                new Edge { Id = "e1", SourceNodeId = "draw-spin", SourcePort = "board", TargetNodeId = "eval-lines", TargetPort = "board" },
                new Edge { Id = "e3", SourceNodeId = "draw-spin", SourcePort = "board", TargetNodeId = "branch-bonus", TargetPort = "board" },
                new Edge { Id = "e4", SourceNodeId = "eval-lines", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
                new Edge { Id = "e6", SourceNodeId = "branch-bonus", SourcePort = "true", TargetNodeId = "draw-fs-count", TargetPort = "in" },
                new Edge { Id = "e7", SourceNodeId = "draw-fs-count", SourcePort = "out", TargetNodeId = "put-fs-left", TargetPort = "in" },
                new Edge { Id = "e8", SourceNodeId = "put-fs-left", SourcePort = "out", TargetNodeId = "loopFS", TargetPort = "in" },
                new Edge { Id = "e9", SourceNodeId = "loopFS", SourcePort = "body", TargetNodeId = "draw-free-spin", TargetPort = "in" },
                new Edge { Id = "e10", SourceNodeId = "draw-free-spin", SourcePort = "board", TargetNodeId = "map-accumulate-wilds", TargetPort = "board" },
                new Edge { Id = "e11", SourceNodeId = "map-accumulate-wilds", SourcePort = "board", TargetNodeId = "map-apply-wilds", TargetPort = "board" },
                new Edge { Id = "e12", SourceNodeId = "map-apply-wilds", SourcePort = "board", TargetNodeId = "eval-free-lines", TargetPort = "board" },
                new Edge { Id = "e13", SourceNodeId = "loopFS", SourcePort = "exit", TargetNodeId = "sink", TargetPort = "wins" },
            ],
        };
    }

    [Fact]
    public void MiniDogHouse_WholeGraph_GeneratedExactPmf_EqualsInterpreter()
    {
        var config = MiniDogHouse();
        var compile = new GraphCompiler().Compile(config);
        Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => $"[{e.Code}] {e.Message}")));

        var emit = new CSharpEmitter().Emit(config);
        Assert.True(emit.Supported, string.Join("; ", emit.Diagnostics));
        Assert.Contains("for (;;)", emit.Source);                  // free-spins loop
        Assert.Contains("TransformRegistry.TryGet", emit.Source);  // sticky-wild transforms
        Assert.Contains("__SumWinsMul", emit.Source);              // ×2 multiplier
        var type = CompileGenerated(emit);

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(new Dict());
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));

        AssertPmfEqual(InterpreterPmf(compile.Program!, new Dict()), genPmf, "mini-doghouse");
    }

    [Fact]
    public void FullDogHouse_EmitsCompilableCSharp()
    {
        // The real Dog House (5×4 reels, 20 paylines, free-spins loop, sticky
        // wilds, ×2 multiplier) now EMITS and the emitted C# COMPILES.  Its full
        // PMF is too large to enumerate exactly (12^5 reel × 20 free spins) —
        // the mini fixture above proves the constructs' semantics.
        var config = SlotMath.Core.Tests.Benchmarks.DogHouseNoPluginBenchmarkTests.CreateConfig();
        var emit = new CSharpEmitter().Emit(config);
        Assert.True(emit.Supported, "Dog House did not emit: " + string.Join("; ", emit.Diagnostics));
        Assert.Contains("for (;;)", emit.Source);
        Assert.Contains("TransformRegistry.TryGet", emit.Source);
        Assert.Contains("__SumWinsMul", emit.Source);

        var type = CompileGenerated(emit);
        Assert.NotNull(Activator.CreateInstance(type));
    }

    // ════════════════════════════════════════════════════════════════════
    //  Sticky wilds as a PURE subgraph (no C# transform): array-accumulator
    //  fold (collect wild positions, deduped via contains/append) + array-map
    //  (overlay wilds).  Proves array-fold / array-map / contains / append.
    // ════════════════════════════════════════════════════════════════════

    private static FoldExpr AccumulateSticky(string wild) => new()
    {
        StateKey = "board",
        AccName = "acc",
        ItemName = "sym",
        IndexName = "i",
        ItemType = ExprType.String,
        Init = Field("stickyPositions"),
        Body = new IfExpr
        {
            Condition = And(
                Eq(Field("sym"), Str(wild)),
                new NotExpr { Expr = new CallExpr { Function = "contains", Args = [Field("acc"), Field("i")] } }),
            ThenExpr = new CallExpr { Function = "append", Args = [Field("acc"), Field("i")] },
            ElseExpr = Field("acc"),
        },
    };

    private static MapExpr OverlayWilds(string wild) => new()
    {
        StateKey = "board",
        ItemName = "sym",
        IndexName = "i",
        ItemType = ExprType.String,
        Body = new IfExpr
        {
            Condition = new CallExpr { Function = "contains", Args = [Field("stickyPositions"), Field("i")] },
            ThenExpr = Str(wild),
            ElseExpr = Field("sym"),
        },
    };

    [Fact]
    public void StickyWilds_PureSubgraph_GeneratedExactPmf_EqualsInterpreter()
    {
        EnsureEvaluator("lines_sw", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = [new PaytableEntry { SymbolId = "A", Counts = [2], Payouts = ["4"] }] },
            new PaylineSet { Id = "ps", Paylines = [new Payline { Positions = [0, 0] }] },
            wildSymbolId: "W"));

        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "sticky-pure",
            ReelStrips = [new ReelStrip { Id = "r0", Name = "R0", Symbols = ["W", "A"] }, new ReelStrip { Id = "r1", Name = "R1", Symbols = ["W", "A"] }],
            ReelSets = [new ReelSet { Id = "rs", Name = "Main", StripIds = ["r0", "r1"] }],
            BoardConfig = new BoardConfig { Rows = 1, Columns = 2 },
            StateSchema =
            [
                new StateFieldSchema { Name = "stickyPositions", Type = "string[]" },
                new StateFieldSchema { Name = "__iter_loopSW__", Type = "number" },
                new StateFieldSchema { Name = "__wins_loopSW__", Type = "number" },
            ],
            Expressions = new Dictionary<string, Expression> { ["accumulate"] = AccumulateSticky("W"), ["overlay"] = OverlayWilds("W") },
            Nodes =
            [
                new LoopNode { Id = "loopSW", MaxIterations = 2, Outputs = new PortMap { ["body"] = NumPort, ["exit"] = WinsPort } },
                new DrawNode { Id = "draw-fs", Inputs = new PortMap { ["in"] = NumPort }, Outputs = new PortMap { ["board"] = BoardPort } },
                new ModifyStateNode { Id = "accumulate", ExpressionId = "accumulate", OutputKey = "stickyPositions", Inputs = new PortMap { ["board"] = BoardPort }, Outputs = new PortMap { ["board"] = BoardPort } },
                new ModifyStateNode { Id = "overlay", ExpressionId = "overlay", OutputKey = "board", Inputs = new PortMap { ["board"] = BoardPort }, Outputs = new PortMap { ["board"] = BoardPort } },
                new MapNode { Id = "eval", TransformId = "lines_sw", Inputs = new PortMap { ["board"] = BoardPort } },
                new MetricsSinkNode { Id = "sink", WinCap = 10_000, Inputs = new PortMap { ["wins"] = WinsPort } },
            ],
            Edges =
            [
                new Edge { Id = "e0", SourceNodeId = "loopSW", SourcePort = "body", TargetNodeId = "draw-fs", TargetPort = "in" },
                new Edge { Id = "e1", SourceNodeId = "draw-fs", SourcePort = "board", TargetNodeId = "accumulate", TargetPort = "board" },
                new Edge { Id = "e2", SourceNodeId = "accumulate", SourcePort = "board", TargetNodeId = "overlay", TargetPort = "board" },
                new Edge { Id = "e3", SourceNodeId = "overlay", SourcePort = "board", TargetNodeId = "eval", TargetPort = "board" },
                new Edge { Id = "e4", SourceNodeId = "loopSW", SourcePort = "exit", TargetNodeId = "sink", TargetPort = "wins" },
            ],
        };

        var initial = new Dict { ["stickyPositions"] = System.Array.Empty<object?>() };
        var compile = new GraphCompiler().Compile(config);
        Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => $"[{e.Code}] {e.Message}")));

        var emit = new CSharpEmitter().Emit(config);
        Assert.True(emit.Supported, string.Join("; ", emit.Diagnostics));
        Assert.Contains("__Append", emit.Source);   // array-accumulator fold
        Assert.Contains("__Contains", emit.Source);  // contains()
        var type = CompileGenerated(emit);

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(initial);
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));
        AssertPmfEqual(InterpreterPmf(compile.Program!, initial), genPmf, "sticky-pure");
    }

    // ════════════════════════════════════════════════════════════════════
    //  Mini Dog House with sticky wilds as a PURE subgraph (1-to-1): the whole
    //  game — fan-out + branch(aggregate) + loop + array-fold/map sticky wilds +
    //  ×2 multiplier — exact PMF ≡ interpreter at enumerable scale.
    // ════════════════════════════════════════════════════════════════════

    private static GraphConfig MiniPureDogHouse()
    {
        EnsureEvaluator("lines_dh2", new LinesEvaluator(
            new Paytable { Id = "pt", Entries = [new PaytableEntry { SymbolId = "A", Counts = [2], Payouts = ["4"] }] },
            new PaylineSet { Id = "ps", Paylines = [new Payline { Positions = [0, 0] }] },
            wildSymbolId: "W"));

        Port Board() => new() { Name = "board", Type = PortType.Board };
        var mulPort = new Port { Name = "multiplier", Type = PortType.Number, DefaultValue = Int(2) };

        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Id = "mini-pure-doghouse",
            ReelStrips = [new ReelStrip { Id = "r0", Name = "R0", Symbols = ["S", "A", "W"] }, new ReelStrip { Id = "r1", Name = "R1", Symbols = ["S", "A", "W"] }],
            ReelSets = [new ReelSet { Id = "rs", Name = "Main", StripIds = ["r0", "r1"] }],
            BoardConfig = new BoardConfig { Rows = 1, Columns = 2 },
            StateSchema =
            [
                new StateFieldSchema { Name = "fsLeft", Type = "number" },
                new StateFieldSchema { Name = "stickyPositions", Type = "string[]" },
                new StateFieldSchema { Name = "__iter_loopFS__", Type = "number" },
                new StateFieldSchema { Name = "__wins_loopFS__", Type = "number" },
            ],
            Expressions = new Dictionary<string, Expression>
            {
                ["bonus-trigger"] = new CompareExpr { Op = CompareOp.Gte, Left = new AggregateExpr { Func = AggregateFunc.Count, StateKey = "board", ItemName = "cell", ItemType = ExprType.String, Predicate = Eq(Field("cell"), Str("S")) }, Right = Int(2) },
                ["fs-stop"] = new CompareExpr { Op = CompareOp.Gte, Left = Field("__iter_loopFS__"), Right = Field("fsLeft") },
                ["accumulate_sticky"] = AccumulateSticky("W"),
                ["overlay_wilds"] = OverlayWilds("W"),
            },
            Nodes =
            [
                new DrawNode { Id = "draw-spin", BoardStateKey = "board", Outputs = new PortMap { ["board"] = Board() } },
                new MapNode { Id = "eval-lines", TransformId = "lines_dh2", Inputs = new PortMap { ["board"] = Board() }, Outputs = new PortMap { ["wins"] = WinsPort } },
                new BranchNode { Id = "branch-bonus", ConditionId = "bonus-trigger", Inputs = new PortMap { ["board"] = Board() }, Outputs = new PortMap { ["true"] = Board(), ["false"] = Board() } },
                new DrawNode { Id = "draw-fs-count", DrawWeights = [new DrawWeight { OutcomeId = "fs2", Weight = 1, Value = 2 }], Inputs = new PortMap { ["in"] = Board() }, Outputs = new PortMap { ["out"] = NumPort } },
                new PutStateNode { Id = "put-fs-left", StateKey = "fsLeft", Inputs = new PortMap { ["in"] = NumPort }, Outputs = new PortMap { ["out"] = NumPort } },
                new LoopNode { Id = "loopFS", MaxIterations = 10, StopConditionId = "fs-stop", Inputs = new PortMap { ["in"] = NumPort }, Outputs = new PortMap { ["body"] = NumPort, ["exit"] = WinsPort } },
                new DrawNode { Id = "draw-free-spin", Inputs = new PortMap { ["in"] = NumPort }, Outputs = new PortMap { ["board"] = Board() } },
                new ModifyStateNode { Id = "accumulate", ExpressionId = "accumulate_sticky", OutputKey = "stickyPositions", Inputs = new PortMap { ["board"] = Board() }, Outputs = new PortMap { ["board"] = Board() } },
                new ModifyStateNode { Id = "overlay", ExpressionId = "overlay_wilds", OutputKey = "board", Inputs = new PortMap { ["board"] = Board() }, Outputs = new PortMap { ["board"] = Board() } },
                new MapNode { Id = "eval-free-lines", TransformId = "lines_dh2", Inputs = new PortMap { ["board"] = Board(), ["multiplier"] = mulPort } },
                new MetricsSinkNode { Id = "sink", WinCap = 100_000, Inputs = new PortMap { ["wins"] = WinsPort } },
            ],
            Edges =
            [
                new Edge { Id = "e1", SourceNodeId = "draw-spin", SourcePort = "board", TargetNodeId = "eval-lines", TargetPort = "board" },
                new Edge { Id = "e3", SourceNodeId = "draw-spin", SourcePort = "board", TargetNodeId = "branch-bonus", TargetPort = "board" },
                new Edge { Id = "e4", SourceNodeId = "eval-lines", SourcePort = "wins", TargetNodeId = "sink", TargetPort = "wins" },
                new Edge { Id = "e6", SourceNodeId = "branch-bonus", SourcePort = "true", TargetNodeId = "draw-fs-count", TargetPort = "in" },
                new Edge { Id = "e7", SourceNodeId = "draw-fs-count", SourcePort = "out", TargetNodeId = "put-fs-left", TargetPort = "in" },
                new Edge { Id = "e8", SourceNodeId = "put-fs-left", SourcePort = "out", TargetNodeId = "loopFS", TargetPort = "in" },
                new Edge { Id = "e9", SourceNodeId = "loopFS", SourcePort = "body", TargetNodeId = "draw-free-spin", TargetPort = "in" },
                new Edge { Id = "e10", SourceNodeId = "draw-free-spin", SourcePort = "board", TargetNodeId = "accumulate", TargetPort = "board" },
                new Edge { Id = "e11", SourceNodeId = "accumulate", SourcePort = "board", TargetNodeId = "overlay", TargetPort = "board" },
                new Edge { Id = "e12", SourceNodeId = "overlay", SourcePort = "board", TargetNodeId = "eval-free-lines", TargetPort = "board" },
                new Edge { Id = "e13", SourceNodeId = "loopFS", SourcePort = "exit", TargetNodeId = "sink", TargetPort = "wins" },
            ],
        };
    }

    [Fact]
    public void MiniPureDogHouse_WholeGraph_GeneratedExactPmf_EqualsInterpreter()
    {
        var config = MiniPureDogHouse();
        var initial = new Dict { ["stickyPositions"] = System.Array.Empty<object?>() };
        var compile = new GraphCompiler().Compile(config);
        Assert.True(compile.IsValid, string.Join("; ", compile.Errors.Select(e => $"[{e.Code}] {e.Message}")));

        var emit = new CSharpEmitter().Emit(config);
        Assert.True(emit.Supported, string.Join("; ", emit.Diagnostics));
        Assert.Contains("__Append", emit.Source);   // pure sticky-wild accumulator
        Assert.Contains("__SumWinsMul", emit.Source); // ×2 multiplier
        var type = CompileGenerated(emit);

        var game = (ICompiledGame)Activator.CreateInstance(type)!;
        game.SetInitial(initial);
        var genPmf = ExactPmf.Enumerate(d => game.RunSpin(d));
        AssertPmfEqual(InterpreterPmf(compile.Program!, initial), genPmf, "mini-pure-doghouse");
    }

    [Fact]
    public void FullPureDogHouse_EmitsCompilableCSharp()
    {
        // The real Dog House with sticky wilds as a PURE subgraph (zero C#
        // molecule) emits and Roslyn-compiles.
        var config = SlotMath.Core.Tests.Benchmarks.DogHouseNoPluginBenchmarkTests.CreatePureStickyConfig();
        var emit = new CSharpEmitter().Emit(config);
        Assert.True(emit.Supported, "Pure Dog House did not emit: " + string.Join("; ", emit.Diagnostics));
        Assert.Contains("__Append", emit.Source);    // sticky accumulator fold
        Assert.Contains("__Contains", emit.Source);   // dedupe / overlay
        Assert.DoesNotContain("\"accumulate-wilds\"", emit.Source); // no C# transform
        var type = CompileGenerated(emit);
        Assert.NotNull(Activator.CreateInstance(type));
    }
}

/// <summary>
/// Deterministic level-c plugin used by the codegen plugin test: pays 5 when at
/// least two scatter "S" cells are on the board.  Pure function of the board, so
/// the exact interpreter and the generated code agree on its PMF.
/// </summary>
public sealed class CodegenScatterPlugin : IEvaluator
{
    public Win[] Evaluate(IReadOnlyDictionary<string, object?> state)
    {
        var cells = GridState.Cells(state);
        var count = 0;
        foreach (var c in cells)
            if (c as string == "S")
                count++;

        if (count < 2)
            return Array.Empty<Win>();

        return [new Win { SymbolId = "S", Count = count, Positions = Array.Empty<(int, int)>(), Payout = 5m, EvaluatorName = "ScatterPlugin" }];
    }
}
