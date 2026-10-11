namespace SlotMath.Api.Infrastructure;

/// <summary>Cached math belongs to a particular loaded engine and execution policy.</summary>
public static class LightEvaluationCacheKey
{
    public static string Compute(EvaluateLightRequest request, bool allowPlugins, RuntimeProvenance? runtime = null) =>
        "light:v2:" + CanonicalHash.Compute(new
        {
            config = request.Config,
            seed = request.Seed,
            maxBranches = request.MaxBranches,
            sampleSize = request.SampleSize,
            runtime = runtime ?? RuntimeProvenance.Current,
            allowPlugins,
            // MVID remains available if a replaced DLL or bundled deployment
            // prevents an honest file fingerprint of the loaded assembly.
            coreModule = typeof(SlotMath.Core.Math.SampledInterpreter).Module.ModuleVersionId.ToString("D"),
            apiModule = typeof(LightEvaluationCacheKey).Module.ModuleVersionId.ToString("D"),
        });
}
