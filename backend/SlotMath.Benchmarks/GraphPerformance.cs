using System.Diagnostics;
using System.Text.Json;
using SlotMath.Core;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Serialization;

internal static class GraphPerformance
{
    public static void Run(string[] args)
    {
        string Option(string name, string fallback) { var i = Array.IndexOf(args, name); return i < 0 ? fallback : args[i + 1]; }
        var path = Option("--graph", "");
        var rounds = int.Parse(Option("--rounds", "20000"));
        var repeats = int.Parse(Option("--repeats", "3"));
        var workers = int.Parse(Option("--workers", "1"));
        var graph = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(path), JsonOptions.Default)!;
        var compileTimer = Stopwatch.StartNew();
        var compiled = new GraphCompiler(optimizeSampling: !args.Contains("--reference")).Compile(graph);
        if (!compiled.IsValid) throw new InvalidOperationException(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        compileTimer.Stop();
        var live = args.Contains("--live");
        int updates = 0;
        SampledResult<Dictionary<string, object?>> Execute(int n) => SampledInterpreter.Evaluate(compiled.Program!, new Dictionary<string, object?>(),
            new SampledConfig
            {
                Seed = 42,
                MaxSpins = n,
                DegreeOfParallelism = workers,
                WinScale = (double)compiled.WinScale,
                MaxWinCap = graph.Nodes.OfType<MetricsSinkNode>().Single().WinCap,
                ProgressCallback = live ? _ => Interlocked.Increment(ref updates) : null
            });
        var warmup = int.Parse(Option("--warmup", "20000"));
        Execute(warmup); // warmed code and alias tables, outside measurements
        var measurements = new List<object>();
        long AllocatedBytes() => workers == 1 ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalAllocatedBytes(true);
        for (var i = 0; i < repeats; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var bytes = AllocatedBytes();
            var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            updates = 0;
            var timer = Stopwatch.StartNew();
            var result = Execute(rounds);
            timer.Stop();
            var allocated = AllocatedBytes() - bytes;
            var snapshot = result.Stats.Snapshot();
            measurements.Add(new
            {
                elapsedMs = timer.Elapsed.TotalMilliseconds,
                roundsPerSecond = rounds / timer.Elapsed.TotalSeconds,
                cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
                allocatedBytes = allocated,
                bytesPerRound = allocated / (double)rounds,
                gc = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - collections[g]).ToArray(),
                updates,
                count = result.SpinsCompleted,
                rtp = snapshot.Mean,
                variance = snapshot.Variance,
                hitFrequency = snapshot.HitFrequency,
                maxWin = snapshot.MaxObserved,
                histogram = snapshot.AdaptiveHistogram
            });
            Console.WriteLine($"{i + 1}: {rounds / timer.Elapsed.TotalSeconds:N0} rounds/s; {allocated / (double)rounds:N0} B/round; RTP {snapshot.Mean:R}");
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
            streamScheme = "splitmix64-chunk-65536",
            compileMs = compileTimer.Elapsed.TotalMilliseconds,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            cpuCount = Environment.ProcessorCount,
            allocationCounter = workers == 1 ? "current-thread" : "process-total",
            measurements
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var output = Option("--report", "");
        if (output.Length > 0) File.WriteAllText(output, json + "\n");
    }
}
