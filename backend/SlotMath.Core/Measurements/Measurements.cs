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
}
public sealed record MeasurementPoint(string NodeId, string Label);
public sealed record MeasurementField(string Name, string Type);
public sealed record MeasurementSchema(IReadOnlyList<MeasurementPoint> Points, IReadOnlyList<MeasurementField> Fields);
public sealed record MeasurementSnapshot(string Id, long Observations, long Count, long Excluded, long Errors,
    double? Min, double? Max, double? Mean, double? Sum, double? StdDev, string? FirstError);

/// <summary>Worker-private bounded storage. A round is committed atomically: observations from an
/// interrupted round are discarded. No histories, state copies or RNG calls are retained.</summary>
internal sealed class MeasurementCollector(IReadOnlyList<MeasurementDefinition> definitions)
{
    private readonly MeasurementAccumulator[] _round = new MeasurementAccumulator[definitions.Count];
    public MeasurementAccumulator[] Total { get; } = new MeasurementAccumulator[definitions.Count];
    public MeasurementAccumulator[] Delta { get; } = new MeasurementAccumulator[definitions.Count];
    public IReadOnlyList<MeasurementDefinition> Definitions => definitions;
    public void Begin() => Array.Clear(_round);
    public void Observe<T>(int index, T state, Func<T, ExprValue>? value, Func<T, ExprValue>? filter, double payout = 0)
    {
        ref var accumulator = ref _round[index];
        accumulator.Observations++;
        try
        {
            if (filter is not null)
            {
                var predicate = filter(state);
                if (predicate.Kind != ExprType.Boolean) throw new InvalidOperationException("Measurement filter must return Boolean.");
                if (!predicate.BoolValue) { accumulator.Excluded++; return; }
            }
            if (value is null) { accumulator.Add(payout); return; }
            var number = value(state);
            if (number.Kind != ExprType.Number) throw new InvalidOperationException("Measurement value must return Number.");
            accumulator.Add(number.AsDouble());
        }
        catch (Exception ex) when (ex is ExpressionEvaluationException or InvalidOperationException or FormatException or ArithmeticException or ArgumentException)
        { accumulator.Errors++; accumulator.FirstError ??= ex.Message.Length > 240 ? ex.Message[..240] : ex.Message; }
    }
    public void ObservePayout(int index, double payout)
    {
        _round[index].Observations++;
        _round[index].Add(payout);
    }
    public void Commit()
    {
        Merge(Total, _round); Merge(Delta, _round);
    }
    public static void Merge(MeasurementAccumulator[] target, MeasurementAccumulator[] source)
    { for (var i = 0; i < target.Length; i++) target[i].Merge(source[i]); }
    public static MeasurementSnapshot[] Snapshot(IReadOnlyList<MeasurementDefinition> definitions, MeasurementAccumulator[] values)
        => values.Select((value, i) => value.Snapshot(definitions[i].Id)).ToArray();
}
internal struct MeasurementAccumulator
{
    public long Observations, Count, Excluded, Errors;
    public double Min, Max, Mean, Sum, M2;
    public string? FirstError;
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
    public readonly MeasurementSnapshot Snapshot(string id) => new(id, Observations, Count, Excluded, Errors,
        Count == 0 ? null : Min, Count == 0 ? null : Max, Count == 0 ? null : Mean, Count == 0 ? null : Sum,
        Count < 2 ? null : System.Math.Sqrt(System.Math.Max(0, M2 / (Count - 1))), FirstError);
}
