using SlotMath.Api.Infrastructure;
using SlotMath.Core.Measurements;
namespace SlotMath.Api.Features.Runs;

public sealed record RetainedReference<T>(string RunId, T Report, DiagnosticRetention Retention);
public static class DiagnosticReferences
{
    public static void Map(RouteGroupBuilder group, InMemoryRunStore runs)
    {
        group.MapPost("/{id}/measurements/reference/comparison", (string id, ExactLawComparisonRequest request, CancellationToken token) =>
            Calculate(id, "independent-law-comparison", request, () => FiniteModelAnalysis.Compare(request, token) with
            {
                AuthoredInputSha256 = RuntimeProvenance.AuthoredInputHash(request),
                AlgorithmVersion = "finite-law-comparison-rational-v1",
                CoreBinarySha256 = RuntimeProvenance.Current.CoreBinarySha256
            }, runs))
            .Produces<RetainedReference<ExactLawComparisonReport>>().RequireRateLimiting("compute");
        group.MapPost("/{id}/measurements/reference/distribution", (string id, FiniteModelRequest request, CancellationToken token) =>
            Calculate(id, "independent-finite-law", request, () => FiniteModelAnalysis.Distribution(request, token) with
            {
                AuthoredInputSha256 = RuntimeProvenance.AuthoredInputHash(request),
                AlgorithmVersion = "finite-law-rational-v1",
                CoreBinarySha256 = RuntimeProvenance.Current.CoreBinarySha256
            }, runs))
            .Produces<RetainedReference<FiniteModelReport>>().RequireRateLimiting("compute");
        group.MapPost("/{id}/measurements/reference/markov", (string id, MarkovModelRequest request, CancellationToken token) =>
            Calculate(id, "independent-finite-state", request, () => FiniteModelAnalysis.Markov(request, token) with
            {
                AuthoredInputSha256 = RuntimeProvenance.AuthoredInputHash(request),
                AlgorithmVersion = "absorbing-rational-gauss-jordan-v1",
                CoreBinarySha256 = RuntimeProvenance.Current.CoreBinarySha256
            }, runs))
            .Produces<RetainedReference<MarkovModelReport>>().RequireRateLimiting("compute");
    }
    private static IResult Calculate<T>(string id, string kind, object input, Func<T> calculate, InMemoryRunStore runs)
    {
        if (runs.Get(id) is null) return Results.NotFound(new { error = "Run not found." });
        try
        {
            var result = calculate();
            return Results.Ok(new RetainedReference<T>(id, result, runs.RetainDiagnostic(id, kind, input, result!)));
        }
        catch (Exception ex) when (ex is ArgumentException or ArithmeticException or FormatException)
        { return Results.BadRequest(new { error = ex.Message }); }
    }
}
