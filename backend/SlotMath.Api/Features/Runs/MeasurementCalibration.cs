using SlotMath.Api.Infrastructure;
using SlotMath.Core.Measurements;
namespace SlotMath.Api.Features.Runs;

public sealed record MeasurementCalibrationRequest(string MeasurementId, string Statistic = "cdf", int Replicates = 2000, long Seed = 42);
public sealed record MeasurementCalibrationReport(string RunId, string? MeasurementHash, string MeasurementId, NullCalibrationReport Report)
{ public DiagnosticRetention? Retention { get; init; } }
public static class MeasurementCalibration
{
    public static void Map(RouteGroupBuilder group, InMemoryRunStore runs)
    {
        group.MapPost("/{id}/measurements/calibration", async (string id, MeasurementCalibrationRequest request, CancellationToken token) =>
        {
            var run = runs.Get(id); if (run is null) return Results.NotFound(new { error = "Run not found." });
            var definition = run.Measurements.FirstOrDefault(d => d.Id == request.MeasurementId);
            var observation = run.Progress?.Measurements.FirstOrDefault(d => d.Id == request.MeasurementId);
            if (run.Status != "completed" || run.Execution is { PersistentKeys.Length: > 0 } || definition?.Options is not { IndependentSubjects: true, Weight: null, ReferenceDistribution.Length: > 0 }
                || observation is not { Errors: 0, Analysis: { SupportComplete: true, UnclosedEpisodes: 0, DuplicateAwards: 0 } })
                return Results.BadRequest(new { error = "Calibration requires a completed independent unweighted subject population, a pinned prespecified PMF and complete error-free support." });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var input = new NullCalibrationRequest(observation.Analysis.Support, definition.Options.ReferenceDistribution, request.Statistic, request.Replicates, request.Seed);
                var report = await Task.Run(() => DiscreteNullCalibration.Calculate(input, deadline.Token), deadline.Token);
                var result = new MeasurementCalibrationReport(id, run.MeasurementHash, request.MeasurementId, report with { AuthoredInputSha256 = RuntimeProvenance.AuthoredInputHash(input), CoreBinarySha256 = RuntimeProvenance.Current.CoreBinarySha256 });
                return Results.Ok(result with { Retention = runs.RetainDiagnostic(id, "discrete-calibration", input, result) });
            }
            catch (OperationCanceledException) { return Results.Json(new { error = "Null calibration was cancelled or exceeded its 20-second budget." }, statusCode: 408); }
            catch (Exception ex) when (ex is ArgumentException or ArithmeticException) { return Results.BadRequest(new { error = ex.Message }); }
        }).Produces<MeasurementCalibrationReport>().RequireRateLimiting("compute");
    }
}
