using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SlotMath.Core.Math;

namespace SlotMath.Benchmarks;

/// <summary>Sequential paired process comparisons with frozen binaries, complete-law checks and retained raw evidence.</summary>
internal static class ConvolutionMatrixBenchmark
{
    private static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static double Quantile(IEnumerable<double> source, double probability)
    {
        var values = source.Order().ToArray();
        var at = (values.Length - 1) * probability;
        var lo = (int)Math.Floor(at);
        return values[lo] + (values[Math.Min(lo + 1, values.Length - 1)] - values[lo]) * (at - lo);
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

        int[] Numbers(string name, string fallback)
        {
            return Option(name, fallback).Split(',')
                .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
        }

        var lengths = Numbers("--lengths", "64,512,4096,65536");
        var bits = Numbers("--coefficient-bits", "32,256,1024");
        var strides = Numbers("--strides", "1,4,16");
        var repeats = Number("--repeats", "3");
        var warmups = Number("--warmups", "1");
        var blocks = Number("--blocks", "2");
        var rightLength = Number("--right-length", "0");
        var seconds = Number("--process-seconds", "180");
        var pairLimit = long.Parse(Option("--max-direct-pairs", "67108864"), CultureInfo.InvariantCulture);
        var heapLimit = long.Parse(Option("--heap-limit-bytes", "1073741824"), CultureInfo.InvariantCulture);
        if (lengths.Length == 0 || bits.Length == 0 || strides.Length == 0 ||
            (long)lengths.Length * bits.Length * strides.Length > 256 ||
            lengths.Any(value => value is < 2 or > 65536) || bits.Any(value => value is < 32 or > 1400) ||
            strides.Any(value => value is < 1 or > 1024) || repeats is < 1 or > 100 || warmups is < 1 or > 20 ||
            blocks is < 1 or > 8 || seconds is < 1 or > 3600 || pairLimit is < 1 or > 4294967296 ||
            heapLimit is < 134217728 or > 4294967296 || rightLength is < 0 or 1 or > 65536)
        {
            throw new ArgumentException("Invalid convolution matrix dimensions or process limits.");
        }

        var requestedDirectory = Option("--report-directory", "");
        if (string.IsNullOrWhiteSpace(requestedDirectory))
        {
            throw new ArgumentException("Choose a new or empty --report-directory.");
        }

        var directory = Path.GetFullPath(requestedDirectory);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new ArgumentException(
                "Choose a new or empty --report-directory; existing evidence is never overwritten.");
        }

        Directory.CreateDirectory(directory);
        var benchmark = typeof(ConvolutionBenchmark).Assembly.Location;
        var core = typeof(Rational).Assembly.Location;
        var benchmarkHash = HashFile(benchmark);
        var coreHash = HashFile(core);
        var unit = args.Contains("--unit-coefficients");
        var baseline = Option("--baseline-benchmark", "");
        var baselineCore = baseline.Length == 0
            ? null
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(baseline))!, "SlotMath.Core.dll");
        var baselineCoreHash = baselineCore is null ? null : HashFile(baselineCore);
        if (baselineCore is not null && HashFile(baseline) != benchmarkHash)
        {
            throw new ArgumentException(
                "Use the same benchmark DLL with the frozen baseline Core DLL for matched comparisons.");
        }

        var repository = Directory.GetCurrentDirectory();
        var backend = Path.Combine(repository, "backend");
        if (!File.Exists(Path.Combine(backend, "SlotMathLab.slnx")))
        {
            throw new ArgumentException("Run the matrix from the repository root.");
        }

        Dictionary<string, string> sources = Directory.EnumerateFiles(backend, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) ||
                           path.EndsWith(".csproj", StringComparison.Ordinal) ||
                           Path.GetFileName(path) == ".editorconfig")
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order().ToDictionary(path => Path.GetRelativePath(repository, path), HashFile);
        var observations = new List<Observation>();
        var skipped = new List<object>();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var loadAtStart = File.Exists("/proc/loadavg") ? File.ReadAllText("/proc/loadavg").Trim() : null;
        var observationsPath = Path.Combine(directory, "observations.json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        void CheckFrozen()
        {
            if (HashFile(benchmark) != benchmarkHash || HashFile(core) != coreHash ||
                sources.Any(pair => HashFile(Path.Combine(repository, pair.Key)) != pair.Value) ||
                (baselineCore is not null &&
                 (HashFile(baselineCore) != baselineCoreHash || HashFile(baseline) != benchmarkHash)))
            {
                throw new InvalidOperationException(
                    "Source or binaries changed during the benchmark; measurements are not comparable.");
            }
        }

        foreach (var length in lengths)
        {
            foreach (var coefficientBits in bits)
            {
                foreach (var stride in strides)
                {
                    var right = rightLength == 0 ? length : rightLength;
                    var key = $"n{length}-m{right}-b{coefficientBits}-s{stride}" + (unit ? "-unit" : "");
                    var leftAtoms = (length - 1) / stride + 1 + (stride > 1 ? 1 : 0);
                    var rightAtoms = (right - 1) / stride + 1 + (stride > 1 ? 1 : 0);
                    var pairs = (long)leftAtoms * rightAtoms;
                    string? expected = null;
                    string? expectedLeft = null, expectedRight = null;
                    foreach (var control in baselineCore is null
                                 ? new[] { "direct", "transform" }
                                 : new[] { "baseline" })
                    {
                        if (control == "direct" && pairs > pairLimit)
                        {
                            skipped.Add(new
                            {
                                @case = key,
                                control,
                                pairs,
                                reason = "Direct product count exceeds the explicit control allowance."
                            });
                            continue;
                        }

                        for (var block = 0; block < blocks; block++)
                        {
                            string[] order = (block + (control == "transform" ? 1 : 0)) % 2 == 0
                                ? ["auto", control, control, "auto"]
                                : [control, "auto", "auto", control];
                            for (var position = 0; position < order.Length; position++)
                            {
                                CheckFrozen();
                                var mode = order[position];
                                var executable = mode == "baseline" ? baseline : benchmark;
                                var childMode = mode == "baseline" ? "auto" : mode;
                                var expectedCoreHash = mode == "baseline" ? baselineCoreHash : coreHash;
                                var name = $"{key}-{control}-{block}-{position}-{mode}";
                                var reportPath = Path.Combine(directory, name + ".json");
                                var start = new ProcessStartInfo("dotnet")
                                {
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true
                                };
                                foreach (var argument in new[]
                                         {
                                             executable, "--convolution-mode", childMode, "--length",
                                             length.ToString(CultureInfo.InvariantCulture), "--right-length",
                                             right.ToString(CultureInfo.InvariantCulture), "--bits",
                                             coefficientBits.ToString(CultureInfo.InvariantCulture), "--stride",
                                             stride.ToString(CultureInfo.InvariantCulture), "--warmups",
                                             warmups.ToString(CultureInfo.InvariantCulture), "--repeats",
                                             repeats.ToString(CultureInfo.InvariantCulture), "--max-direct-pairs",
                                             pairLimit.ToString(CultureInfo.InvariantCulture), "--report",
                                             reportPath
                                         })
                                {
                                    start.ArgumentList.Add(argument);
                                }

                                if (unit)
                                {
                                    start.ArgumentList.Add("--unit-coefficients");
                                }

                                start.Environment["DOTNET_TieredCompilation"] = "0";
                                start.Environment["COMPlus_TieredCompilation"] = "0";
                                start.Environment["DOTNET_GCHeapHardLimit"] =
                                    heapLimit.ToString("X", CultureInfo.InvariantCulture);
                                using Process process = Process.Start(start)!;
                                Console.WriteLine($"{name}: running process {process.Id}");
                                File.WriteAllText(Path.Combine(directory, "active.json"), JsonSerializer.Serialize(new
                                {
                                    name,
                                    processId = process.Id,
                                    startedAt = DateTimeOffset.UtcNow,
                                    command = start.ArgumentList.ToArray()
                                }, options));
                                Task<string> output = process.StandardOutput.ReadToEndAsync(),
                                    error = process.StandardError.ReadToEndAsync();
                                if (!process.WaitForExit(seconds * 1000))
                                {
                                    process.Kill(true);
                                    process.WaitForExit();
                                    File.WriteAllText(Path.Combine(directory, name + ".log"),
                                        output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
                                    throw new TimeoutException(
                                        $"{name} exceeded its {seconds}s process allowance; retained evidence is incomplete.");
                                }

                                File.WriteAllText(Path.Combine(directory, name + ".log"),
                                    output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
                                if (process.ExitCode != 0)
                                {
                                    throw new InvalidOperationException(
                                        $"{name} failed with exit {process.ExitCode}; inspect its retained log.");
                                }

                                CheckFrozen();
                                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(reportPath));
                                JsonElement root = document.RootElement;
                                var fingerprint = root.GetProperty("expectedOutputSha256").GetString()!;
                                expected ??= fingerprint;
                                expectedLeft ??= root.GetProperty("leftSha256").GetString();
                                expectedRight ??= root.GetProperty("rightSha256").GetString();
                                if (root.GetProperty("coreBinarySha256").GetString() != expectedCoreHash ||
                                    root.GetProperty("benchmarkBinarySha256").GetString() != benchmarkHash ||
                                    fingerprint != expected ||
                                    root.GetProperty("leftSha256").GetString() != expectedLeft ||
                                    root.GetProperty("rightSha256").GetString() != expectedRight ||
                                    root.GetProperty("leftAtoms").GetInt32() != leftAtoms ||
                                    root.GetProperty("rightAtoms").GetInt32() != rightAtoms ||
                                    root.GetProperty("tieredCompilation").GetString() != "0")
                                {
                                    throw new ArithmeticException(
                                        "Benchmark identities or independent results disagree.");
                                }

                                Sample[] samples = root.GetProperty("measurements").EnumerateArray().Select(value =>
                                {
                                    if (value.GetProperty("outputSha256").GetString() != expected)
                                    {
                                        throw new ArithmeticException(
                                            "A timed result differs from the complete oracle.");
                                    }

                                    return new Sample(value.GetProperty("elapsedMs").GetDouble(),
                                        value.GetProperty("allocatedBytes").GetDouble(),
                                        value.GetProperty("cpuMs").GetDouble(),
                                        value.GetProperty("processLifetimePeakWorkingSetBytes").GetDouble(),
                                        value.GetProperty("selected").GetString()!);
                                }).ToArray();
                                if (samples.Length != repeats)
                                {
                                    throw new ArithmeticException(
                                        "A process did not retain every requested measurement.");
                                }

                                observations.Add(new Observation(key, control, block, position, mode,
                                    Path.GetFileName(reportPath), fingerprint, samples));
                                File.WriteAllText(observationsPath, JsonSerializer.Serialize(observations, options));
                                Console.WriteLine(
                                    $"{name}: verified {samples.Length} complete outputs; median {Quantile(samples.Select(sample => sample.Ms), 0.5):F3} ms");
                            }
                        }
                    }
                }
            }
        }

        var summary = observations.GroupBy(value => (value.Case, value.Control)).Select(group =>
        {
            Sample[] auto = group.Where(value => value.Mode == "auto").SelectMany(value => value.Samples).ToArray();
            Sample[] control = group.Where(value => value.Mode == group.Key.Control).SelectMany(value => value.Samples)
                .ToArray();
            var autoMedian = Quantile(auto.Select(sample => sample.Ms), 0.5);
            var controlMedian = Quantile(control.Select(sample => sample.Ms), 0.5);
            return new
            {
                @case = group.Key.Case,
                control = group.Key.Control,
                samplesPerVariant = auto.Length,
                autoMedianMs = autoMedian,
                controlMedianMs = controlMedian,
                controlOverAuto = controlMedian / autoMedian,
                autoP25Ms = Quantile(auto.Select(sample => sample.Ms), 0.25),
                autoP75Ms = Quantile(auto.Select(sample => sample.Ms), 0.75),
                controlP25Ms = Quantile(control.Select(sample => sample.Ms), 0.25),
                controlP75Ms = Quantile(control.Select(sample => sample.Ms), 0.75),
                autoAllocatedMedian = Quantile(auto.Select(sample => sample.Bytes), 0.5),
                controlAllocatedMedian = Quantile(control.Select(sample => sample.Bytes), 0.5),
                autoSelected = auto.Select(sample => sample.Selected).Distinct().ToArray()
            };
        }).ToArray();
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new
        {
            completed = true,
            startedAt,
            completedAt = DateTimeOffset.UtcNow,
            lengths,
            rightLength,
            unit,
            baselineCoreHash,
            bits,
            strides,
            blocks,
            warmups,
            repeats,
            pairLimit,
            heapLimit,
            coreBinarySha256 = coreHash,
            benchmarkBinarySha256 = benchmarkHash,
            sources,
            processors = Environment.ProcessorCount,
            runtime = Environment.Version.ToString(),
            cpuModel = File.Exists("/proc/cpuinfo")
                ? File.ReadLines("/proc/cpuinfo")
                    .FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1]
                    .Trim()
                : null,
            hostLoadAtStart = loadAtStart,
            hostLoadAtEnd = File.Exists("/proc/loadavg") ? File.ReadAllText("/proc/loadavg").Trim() : null,
            summary,
            skipped,
            note =
                "Each comparison uses frozen processes in alternating ABBA/BAAB order, with separate warmup and complete analytic coefficient checks. Direct controls exceeding the declared pair allowance are explicitly omitted; those rows establish capacity/equality with the forced transform, not measured speedup over direct multiplication. Raw per-process reports retain CPU, allocation, GC and process-lifetime peaks."
        }, options));
        File.Delete(Path.Combine(directory, "active.json"));
        Console.WriteLine($"Completed {observations.Count} processes; validated evidence in {directory}");
    }

    private sealed record Sample(double Ms, double Bytes, double CpuMs, double PeakBytes, string Selected);

    private sealed record Observation(
        string Case,
        string Control,
        int Block,
        int Position,
        string Mode,
        string File,
        string OutputSha256,
        Sample[] Samples);
}
