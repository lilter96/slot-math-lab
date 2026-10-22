using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlotMath.Core;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using SlotMath.Core.Serialization;

namespace SlotMath.Benchmarks;

internal static class GraphPerformance
{
    private static string? DistributionFingerprint(Dist<BigInteger>? distribution)
    {
        if (distribution is null)
        {
            return null;
        }

        StringBuilder canonical = new StringBuilder().Append(distribution.Denominator).Append('\n');
        foreach (Dist<BigInteger>.Entry atom in distribution.Entries.OrderBy(atom => atom.Value))
        {
            canonical.Append(atom.Value).Append(':').Append(atom.Numerator).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <summary>Structured exact evaluation: the exact rational RTP of a graph with too many outcomes to enumerate.</summary>
    private static void Exact(CompileResult compiled, GraphConfig graph, int repeats, string report,
        StructuredExactOptions options)
    {
        var measurements = new List<object>();
        for (var i = 0; i < repeats; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var bytes = GC.GetTotalAllocatedBytes(true);
            var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            TimeSpan pause = GC.GetTotalPauseDuration();
            var timer = Stopwatch.StartNew();
            TimeSpan cpu = Process.GetCurrentProcess().TotalProcessorTime;
            StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled, options);
            timer.Stop();
            var allocated = GC.GetTotalAllocatedBytes(true) - bytes;
            var gc = string.Join('/', Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - collections[g]));
            var pauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds;
            if (!result.Succeeded)
            {
                Console.WriteLine($"declined: {result.DeclinedReason} ({result.Elapsed.TotalMilliseconds:N0} ms)");
                return;
            }

            RtpMetric rtp = result.Rtp(compiled.WinScale);
            Console.WriteLine(
                $"{i + 1}: {result.Elapsed.TotalMilliseconds:N0} ms; RTP {rtp.DisplayValue:R}; distribution {(result.Distribution is { } d ? $"{d.Count} values" : "not built")}; " +
                $"{result.RandomVariables} variables; {result.Terms:N0} terms; {result.Laws:N0} laws; work {result.Work:N0}; {allocated / 1048576.0:N0} MiB allocated; gc {gc}, {pauseMs:N0} ms paused; heap {GC.GetTotalMemory(false) / 1048576.0:N0} MiB");
            measurements.Add(new
            {
                elapsedMs = result.Elapsed.TotalMilliseconds,
                wallMs = timer.Elapsed.TotalMilliseconds,
                cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
                rtp = rtp.DisplayValue,
                rationalRtp = $"{rtp.RationalNumerator}/{rtp.RationalDenominator}",
                distributionValues = result.Distribution?.Count,
                distributionSha256 = DistributionFingerprint(result.Distribution),
                rationalSecondMoment = result.SecondMoment?.ToString(),
                rationalThirdMoment = result.ThirdMoment?.ToString(),
                rationalFourthMoment = result.FourthMoment?.ToString(),
                randomVariables = result.RandomVariables,
                terms = result.Terms,
                laws = result.Laws,
                work = result.Work,
                diagnostics = result.Diagnostics,
                requestedEvidence = result.RequestedEvidence.ToString(),
                requestedSolverPlan = result.RequestedSolverPlan.ToString(),
                attempts = result.Attempts,
                storage = result.Storage,
                solverNotes = result.SolverNotes,
                loops = result.Loops.Select(loop => new
                {
                    loop.LoopId,
                    loop.Evaluation,
                    loop.IterationCap,
                    loop.Runs,
                    loop.States
                }),
                allocatedBytes = allocated,
                gcPauseMs = pauseMs,
                hitFrequency = result.HitFrequency?.ToDouble(),
                rationalHitFrequency =
                    result.HitFrequency is { } hits ? $"{hits.Numerator}/{hits.Denominator}" : null
            });
        }

        if (report.Length > 0)
        {
            File.WriteAllText(report,
                JsonSerializer.Serialize(
                    new
                    {
                        runtime = Environment.Version.ToString(),
                        processors = Environment.ProcessorCount,
                        configHash = ConfigHash.Compute(graph),
                        winScale = compiled.WinScale.ToString(),
                        coreBinarySha256 =
                            Convert.ToHexStringLower(
                                SHA256.HashData(File.ReadAllBytes(typeof(StructuredExactEvaluator).Assembly.Location))),
                        options = new
                        {
                            options.UseTransformConvolution,
                            options.LoopMatrixMaxStates,
                            options.MaxWork,
                            options.DistributionWork,
                            options.LoopLawWork,
                            options.MomentOrder,
                            options.HigherMomentWork,
                            Evidence = options.Evidence.ToString(),
                            options.TrackStorage,
                            SolverPlan = options.SolverPlan.ToString()
                        },
                        measurements
                    }, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
    }

    public static void Run(string[] args)
    {
        string Option(string name, string fallback)
        {
            var i = Array.IndexOf(args, name);
            return i < 0 ? fallback : args[i + 1];
        }

        var path = Option("--graph", "");
        var rounds = int.Parse(Option("--rounds", "20000"));
        var repeats = int.Parse(Option("--repeats", "3"));
        var workers = int.Parse(Option("--workers", "1"));
        GraphConfig graph = args.Contains("--numeric-sum")
            ? NumericSumBenchmark.Graph(int.Parse(Option("--numeric-sum", "1024"),
                CultureInfo.InvariantCulture))
            : JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(path), JsonOptions.Default)!;
        SamplingOptions.Tier = Enum.Parse<SamplingTier>(Option("--tier", "Tiered"), true);
        var compileTimer = Stopwatch.StartNew();
        CompileResult compiled = new GraphCompiler(optimizeSampling: !args.Contains("--reference")).Compile(graph);
        if (!compiled.IsValid)
        {
            throw new InvalidOperationException(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        }

        compileTimer.Stop();
        if (args.Contains("--exact") && args.Contains("--allocations"))
        {
            using var profile = new AllocationProfile();
            StructuredExactEvaluator.Evaluate(compiled);
            profile.Report(1);
            return;
        }

        if (args.Contains("--exact"))
        {
            Exact(compiled, graph, repeats, Option("--report", ""), new StructuredExactOptions
            {
                UseTransformConvolution = !args.Contains("--direct-convolution"),
                Evidence = Enum.Parse<StructuredEvidence>(Option("--evidence", "Report"), true),
                SolverPlan = Enum.Parse<StructuredSolverPlan>(Option("--solver-plan", "Auto"), true),
                TrackStorage = args.Contains("--storage"),
                LoopMatrixMaxStates = args.Contains("--no-loop-matrices") ? 0 : 32,
                DistributionWork = long.Parse(Option("--distribution-work", "4000000"),
                    CultureInfo.InvariantCulture),
                LoopLawWork = long.Parse(Option("--loop-law-work", "2000000"), CultureInfo.InvariantCulture),
                MomentOrder = int.Parse(Option("--moment-order", "2"), CultureInfo.InvariantCulture),
                HigherMomentWork = long.Parse(Option("--higher-moment-work", "8000000"), CultureInfo.InvariantCulture)
            });
            return;
        }

        var live = args.Contains("--live");
        var updates = 0;

        SampledResult<Dictionary<string, object?>> Execute(int n)
        {
            return SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(),
                new SampledConfig
                {
                    Seed = 42,
                    MaxSpins = n,
                    DegreeOfParallelism = workers,
                    WinScale = (double)compiled.WinScale,
                    MaxWinCap = graph.Nodes.OfType<MetricsSinkNode>().Single().WinCap,
                    ProgressCallback = live ? _ => Interlocked.Increment(ref updates) : null
                });
        }

        var warmup = int.Parse(Option("--warmup", "20000"));
        Execute(warmup); // warmed code and alias tables, outside measurements
        if (args.Contains("--allocations"))
        {
            using var profile = new AllocationProfile();
            Execute(rounds);
            profile.Report(rounds);
            return;
        }

        var measurements = new List<object>();

        long AllocatedBytes()
        {
            return workers == 1 ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalAllocatedBytes(true);
        }

        for (var i = 0; i < repeats; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var bytes = AllocatedBytes();
            var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            TimeSpan cpu = Process.GetCurrentProcess().TotalProcessorTime;
            TimeSpan pause = GC.GetTotalPauseDuration();
            updates = 0;
            var timer = Stopwatch.StartNew();
            SampledResult<Dictionary<string, object?>> result = Execute(rounds);
            timer.Stop();
            var allocated = AllocatedBytes() - bytes;
            var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds;
            var pauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds;
            var gcCounts = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - collections[g]).ToArray();
            StreamingStatsSnapshot snapshot = result.Stats.Snapshot();
            measurements.Add(new
            {
                elapsedMs = timer.Elapsed.TotalMilliseconds,
                roundsPerSecond = rounds / timer.Elapsed.TotalSeconds,
                cpuMs,
                gcPauseMs = pauseMs,
                allocatedBytes = allocated,
                bytesPerRound = allocated / (double)rounds,
                gc = gcCounts,
                updates,
                count = result.SpinsCompleted,
                rtp = snapshot.Mean,
                variance = snapshot.Variance,
                hitFrequency = snapshot.HitFrequency,
                maxWin = snapshot.MaxObserved,
                capHits = snapshot.CapHits,
                maxWinCap = snapshot.MaxWinCap,
                histogram = snapshot.AdaptiveHistogram,
                loopTerminations = result.Execution?.LoopTerminations
            });
            Console.WriteLine(
                $"{i + 1}: {rounds / timer.Elapsed.TotalSeconds:N0} rounds/s; {allocated / (double)rounds:N0} B/round; RTP {snapshot.Mean:R}; cpu {cpuMs / timer.Elapsed.TotalMilliseconds:N2} cores; gc {string.Join('/', gcCounts)}, pause {pauseMs:N0} ms of {timer.Elapsed.TotalMilliseconds:N0} ms");
        }

        var report = new
        {
            configHash = ConfigHash.Compute(graph),
            engine = compiled.SamplingEngine,
            seed = 42,
            rounds,
            warmup,
            workers,
            live,
            winScale = compiled.WinScale.ToString(),
            streamScheme = "splitmix64-chunk-65536",
            compileMs = compileTimer.Elapsed.TotalMilliseconds,
            runtime = RuntimeInformation.FrameworkDescription,
            cpuCount = Environment.ProcessorCount,
            allocationCounter = workers == 1 ? "current-thread" : "process-total",
            measurements
        };
        // The last bucket of a win histogram has no upper bound.
        var json = JsonSerializer.Serialize(report,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            });
        var output = Option("--report", "");
        if (output.Length > 0)
        {
            File.WriteAllText(output, json + "\n");
        }
    }
}
