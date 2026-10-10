using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;

namespace SlotMath.Api.Infrastructure;
/// <summary>Fingerprint the assemblies actually executing the calculation. A source commit
/// alone cannot identify a working-tree build or establish independent verification.</summary>
public sealed record RuntimeProvenance(string MeasurementContract, string NumericalMethods, string Framework,
    string? CoreBinarySha256, string? ApiBinarySha256)
{
    public static RuntimeProvenance Current { get; } = new("measurements-v1", "central-moments-pebay-v1;bins-v1;clopper-pearson-v1;hoeffding-spending-v1;pearson-gamma-v1;session-accounting-v2-decimal-roundtrip;categorical-joint-v1;discrete-null-v1;finite-stationary-v1;witness-prefix-v1;loop-completion-v1;paid-parent-cohorts-v1;paid-turnover-v1;exact-assertion-v1;enumerated-joint-v2;finite-law-comparison-rational-v1;typed-operators-v1;rational-functions-v1;strict-collections-v1;typed-array-index-v1",
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Fingerprint(typeof(SlotMath.Core.Math.SampledInterpreter).Assembly), Fingerprint(typeof(RuntimeProvenance).Assembly));
    private static string? Fingerprint(Assembly assembly)
    {
        if (string.IsNullOrEmpty(assembly.Location)) return null;
        var bytes = File.ReadAllBytes(assembly.Location);
        using var pe = new PEReader(new MemoryStream(bytes)); var metadata = pe.GetMetadataReader();
        // A local rebuild may replace the on-disk DLL while the process keeps the
        // previous assembly loaded. Withhold its hash instead of misidentifying it.
        if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != assembly.ManifestModule.ModuleVersionId) return null;
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
    public static string AuthoredInputHash<T>(T request) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
}
