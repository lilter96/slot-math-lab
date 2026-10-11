using System.Collections;
using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Games;

/// <summary>
///     One migrated game graph from <c>TestData/Games</c>, with every interpreter of the kernel behind one call each,
///     so that the tests of a game read as the claims they check. The graphs are authored in
///     <c>frontend/src/games</c> and exported by <c>frontend/scripts/export-game-fixtures.mjs</c>.
/// </summary>
internal sealed class GameGraph
{
    private readonly CompileResult _reference;

    private GameGraph(GraphConfig config)
    {
        Config = config;
        Compiled = Compile(config, true);
        _reference = Compile(config, false);
        WinCap = config.Nodes.OfType<MetricsSinkNode>().Single().WinCap!.Value;
    }

    public GraphConfig Config { get; }

    /// <summary>The graph as the API compiles it, with the compiled sampling tier.</summary>
    public CompileResult Compiled { get; }

    public BigInteger WinCap { get; }

    public BigInteger WinScale => Compiled.WinScale;

    private static string Data(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", "Games", file);

    public static GameGraph Load(string file) =>
        new(JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Data(file)), JsonOptions.Default)!);

    /// <summary>Rounds of the original game and its simulated statistics, written by <c>scripts/verification/tgslots</c>.</summary>
    public static JsonDocument Golden(string file) => JsonDocument.Parse(File.ReadAllText(Data(file)));

    private static CompileResult Compile(GraphConfig config, bool optimizeSampling,
        IReadOnlyList<MeasurementDefinition>? plan = null)
    {
        CompileResult result = new GraphCompiler(optimizeSampling: optimizeSampling).Compile(config, plan);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.NodeId}: {e.Message}")));
        return result;
    }

    /// <summary>The program of one sampling tier: compiled (the default everywhere) or the reference interpreter.</summary>
    public Slot<Dict, BigInteger> Program(bool compiledTier) => (compiledTier ? Compiled : _reference).Program!;

    /// <summary>A win as the interpreters return it, as a multiple of the bet.</summary>
    public Rational Bets(BigInteger win) => new(win, WinScale);

    /// <summary>One played round, as the play endpoint runs it.</summary>
    public (Rational Win, Dict State) Play(long seed, long round, bool traced = true, bool compiledTier = true)
    {
        (BigInteger value, Dict state) = SampledInterpreter.RunSingle(Program(compiledTier),
            new Dict { ["traceEnabled"] = traced }, seed, round);
        return (Bets(value), state);
    }

    /// <summary>One traced round in which every draw takes the outcome scripted for it, in the graph's draw order.</summary>
    public (Rational Win, Dict State) Replay(IEnumerable<int> draws)
    {
        var choices = new Queue<int>(draws);
        InterpreterResult<Dict, BigInteger> result =
            TrampolineInterpreter.Run(Program(false), new Dict { ["traceEnabled"] = true }, choices);
        Assert.Empty(choices);
        return (Bets(result.Value), result.FinalState);
    }

    public SampledResult<Dict> Sample(long rounds, long seed, int workers = 4, bool compiledTier = true) =>
        SampledInterpreter.Evaluate(Program(compiledTier), new Dict(),
            new SampledConfig
            {
                Seed = seed,
                MaxSpins = rounds,
                DegreeOfParallelism = workers,
                MaxWinCap = WinCap,
                WinScale = (double)WinScale
            });

    /// <summary>
    ///     A sampling run that also observes the quantities of <paramref name="plan" /> once per round. The mean of
    ///     each and the standard error of that mean are returned by the id of the quantity.
    /// </summary>
    public (SampledResult<Dict> Sampled, Dictionary<string, (double Mean, double StdErr)> Observed) Measure(long rounds,
        long seed,
        int workers, params MeasurementDefinition[] plan)
    {
        SampledResult<Dict> sampled = SampledInterpreter.Evaluate(Compile(Config, true, plan).Program!, new Dict(),
            new SampledConfig
            {
                Seed = seed,
                MaxSpins = rounds,
                DegreeOfParallelism = workers,
                MaxWinCap = WinCap,
                WinScale = (double)WinScale,
                Measurements = plan
            });
        foreach (MeasurementSnapshot snapshot in sampled.Measurements)
        {
            Assert.True(snapshot.Errors == 0 && snapshot.Count == rounds,
                $"{snapshot.Id}: {snapshot.Count} values, {snapshot.FirstError}");
        }

        return (sampled, sampled.Measurements.ToDictionary(snapshot => snapshot.Id,
            snapshot => (snapshot.Mean!.Value, snapshot.StdDev!.Value / System.Math.Sqrt(snapshot.Count))));
    }

    /// <summary>
    ///     A quantity for <see cref="Measure" />: a state expression read when the round is settled, or, with
    ///     <paramref name="beforeNode" />, just before that node of the graph is visited.
    /// </summary>
    public static MeasurementDefinition Quantity(string id, Expression value, string? beforeNode = null) =>
        new() { Id = id, Name = id, NodeId = beforeNode, Value = value };

    public static Expression State(string key) => new FieldAccessExpr { Target = "state", Path = [key] };

    // An evaluation that has to finish is bounded by what it may do (branches, units of work) and never by the clock:
    // the suites run side by side, and on a busy machine an evaluation of half a minute has been seen to take over
    // ten. A limit in time would make the same graph pass on one run and fail on the next.

    /// <summary>The classic exact interpreter: every outcome visited, or pruned below <paramref name="epsilon" />.</summary>
    public SlotMathReport Enumerate(Rational? epsilon = null, int maxBranches = 2_000_000)
    {
        ExactResult<Dict, BigInteger> result = ExactInterpreter.Evaluate(Compiled.Program!, new Dict(),
            StateHasher.CanonicalHash,
            new ExactConfig
            {
                Budget = new Budget { MaxBranches = maxBranches },
                EpsilonNumerator = epsilon?.Numerator ?? 0,
                EpsilonDenominator = epsilon?.Denominator ?? 1
            });
        return ExactMetrics.Compute(result.ValueDistribution(), WinCap, winScale: WinScale);
    }

    public StructuredExactResult Structured(StructuredExactOptions? options = null) =>
        StructuredExactEvaluator.Evaluate(Compiled, options ?? new StructuredExactOptions(), CancellationToken.None);

    /// <summary>The hybrid evaluator: exact within <paramref name="maxBranches" />, sampled beyond.</summary>
    public RegimeResult Hybrid(int maxBranches, long rounds, long seed) =>
        HybridEvaluator.Evaluate(Compiled.Program!, new Dict(), StateHasher.CanonicalHash,
            new RegimeConfig
            {
                Budget = new Budget { MaxBranches = maxBranches },
                SampledSeed = seed,
                SampledSpins = rounds,
                DegreeOfParallelism = 4,
                MaxWinCap = WinCap,
                WinScale = WinScale
            });

    /// <summary>
    ///     How every loop a played round entered came to its end, by loop. A loop that used up its iteration limit
    ///     ends with <c>modelLimit</c>: the limit is a guard of the model and not a rule of the game, so a round that
    ///     is played out by the rules has none.
    /// </summary>
    public static Dictionary<string, string> LoopExits(Dict state) => state
        .Where(field => field.Key.StartsWith("__exitReason_", StringComparison.Ordinal))
        .ToDictionary(field => field.Key, field => Text(field.Value));

    public static Rational Exact(RtpMetric metric) =>
        new(metric.RationalNumerator!.Value, metric.RationalDenominator!.Value);

    public static Rational Number(object? value) => value switch
    {
        BigInteger integer => new Rational(integer, 1),
        ExprValue { Kind: ExprType.Number } number => new Rational(number.NumberNumerator, number.NumberDenominator),
        _ => throw new InvalidOperationException($"Not a number: {value}")
    };

    public static string Text(object? value) => value switch
    {
        string text => text,
        ExprValue { Kind: ExprType.String or ExprType.Symbol } text => text.StringValue!,
        _ => throw new InvalidOperationException($"Not a text: {value}")
    };

    public static object?[] Items(object? value) => value switch
    {
        ExprValue { Kind: ExprType.Array } array => [.. array.ArrayValue!.Select(item => (object?)item)],
        IEnumerable sequence and not string => [.. sequence.Cast<object?>()],
        _ => throw new InvalidOperationException($"Not an array: {value}")
    };
}

/// <summary>
///     The statistical gate of D8 for one observed mean against its exact value: within four standard errors. Every
///     use is paired with a corrupted exact value that the same observation must reject.
/// </summary>
internal static class FourSigma
{
    public static bool Accepts(double exact, double observed, double stdErr) =>
        System.Math.Abs(observed - exact) <= 4 * stdErr;
}
