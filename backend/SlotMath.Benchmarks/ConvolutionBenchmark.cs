using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime;
using System.Security.Cryptography;
using System.Text.Json;
using SlotMath.Core.Math;

namespace SlotMath.Benchmarks;

/// <summary>Measures the actual integer kernel, including admission and CRT, against an independent polynomial oracle.</summary>
internal static class ConvolutionBenchmark
{
    private static BigInteger[] Direct(BigInteger[] left, BigInteger[] right, long pairLimit)
    {
        (BigInteger value, int index)[] a = left.Select((value, index) => (value, index))
            .Where(atom => !atom.value.IsZero).ToArray();
        (BigInteger value, int index)[] b = right.Select((value, index) => (value, index))
            .Where(atom => !atom.value.IsZero).ToArray();
        if ((long)a.Length * b.Length > pairLimit)
        {
            throw new ArgumentException("The direct control exceeds the declared pair allowance.");
        }

        var result = new BigInteger[left.Length + right.Length - 1];
        foreach ((BigInteger value, int index) x in a)
        {
            foreach ((BigInteger value, int index) y in b)
            {
                result[x.index + y.index] += x.value * y.value;
            }
        }

        return result;
    }

    private static string Hash(BigInteger[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (BigInteger value in values)
        {
            var bytes = value.ToByteArray(true, true);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static void Run(string[] args)
    {
        string Option(string name, string fallback)
        {
            var at = Array.IndexOf(args, name);
            return at < 0 ? fallback :
                at + 1 < args.Length ? args[at + 1] :
                throw new ArgumentException($"Missing value for {name}.");
        }

        int Number(string name, string fallback)
        {
            return int.Parse(Option(name, fallback), CultureInfo.InvariantCulture);
        }

        var spec = new Specification(Number("--length", "4096"), Number("--right-length", Option("--length", "4096")),
            Number("--bits", "32"), Number("--stride", "1"));
        var mode = Option("--convolution-mode", "auto");
        var repeats = Number("--repeats", "6");
        var warmups = Number("--warmups", "2");
        var pairLimit = long.Parse(Option("--max-direct-pairs", "67108864"), CultureInfo.InvariantCulture);
        if (mode is not ("auto" or "transform" or "direct") || repeats is < 1 or > 100 || warmups is < 1 or > 20 ||
            spec.Left is < 2 or > 65536 || spec.Right is < 2 or > 65536 || spec.Bits is < 32 or > 1400 ||
            spec.Stride is < 1 or > 1024 || pairLimit is < 1 or > 4294967296)
        {
            throw new ArgumentException("Invalid convolution benchmark mode, dimensions, bits, stride or allowance.");
        }

        var fixture = new Workload(spec);
        fixture.Fill();
        BigInteger[] expected = fixture.Oracle();
        var expectedHash = Hash(expected);
        var leftHash = Hash(fixture.Left);
        var rightHash = Hash(fixture.Right);
        var leftAtoms = fixture.Left.Count(value => !value.IsZero);
        var rightAtoms = fixture.Right.Count(value => !value.IsZero);
        var pairs = (long)leftAtoms * rightAtoms;
        BigInteger leftMass = fixture.Left.Aggregate(BigInteger.Add),
            rightMass = fixture.Right.Aggregate(BigInteger.Add);
        BigInteger bound = BigInteger.Min(leftMass * fixture.Right.Max(), rightMass * fixture.Left.Max());
        BigInteger modulus = BigInteger.One;
        var moduli = 0;
        while (modulus <= bound && moduli < ExactIntegerConvolution.ModulusCount)
        {
            modulus *= ExactIntegerConvolution.Modulus(moduli++);
        }

        if (expected.Any(value => value.Sign < 0 || value > bound) ||
            expected.Aggregate(BigInteger.Add) != leftMass * rightMass)
        {
            throw new ArithmeticException("The analytic oracle violates a coefficient bound or mass identity.");
        }

        // Validate the closed form against literal schoolbook products on a bounded rectangular prefix.
        var control = new Workload(spec with
        {
            Left = 67,
            Right = 94
        });
        control.Fill();
        if (!Direct(control.Left, control.Right, 10000).SequenceEqual(control.Oracle()))
        {
            throw new ArithmeticException("The analytic oracle failed its independent bounded direct control.");
        }

        using var process = Process.GetCurrentProcess();
        var measurements = new List<object>();
        for (var repeat = -warmups; repeat < repeats; repeat++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var bytes = GC.GetTotalAllocatedBytes(true);
            var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            TimeSpan pause = GC.GetTotalPauseDuration(), cpu = process.TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            long work = 0;

            void Spend(long units)
            {
                work = checked(work + units);
            }

            BigInteger[] actual;
            var selected = "direct";
            if (mode != "direct" && ExactIntegerConvolution.TryTransform(fixture.Left, fixture.Right, Spend,
                    out actual, mode == "auto"))
            {
                selected = "transform";
            }
            else
            {
                if (mode == "transform")
                {
                    throw new InvalidOperationException("The forced transform declined this input.");
                }

                actual = Direct(fixture.Left, fixture.Right, pairLimit);
            }

            clock.Stop();
            var allocated = GC.GetTotalAllocatedBytes(true) - bytes;
            var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            var gcPauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds;
            var collections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - gc[g]).ToArray();
            if (!actual.SequenceEqual(expected))
            {
                throw new ArithmeticException("Convolution differs from the independent complete coefficient oracle.");
            }

            var fingerprint = Hash(actual);
            if (fingerprint != expectedHash)
            {
                throw new ArithmeticException("Convolution fingerprint differs from the oracle.");
            }

            if (repeat >= 0)
            {
                measurements.Add(new
                {
                    selected,
                    elapsedMs = clock.Elapsed.TotalMilliseconds,
                    cpuMs,
                    allocatedBytes = allocated,
                    gc = collections,
                    gcPauseMs,
                    transformWork = work,
                    directPairs = selected == "direct" ? pairs : 0,
                    outputSha256 = fingerprint,
                    processLifetimePeakWorkingSetBytes = process.PeakWorkingSet64
                });
            }

            var phase = repeat < 0 ? $"warmup {repeat + warmups + 1}" : $"sample {repeat + 1}";
            Console.WriteLine(
                $"{mode} {phase}: {selected}, {clock.Elapsed.TotalMilliseconds:F3} ms, {allocated} B; all coefficients verified");
        }

        var report = new
        {
            mode,
            specification = spec,
            leftAtoms,
            rightAtoms,
            pairs,
            warmups,
            coefficientBoundBits = bound.GetBitLength(),
            requiredModuli = moduli,
            transformLength = BitOperations.RoundUpToPowerOf2((uint)expected.Length),
            runtime = Environment.Version.ToString(),
            processors = Environment.ProcessorCount,
            serverGc = GCSettings.IsServerGC,
            gcTotalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            gcHeapHardLimit = Environment.GetEnvironmentVariable("DOTNET_GCHeapHardLimit"),
            leftSha256 = leftHash,
            rightSha256 = rightHash,
            expectedOutputSha256 = expectedHash,
            coreBinarySha256 =
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Rational).Assembly.Location))),
            benchmarkBinarySha256 =
                Convert.ToHexStringLower(
                    SHA256.HashData(File.ReadAllBytes(typeof(ConvolutionBenchmark).Assembly.Location))),
            note =
                "Integer-kernel comparison, not whole-graph throughput. Timings include admission/inspection, dense output, sparse-index preparation or modular conversion and CRT. Input/oracle construction and full coefficient verification are outside timing. Process lifetime peak includes setup and validation, not an isolated kernel peak. Forced transform is only a reference control; no game/graph pattern is recognized.",
            measurements
        };
        var path = Option("--report", "");
        if (path.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
    }

    private sealed record Specification(int Left, int Right, int Bits, int Stride);

    private sealed class Workload(Specification spec)
    {
        private readonly BigInteger _a = (BigInteger.One << (spec.Bits - 1)) + 11;
        private readonly BigInteger _c = (BigInteger.One << (spec.Bits - 1)) + 23;
        public BigInteger[] Left { get; } = new BigInteger[spec.Left];
        public BigInteger[] Right { get; } = new BigInteger[spec.Right];

        public void Fill()
        {
            for (var i = 0; i < Left.Length; i += spec.Stride)
            {
                Left[i] = _a + 3 * (i / spec.Stride);
            }

            for (var i = 0; i < Right.Length; i += spec.Stride)
            {
                Right[i] = _c + 5 * (i / spec.Stride);
            }

            // An extra atom prevents a regularly spaced sparse input from being only a rescaled dense lattice.
            if (spec.Stride > 1)
            {
                Left[1] = _a + 101;
                Right[1] = _c + 103;
            }
        }

        private BigInteger Base(int position, int length, BigInteger origin, int slope) =>
            position >= 0 && position < length && position % spec.Stride == 0
                ? origin + slope * (position / spec.Stride)
                : BigInteger.Zero;

        public BigInteger[] Oracle()
        {
            var result = new BigInteger[Left.Length + Right.Length - 1];
            var n = (Left.Length - 1) / spec.Stride + 1;
            var m = (Right.Length - 1) / spec.Stride + 1;
            for (var position = 0; position < result.Length; position++)
            {
                if (position % spec.Stride == 0)
                {
                    var k = position / spec.Stride;
                    var lo = Math.Max(0, k - m + 1);
                    var hi = Math.Min(n - 1, k);
                    if (lo <= hi)
                    {
                        BigInteger count = hi - lo + 1;
                        BigInteger sum = (BigInteger)hi * (hi + 1) / 2 - (BigInteger)(lo - 1) * lo / 2;
                        BigInteger squares = (BigInteger)hi * (hi + 1) * (2 * hi + 1) / 6 -
                                             (BigInteger)(lo - 1) * lo * (2 * lo - 1) / 6;
                        // Sum (a + 3*i)*(c + 5*(k-i)) over the exact overlap, using Faulhaber sums.
                        result[position] = _a * (_c + 5 * k) * count +
                            (3 * (_c + 5 * k) - 5 * _a) * sum - 15 * squares;
                    }
                }

                if (spec.Stride > 1)
                {
                    result[position] += (_a + 101) * Base(position - 1, Right.Length, _c, 5) +
                                        (_c + 103) * Base(position - 1, Left.Length, _a, 3);
                    if (position == 2)
                    {
                        result[position] += (_a + 101) * (_c + 103);
                    }
                }
            }

            return result;
        }
    }
}
