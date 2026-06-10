using System.Diagnostics;
using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;

// ═══════════════════════════════════════════════════════════════════════════
//  SlotMath.Benchmarks — engine throughput scenarios
//
//  Stopwatch-based micro-harness (not BenchmarkDotNet, to keep CI light).
//  Run with:  dotnet run -c Release --project SlotMath.Benchmarks
//
//  Scenarios:
//    1. sampled-loop   : hand-built 100-iteration loop program, Monte Carlo
//    2. sampled-graph  : graph-compiled coin-flip loop (the API hot path)
//    3. sampled-reels  : graph-compiled 3-reel slot (large uniform draw)
//    4. exact-retrigger: free-spins-with-retrigger loop, exact interpreter
//    5. exact-reel     : 512-combination reel product, exact interpreter
// ═══════════════════════════════════════════════════════════════════════════

var results = new List<(string Name, double Ms, string Detail)>();

Run("sampled-loop (50k spins × 100 iters)", () =>
{
    var program = BuildHandLoop(iterations: 100);
    var config = new SampledConfig { Seed = 7, MaxSpins = 50_000 };
    var r = SampledInterpreter.Evaluate(program, new LoopState(0, BigInteger.Zero), config);
    return $"mean={r.Stats.Mean:F3}";
});

Run("sampled-graph (20k spins, coin-flip loop ×100)", () =>
{
    var graph = BuildCoinFlipGraph(maxIterations: 100);
    var compiled = new GraphCompiler().Compile(graph);
    if (!compiled.IsValid)
        throw new InvalidOperationException(string.Join("; ", compiled.Errors.Select(e => e.Message)));
    var config = new SampledConfig { Seed = 7, MaxSpins = 20_000 };
    var r = SampledInterpreter.Evaluate(
        compiled.Program!, new Dictionary<string, object?>(), config);
    return $"mean={r.Stats.Mean:F3}";
});

Run("sampled-reels (5k spins, 3×32-symbol reels)", () =>
{
    var graph = BuildReelGraph(stripLength: 32);
    var compiled = new GraphCompiler().Compile(graph);
    if (!compiled.IsValid)
        throw new InvalidOperationException(string.Join("; ", compiled.Errors.Select(e => e.Message)));
    var config = new SampledConfig { Seed = 7, MaxSpins = 5_000 };
    var r = SampledInterpreter.Evaluate(
        compiled.Program!, new Dictionary<string, object?>(), config);
    return $"mean={r.Stats.Mean:F3}";
});

Run("exact-retrigger (free spins, start=8, award cap=40)", () =>
{
    var program = BuildRetriggerGame(startSpins: 8, maxAwarded: 40);
    var r = ExactInterpreter.Evaluate(
        program, new RetriggerState(8, 8, BigInteger.Zero), s => s.Hash(),
        new ExactConfig { Budget = new SlotMath.Core.Math.Regime.Budget { MaxBranches = 500_000 } });
    var (num, den) = r.ValueDistribution().ExpectedBigIntegerValue();
    return $"EV={num}/{den}≈{(double)num / (double)den:F4} branches={r.Stats.TotalBranchesEvaluated:N0} hits={r.Stats.CacheHits}";
});

Run("exact-reel (512 combos × payout draw)", () =>
{
    var program = BuildExactReelGame();
    var r = ExactInterpreter.Evaluate(
        program, new LoopState(0, BigInteger.Zero), s => s.Hash(),
        new ExactConfig());
    var (num, den) = r.ValueDistribution().ExpectedBigIntegerValue();
    return $"EV={num}/{den} branches={r.Stats.TotalBranchesEvaluated:N0}";
});

Console.WriteLine();
Console.WriteLine("── Summary ─────────────────────────────────────────────");
foreach (var (name, ms, detail) in results)
    Console.WriteLine($"{name,-50} {ms,10:F1} ms   {detail}");

return;

void Run(string name, Func<string> action)
{
    // Warmup (JIT) with one cold pass, then measure.
    Console.WriteLine($"▶ {name}");
    try
    {
        var detail = action();
        var sw = Stopwatch.StartNew();
        detail = action();
        sw.Stop();
        results.Add((name, sw.Elapsed.TotalMilliseconds, detail));
        Console.WriteLine($"  {sw.Elapsed.TotalMilliseconds:F1} ms — {detail}");
    }
    catch (Exception ex)
    {
        results.Add((name, double.NaN, $"FAILED: {ex.Message}"));
        Console.WriteLine($"  FAILED — {ex.Message}");
    }
}

// ── Hand-built programs ─────────────────────────────────────────────────

static Slot<LoopState, BigInteger> BuildHandLoop(int iterations)
{
    var weights = WeightSet.FromIntegers(new[] { 1, 1, 2 });
    var body =
        from i in Slot.Draw<LoopState>(_ => weights)
        from _ in Slot.Modify<LoopState>(s => new LoopState(s.Count + 1, s.Total + i))
        select Unit.Value;

    return Slot.Loop<LoopState>(s => s.Count >= iterations, body)
        .SelectMany(_ => Slot.GetState<LoopState, BigInteger>(s => s.Total));
}

static Slot<RetriggerState, BigInteger> BuildRetriggerGame(int startSpins, int maxAwarded)
{
    // Each spin: 70% win 0, 20% win 5, 10% retrigger (+2 spins, total award capped).
    // Naive tree is 3^40 leaves — only state memoisation makes this tractable.
    var weights = WeightSet.FromIntegers(new[] { 7, 2, 1 });
    var body =
        from outcome in Slot.Draw<RetriggerState>(_ => weights)
        from _ in Slot.Modify<RetriggerState>(s =>
        {
            if (outcome == 0) return s with { Left = s.Left - 1 };
            if (outcome == 1) return s with { Left = s.Left - 1, Total = s.Total + 5 };
            var extra = System.Math.Min(2, maxAwarded - s.Awarded);
            return s with { Left = s.Left - 1 + extra, Awarded = s.Awarded + extra };
        })
        select Unit.Value;

    return Slot.Loop<RetriggerState>(s => s.Left <= 0, body)
        .SelectMany(_ => Slot.GetState<RetriggerState, BigInteger>(s => s.Total));
}

static Slot<LoopState, BigInteger> BuildExactReelGame()
{
    var reel = WeightSet.FromIntegers(new[] { 1, 1, 1, 1, 1, 1, 1, 1 });
    return
        from a in Slot.Draw<LoopState>(_ => reel)
        from b in Slot.Draw<LoopState>(_ => reel)
        from c in Slot.Draw<LoopState>(_ => reel)
        select new BigInteger(a == b && b == c ? 50 : a == b ? 5 : 0);
}

// ── Graph configs ───────────────────────────────────────────────────────

static GraphConfig BuildCoinFlipGraph(int maxIterations)
{
    return new GraphConfig
    {
        SchemaVersion = "1.0.0",
        Id = "bench-coin-flip",
        Nodes = new Node[]
        {
            new LoopNode
            {
                Id = "loop",
                Label = "Loop",
                MaxIterations = maxIterations,
                Outputs = new Dictionary<string, Port>
                {
                    ["body"] = new() { Name = "body", Type = PortType.Trigger },
                    ["exit"] = new() { Name = "exit", Type = PortType.Number },
                },
            },
            new DrawNode
            {
                Id = "draw",
                Label = "Flip",
                DrawWeights = new[]
                {
                    new DrawWeight { OutcomeId = "heads", Weight = 1, Value = 2 },
                    new DrawWeight { OutcomeId = "tails", Weight = 1, Value = 0 },
                },
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Trigger },
                },
                Outputs = new Dictionary<string, Port>
                {
                    ["value"] = new() { Name = "value", Type = PortType.Number },
                },
            },
            new MetricsSinkNode
            {
                Id = "sink",
                Label = "Sink",
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Number },
                },
            },
        },
        Edges = new[]
        {
            new Edge { Id = "e1", SourceNodeId = "loop", SourcePort = "body", TargetNodeId = "draw", TargetPort = "in" },
            new Edge { Id = "e2", SourceNodeId = "loop", SourcePort = "exit", TargetNodeId = "sink", TargetPort = "in" },
        },
    };
}

static GraphConfig BuildReelGraph(int stripLength)
{
    var symbols = Enumerable.Range(0, stripLength)
        .Select(i => $"sym-{i % 8}")
        .ToArray();

    return new GraphConfig
    {
        SchemaVersion = "1.0.0",
        Id = "bench-reels",
        Symbols = Enumerable.Range(0, 8)
            .Select(i => new Symbol { Id = $"sym-{i}", Name = $"S{i}", Kind = SymbolKind.Standard })
            .ToArray(),
        ReelStrips = new[]
        {
            new ReelStrip { Id = "r1", Name = "R1", Symbols = symbols },
            new ReelStrip { Id = "r2", Name = "R2", Symbols = symbols },
            new ReelStrip { Id = "r3", Name = "R3", Symbols = symbols },
        },
        ReelSets = new[]
        {
            new ReelSet { Id = "rs", Name = "Main", StripIds = new[] { "r1", "r2", "r3" } },
        },
        BoardConfig = new BoardConfig { Rows = 3, Columns = 3 },
        Nodes = new Node[]
        {
            new DrawNode
            {
                Id = "draw",
                Label = "Spin",
                Outputs = new Dictionary<string, Port>
                {
                    ["board"] = new() { Name = "board", Type = PortType.Board },
                },
            },
            new MetricsSinkNode
            {
                Id = "sink",
                Label = "Sink",
                Inputs = new Dictionary<string, Port>
                {
                    ["in"] = new() { Name = "in", Type = PortType.Board },
                },
            },
        },
        Edges = new[]
        {
            new Edge { Id = "e1", SourceNodeId = "draw", SourcePort = "board", TargetNodeId = "sink", TargetPort = "in" },
        },
    };
}

internal readonly record struct LoopState(int Count, BigInteger Total)
{
    public BigInteger Hash() => new BigInteger(Count) * 1_000_003 + Total;
}

internal readonly record struct RetriggerState(int Left, int Awarded, BigInteger Total)
{
    public BigInteger Hash() =>
        (new BigInteger(Awarded) * 1024 + Left) * 1_000_003 + Total;
}
