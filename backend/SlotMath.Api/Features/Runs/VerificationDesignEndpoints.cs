using SlotMath.Api.Infrastructure;
using SlotMath.Core.Measurements;
namespace SlotMath.Api.Features.Runs;
public sealed record DesignEvidence<T>(string AuthoredInputSha256, RuntimeProvenance Runtime, T Report, DiagnosticRetention? Retention);
public static class VerificationDesignEndpoints
{
    public static void Map(RouteGroupBuilder group, InMemoryRunStore runs)
    {
        group.MapPost("/measurements/resource-impact", (ResourceImpactRequest request, string? runId, CancellationToken token) => Calculate(request, runId, "resource-impact", runs, FiniteModelAnalysis.ResourceImpact, token)).Produces<DesignEvidence<ResourceImpactReport>>().RequireRateLimiting("compute");
        group.MapPost("/measurements/sampling-design", (SamplingDesignRequest request, string? runId, CancellationToken token) => Calculate(request, runId, "sampling-design", runs, FiniteModelAnalysis.SamplingDesign, token)).Produces<DesignEvidence<SamplingDesignReport>>().RequireRateLimiting("compute");
    }
    private static IResult Calculate<TRequest, TReport>(TRequest request, string? runId, string kind, InMemoryRunStore runs,
        Func<TRequest, CancellationToken, TReport> calculate, CancellationToken token) where TRequest : notnull
    {
        if (runId is not null && runs.Get(runId) is null) return Results.NotFound(new { error = "Run not found." });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var report = calculate(request, deadline.Token);
            var result = new DesignEvidence<TReport>(RuntimeProvenance.AuthoredInputHash(request), RuntimeProvenance.Current, report, null);
            return Results.Ok(runId is null ? result : result with { Retention = runs.RetainDiagnostic(runId, kind, request, result) });
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Results.Json(new { error = "Design exceeded its 20-second calculation budget." }, statusCode: 408); }
        catch (Exception ex) when (ex is ArgumentException or ArithmeticException or FormatException) { return Results.BadRequest(new { error = ex.Message }); }
    }
}
