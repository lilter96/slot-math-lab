using SlotMath.Api.Infrastructure;
using SlotMath.Core.Measurements;

namespace SlotMath.Api.Features.Runs;

public sealed record AccountingEvidenceSource(string? ConfigHash, string? MeasurementHash, long Sequence, long PaidRounds, RuntimeProvenance? Producer);
public sealed record MeasurementAccountingReport(string RunId, AccountingEvidenceSource Source, ComponentAccountingReport Report);

public static class MeasurementAccounting
{
    public static void Map(RouteGroupBuilder group, InMemoryRunStore runs)
    {
        group.MapPost("/{id}/measurements/accounting", (string id, ComponentAccountingRequest request) =>
        {
            var run = runs.Get(id);
            if (run is null) return Results.NotFound(new { error = "Run not found." });
            if (run.Status != "completed" || run.Progress is null)
                return Results.BadRequest(new { error = "Finish the run before retaining a component reconciliation. Running or interrupted prefixes are not a completed population." });
            try
            {
                var report = ComponentAccounting.Calculate(request, run.Measurements, run.Progress.Measurements);
                var source = new AccountingEvidenceSource(run.ConfigHash, run.MeasurementHash, run.Sequence, run.Progress.SampleCount, run.RuntimeProvenance);
                var result = new MeasurementAccountingReport(id, source, report);
                return Results.Ok(new RetainedReference<MeasurementAccountingReport>(id, result, runs.RetainDiagnostic(id, "component-accounting", request, result)));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).Produces<RetainedReference<MeasurementAccountingReport>>().RequireRateLimiting("compute");
    }
}
