using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Measurements;

/// <summary>A run's immutable, opt-in measurement plan. Null NodeId observes the settled paid round;
/// otherwise observe state immediately before each visit to that flattened graph node.
/// Null Value means the settled, capped payout and is only valid for round observations.</summary>
public sealed record MeasurementDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? NodeId { get; init; }
    public Expression? Value { get; init; }
    public Expression? Filter { get; init; }
    public string Unit { get; init; } = "";
    public MeasurementOptions? Options { get; init; }
}
public sealed record MeasurementPoint(string NodeId, string Label);
public sealed record MeasurementField(string Name, string Type);
public sealed record MeasurementSchema(IReadOnlyList<MeasurementPoint> Points, IReadOnlyList<MeasurementField> Fields);
public sealed record MeasurementSnapshot(string Id, long Observations, long Count, long Excluded, long Errors,
    double? Min, double? Max, double? Mean, double? Sum, double? StdDev, string? FirstError)
{
    public MeasurementAnalysis? Analysis { get; init; }
    public MeasurementWitness[] Witnesses { get; init; } = [];
}

internal struct MeasurementAccumulator
{
    public long Observations, Count, Excluded, Errors;
    public double Min, Max, Mean, Sum, M2;
    public string? FirstError;
    public MeasurementAnalysisAccumulator? Analysis;
    public WitnessAccumulator? Witnesses;
    public void Add(double value)
    {
        var count = Count + 1;
        var delta = value - Mean;
        var mean = Count == 0 ? value : Mean + delta / count;
        var m2 = Count == 0 ? 0 : M2 + delta * (value - mean);
        var sum = Sum + value;
        if (!double.IsFinite(value) || !double.IsFinite(mean) || !double.IsFinite(m2) || !double.IsFinite(sum))
            throw new InvalidOperationException("Measurement exceeds finite numeric range.");
        Min = Count == 0 ? value : System.Math.Min(Min, value); Max = Count == 0 ? value : System.Math.Max(Max, value);
        Count = count; Mean = mean; M2 = m2; Sum = sum;
    }
    public void Merge(MeasurementAccumulator other)
    {
        if (other.Witnesses is not null) (Witnesses ??= new()).Merge(other.Witnesses);
        if (other.Analysis is not null)
        {
            Analysis ??= other.Analysis.Empty();
            Analysis.Merge(other.Analysis);
        }
        Observations += other.Observations; Excluded += other.Excluded; Errors += other.Errors; FirstError ??= other.FirstError;
        if (other.Count == 0) return;
        if (Count == 0) { Count = other.Count; Min = other.Min; Max = other.Max; Mean = other.Mean; Sum = other.Sum; M2 = other.M2; return; }
        var count = Count + other.Count; var delta = other.Mean - Mean;
        M2 += other.M2 + delta * delta * ((double)Count * other.Count / count);
        Mean += delta * other.Count / count; Sum += other.Sum;
        Min = System.Math.Min(Min, other.Min); Max = System.Math.Max(Max, other.Max); Count = count;
        // Never serialize an infinity as a plausible statistic.
        if (!double.IsFinite(Mean) || !double.IsFinite(M2) || !double.IsFinite(Sum))
            throw new InvalidOperationException("Measurement aggregate exceeds finite numeric range.");
    }
    public readonly MeasurementSnapshot Snapshot(string id, bool ordered = true) => new(id, Observations, Count, Excluded, Errors,
        Count == 0 ? null : Min, Count == 0 ? null : Max, Count == 0 ? null : Mean, Count == 0 ? null : Sum,
        Count < 2 ? null : System.Math.Sqrt(System.Math.Max(0, M2 / (Count - 1))), FirstError) { Analysis = Analysis?.Snapshot(Errors, ordered), Witnesses = Witnesses?.Snapshot() ?? [] };
}
