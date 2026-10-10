// Isolated collector cost harness; launched by benchmark_measurement_cohorts.py.
// Snapshot/hash equality is checked separately. Timings never assert correctness.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

var rounds = int.Parse(args[1]);
var warmup = int.Parse(args[2]);
var repeats = int.Parse(args[3]);
var report = new List<object>();
var scenarios = new[]
{
    (Name: "frequent-19-cohorts", Frequency: 1, CohortCount: 19),
    (Name: "sparse-19-cohorts", Frequency: 101, CohortCount: 19),
    (Name: "dense-one-cohort", Frequency: 1, CohortCount: 1),
    (Name: "ungrouped", Frequency: 1, CohortCount: 0)
};
foreach (var (name, frequency, cohortCount) in scenarios)
{
    MeasurementDefinition[] plan = [new()
    {
        Id = "feature", Name = "Feature", NodeId = "value",
        Options = new()
        {
            Group = cohortCount > 0 ? new ConstantExpr { Kind = ConstantKind.String, Value = "binding" } : null,
            GroupLimit = 19, SupportLimit = 64, IndependentParents = true
        }
    }];
    var keys = Enumerable.Range(0, 19).Select(i => i.ToString()).ToArray();
    var binding = new MeasurementBinding<int>(i => ExprValue.Number(i % 7), null,
        Group: cohortCount > 0 ? i => ExprValue.String(keys[i % cohortCount]) : null);

    MeasurementSnapshot Run(int count)
    {
        var collector = new MeasurementCollector(plan);
        for (var round = 0; round < count; round++)
        {
            collector.Begin(round);
            if (round % frequency == 0) collector.Point(0, "value", round / frequency, binding);
            collector.Commit();
        }
        return collector.Total[0].Snapshot("feature");
    }

    Run(warmup);
    var timing = new List<object>();
    MeasurementSnapshot? last = null;
    for (var repeat = 0; repeat < repeats; repeat++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        last = Run(rounds);
        timer.Stop();
        timing.Add(new { elapsedMs = timer.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytes });
    }
    report.Add(new { name, rounds, frequency, cohortCount, timing, evidence = last });
}

var artifact = typeof(SlotMath.Core.Math.SampledInterpreter).Assembly.Location;
File.WriteAllText(args[0], JsonSerializer.Serialize(new
{
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    cpuCount = Environment.ProcessorCount, warmup, repeats,
    coreBinarySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(artifact))),
    report
}));
