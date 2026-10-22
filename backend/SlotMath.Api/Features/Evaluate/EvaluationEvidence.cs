using SlotMath.Core.Math.Structured;

namespace SlotMath.Api.Features.Evaluate;

/// <summary>Requested population evidence, independent of the execution strategy.</summary>
public static class EvaluationEvidence
{
    public static bool TryMode(string? text, out EvaluationMode mode) =>
        Enum.TryParse(text, true, out mode) &&
        Enum.GetNames<EvaluationMode>().Contains(text?.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(GraphEvaluationRequest request) =>
        request.Evidence is "Report" or "Moments" or "Mean" &&
        (!string.Equals(request.Mode?.Trim(), "Sampled", StringComparison.OrdinalIgnoreCase) ||
         request.Evidence == "Report") &&
        (request.Evidence != "Mean" || request.Analysis is null) &&
        (request.Evidence == "Report" || request.Analysis is null ||
         (request.Analysis.TailProbabilities is { Length: 0 } &&
          request.Analysis.PayoutThresholds is null or { Length: 0 } &&
          request.Analysis.IndependentRounds is null));

    public static StructuredEvidence Parse(string evidence) => evidence switch
    {
        "Report" => StructuredEvidence.Report,
        "Moments" => StructuredEvidence.Moments | StructuredEvidence.HitFrequency,
        "Mean" => StructuredEvidence.Mean,
        _ => throw new ArgumentException("Evidence must be Report, Moments or Mean.", nameof(evidence))
    };
}
