using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Serialization;

namespace SlotMath.Benchmarks;

internal static class TensorBenchmark
{
    private static Metrics Reduce(Rational[] values)
    {
        Rational mean = Rational.Zero, second = Rational.Zero, mass = Rational.Zero;
        for (var index = 0; index < values.Length; index++)
        {
            mean += index * values[index];
            second += (long)index * index * values[index];
            mass += values[index];
        }

        if (mass != Rational.One)
        {
            throw new ArithmeticException("Benchmark probability is incomplete.");
        }

        return new Metrics(mean, second, Rational.One - values[0]);
    }

    private static string Fingerprint(Rational[] values) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('\n', values.Select(value => value.ToString())))));

    public static void Run(string[] args)
    {
        string Option(string name, string fallback)
        {
            var at = Array.IndexOf(args, name);
            return at < 0 ? fallback : args[at + 1];
        }

        var mode = Option("--tensor-mode", "tensor");
        var repeats = int.Parse(Option("--repeats", "6"), CultureInfo.InvariantCulture);
        if (mode is not ("tensor" or "sparse" or "structured") || repeats is < 1 or > 100)
        {
            throw new ArgumentException("Choose tensor/sparse/structured mode and 1..100 repetitions.");
        }

        var components = int.Parse(Option("--tensor-components", "4"), CultureInfo.InvariantCulture);
        var inputKind = Option("--tensor-input", "independent");
        if (inputKind is not ("independent" or "correlated"))
        {
            throw new ArgumentException("Choose independent/correlated input.");
        }

        var sparsity = Option("--tensor-sparsity", "dense");
        if (sparsity is not ("dense" or "sparse"))
        {
            throw new ArgumentException("Choose dense/sparse local matrices.");
        }

        var baseline = GC.GetTotalMemory(true);
        var fixture = new TensorWorkload(components, inputKind == "correlated", sparsity == "sparse");
        ExactTensorOperator? tensor = mode == "tensor"
            ? ExactTensorOperator.Discrete([new TensorProductTerm(1, fixture.Factors)])
            : null;
        ExactTensorWorkspace? workspace = tensor?.CreateWorkspace();
        ExactSparseMatrix? sparse = mode == "sparse" ? fixture.Expand() : null;
        CompileResult? compiled = mode == "structured" ? new GraphCompiler().Compile(fixture.Graph) : null;
        if (compiled is { IsValid: false })
        {
            throw new InvalidOperationException(string.Join("; ", compiled.Errors.Select(error => error.Message)));
        }

        var retainedDelta = GC.GetTotalMemory(true) - baseline;
        var expectedHash = Fingerprint(fixture.Oracle);
        Metrics expectedMetrics = Reduce(fixture.Oracle);
        var destination = new Rational[fixture.States];
        var measurements = new List<object>();
        for (var repeat = 0; repeat < repeats; repeat++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            TimeSpan cpu = Process.GetCurrentProcess().TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            Metrics metrics;
            long work, products;
            StructuredExactResult? structured = null;
            if (tensor is not null)
            {
                ExactOperatorApplication applied = tensor.Apply(fixture.Input, destination, workspace!);
                work = applied.Work;
                products = applied.Products;
                metrics = Reduce(destination);
            }
            else if (sparse is not null)
            {
                ExactOperatorApplication applied = sparse.Apply(fixture.Input, destination);
                work = applied.Work;
                products = applied.Products;
                metrics = Reduce(destination);
            }
            else
            {
                structured = StructuredExactEvaluator.Evaluate(compiled!, new StructuredExactOptions
                {
                    DistributionWork = 100000000,
                    MaxTime = TimeSpan.FromSeconds(30)
                });
                if (!structured.Succeeded || structured.Distribution is null || !structured.PrunedMass.IsZero)
                {
                    throw new InvalidOperationException("Symbolic factor baseline did not retain a complete law: " +
                                                        structured.DeclinedReason);
                }

                metrics = new Metrics(structured.ExpectedWin, structured.SecondMoment!.Value,
                    structured.HitFrequency!.Value);
                work = structured.Work;
                products = -1;
            }

            clock.Stop();
            var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
            var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds;
            var completedCollections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            var peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64;
            if (structured is not null)
            {
                Array.Fill(destination, Rational.Zero);
                Dist<BigInteger> distribution = structured.Distribution!;
                foreach (Dist<BigInteger>.Entry atom in distribution.Entries)
                {
                    destination[(int)atom.Value] = new Rational(atom.Numerator, distribution.Denominator);
                }
            }

            var actualHash = Fingerprint(destination);
            if (expectedHash != actualHash || metrics != expectedMetrics)
            {
                throw new ArithmeticException(
                    "Benchmark variants do not match the complete independent probability oracle.");
            }

            measurements.Add(new
            {
                elapsedMs = clock.Elapsed.TotalMilliseconds,
                cpuMs,
                allocatedBytes = bytes,
                work,
                products,
                probabilitySha256 = actualHash,
                rationalMean = metrics.Mean.ToString(),
                rationalSecondMoment = metrics.Second.ToString(),
                rationalHits = metrics.Hits.ToString(),
                gc = Enumerable.Range(0, 3)
                    .Select(generation => completedCollections[generation] - collections[generation]).ToArray(),
                processLifetimePeakWorkingSetBytes = peakWorkingSet
            });
            Console.WriteLine(
                $"{mode} {repeat + 1}: {clock.Elapsed.TotalMilliseconds:F3} ms, {bytes} B, {fixture.States} states; complete law verified");
        }

        var report = new
        {
            mode,
            inputKind,
            sparsity,
            components,
            states = fixture.States,
            runtime = Environment.Version.ToString(),
            processors = Environment.ProcessorCount,
            configHash = ConfigHash.Compute(fixture.Graph),
            inputSha256 = Fingerprint(fixture.Input),
            expectedProbabilitySha256 = expectedHash,
            fixture.InitialLawFactors,
            coreBinarySha256 =
                Convert.ToHexStringLower(
                    SHA256.HashData(File.ReadAllBytes(typeof(ExactTensorOperator).Assembly.Location))),
            benchmarkBinarySha256 =
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(TensorBenchmark).Assembly.Location))),
            operatorNonzeroEntries = tensor?.StoredNonzeroEntries ?? sparse?.NonzeroEntries,
            tensorScratchEntries = workspace?.ScratchEntries,
            setupRetainedManagedDeltaBytes = retainedDelta,
            note =
                "Every timed path returns the full encoded-state law plus exact mean/second moment/hit frequency. Setup is outside timing. " +
                "Retained delta includes runtime caches; process peak working set includes setup. They are not isolated kernel peak-memory measurements. " +
                "The structured baseline authors initial draws and conditional per-row draws as generic JSON atoms and uses the existing symbolic factoring engine. " +
                "The correlated case starts from a joint draw; only the transition operator factors, not the initial probability vector. " +
                "Linear operators receive a prebuilt input vector and cache their factorization proof; the structured entrypoint rebuilds its symbolic model and input law. " +
                "This is a repeated-evaluation comparison, not evidence that every compiled graph should select the tensor backend.",
            measurements
        };
        var path = Option("--report", "");
        if (path.Length > 0)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
    }

    private readonly record struct Metrics(Rational Mean, Rational Second, Rational Hits);
}
