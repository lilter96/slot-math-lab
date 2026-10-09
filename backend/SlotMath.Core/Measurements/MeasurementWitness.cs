namespace SlotMath.Core.Measurements;

/// <summary>Coordinates identify a deterministic paid round and the authored observation.
/// No user state or random numbers are retained. Min/max are examples, never reachable bounds.</summary>
public sealed record MeasurementWitness(long RoundIndex, long ObservationOrdinal, string? NodeId,
    string Kind, double? Value, double? Pair, string? Group, string? Detail);

internal sealed class WitnessAccumulator
{
    private readonly Dictionary<string, MeasurementWitness> _items = new(StringComparer.Ordinal);
    public void Clear() => _items.Clear();
    public bool IsCandidate(string kind, double? value, long round, long ordinal)
    {
        if (!_items.TryGetValue(kind, out var current)) return true;
        if (kind == "minimum" && value < current.Value || kind == "maximum" && value > current.Value) return true;
        if (kind is "minimum" or "maximum" && value != current.Value) return false;
        return round < current.RoundIndex || round == current.RoundIndex && ordinal < current.ObservationOrdinal;
    }
    public void Add(MeasurementWitness witness)
    {
        if (_items.TryGetValue(witness.Kind, out var current))
        {
            var better = witness.Kind == "minimum" ? witness.Value < current.Value
                : witness.Kind == "maximum" ? witness.Value > current.Value : false;
            if (!better && (witness.Value != current.Value && witness.Kind is "minimum" or "maximum"
                || witness.RoundIndex > current.RoundIndex || witness.RoundIndex == current.RoundIndex && witness.ObservationOrdinal >= current.ObservationOrdinal)) return;
        }
        // A closed set of kinds bounds both per-round and merged storage.
        if (witness.Kind is "first" or "minimum" or "maximum" or "invalid" or "duplicateAward" or "unexpectedSupport" or "assertionViolation") _items[witness.Kind] = witness;
    }
    public void Merge(WitnessAccumulator source) { foreach (var witness in source._items.Values) Add(witness); }
    public MeasurementWitness[] Snapshot() => _items.Values.OrderBy(w => w.Kind, StringComparer.Ordinal).ToArray();
}
