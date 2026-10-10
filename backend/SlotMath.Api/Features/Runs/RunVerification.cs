using SlotMath.Api.Infrastructure;
using SlotMath.Core.Measurements;

namespace SlotMath.Api.Features.Runs;

public sealed record RunVerificationEvidence(string RunId, AccountingEvidenceSource Source, VerificationProfileReport Report);

public static class RunVerification
{
    public static void Map(RouteGroupBuilder group, InMemoryRunStore runs)
    {
        // No client-authored criterion input: a completed run can only evaluate
        // the declaration pinned before it was queued.
        group.MapPost("/{id}/verification", (string id) =>
        {
            var run = runs.Get(id); if (run is null) return Results.NotFound(new { error = "Run not found." });
            if (run.VerificationProfile is not { } profile) return Results.BadRequest(new { error = "This run has no predeclared verification profile. Configure it before the next launch." });
            if (run.Status != "completed" || run.Progress is null) return Results.BadRequest(new { error = "Only a completed run can evaluate its final predeclared profile. Interrupted prefixes remain available as measurements." });
            if (run.VerificationProfileHash != ProfileVerification.Hash(profile)) return Results.BadRequest(new { error = "Pinned verification profile fingerprint mismatch." });
            try
            {
                var report = ProfileVerification.Calculate(profile, run.Measurements, run.Progress.Measurements, completed: true);
                var result = new RunVerificationEvidence(id, new(run.ConfigHash, run.MeasurementHash, run.Sequence, run.Progress.SampleCount, run.RuntimeProvenance), report);
                return Results.Ok(new RetainedReference<RunVerificationEvidence>(id, result, runs.RetainDiagnostic(id, "verification-profile", profile, result)));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).Produces<RetainedReference<RunVerificationEvidence>>().RequireRateLimiting("compute");
    }
}
