using SlotMath.Core.Measurements;

namespace SlotMath.Core.Math;

/// <summary>Execution population, pinned separately from observation/presentation choices.
/// Sessions have a fixed horizon and report ruin as a first-passage event; they do not
/// silently remove post-ruin rounds from return denominators.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ExecutionOptions
{
    public string SamplingEngine { get; init; } = "auto";
    public string Regime { get; init; } = "independentRounds";
    public string[] PersistentKeys { get; init; } = [];
    public int SessionLength { get; init; } = 100;
    public double InitialBankroll { get; init; } = 100;
    public double Wager { get; init; } = 1;
    public string? FeatureMetricId { get; init; }
    public string StreamScheme => Regime switch
    {
        "persistent" => "splitmix64-persistent-v1",
        "sessions" => $"splitmix64-session-v1-{SessionLength}",
        _ => "splitmix64-chunk-65536"
    };
    public void Validate(long rounds, int workers)
    {
        if (SamplingEngine is not ("auto" or "reference")) throw new ArgumentException("Choose automatic compiled sampling or the canonical reference interpreter.");
        if (Regime is not ("independentRounds" or "persistent" or "sessions")) throw new ArgumentException("Unknown execution regime.");
        if (FeatureMetricId is not null && (Regime != "sessions" || FeatureMetricId.Length is < 1 or > 64)) throw new ArgumentException("Session feature-wait tracking requires a valid round-activation metric ID and session execution.");
        if (PersistentKeys is null || PersistentKeys.Length > 64 || PersistentKeys.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 128)
            || PersistentKeys.Distinct(StringComparer.Ordinal).Count() != PersistentKeys.Length) throw new ArgumentException("Use at most 64 distinct persistent state keys.");
        if (Regime == "independentRounds" && PersistentKeys.Length > 0) throw new ArgumentException("Independent rounds cannot carry state between rounds.");
        if (Regime == "persistent" && workers != 1) throw new ArgumentException("A single persistent trajectory requires one worker; parallel chunks cannot reset its state.");
        if (SessionLength is < 1 or > 65536 || Regime == "sessions" && (rounds % SessionLength != 0 || rounds / SessionLength > 10_000_000))
            throw new ArgumentException("Use complete sessions of 1..65536 rounds and at most 10000000 sessions.");
        if (!double.IsFinite(InitialBankroll) || InitialBankroll < 0 || !double.IsFinite(Wager) || Wager <= 0
            || Wager > 1e12 || InitialBankroll > 1e15) throw new ArgumentException("Use a finite nonnegative bankroll and positive wager within the execution budget.");
    }
}

public sealed record ExecutionSummary(string Regime, long AttemptedRounds, long CompletedRounds, long InterruptedRounds,
    long CancelledRounds, long FailedRounds, long CompletedSessions, long InterruptedSessions,
    bool CarriesState, string StateResetPolicy, string SessionPolicy, IReadOnlyList<MeasurementSnapshot> SessionMetrics)
{
    public string SamplingEngine { get; init; } = "unspecified";
    public LoopTerminationSummary[] LoopTerminations { get; init; } = [];
    public bool LoopTerminationsComplete { get; init; } = true;
}

/// <summary>One logical independent session. Memory is constant in the horizon.</summary>
internal sealed class SessionTrajectory
{
    private readonly double initialBankroll, wager;
    public SessionTrajectory(double initialBankroll, double wager) { this.initialBankroll = initialBankroll; this.wager = wager; _peak = initialBankroll; }
    private double _profit, _peak, _drawdown, _maximum;
    private long _rounds, _drought, _longestDrought;
    private long? _ruinAt;
    private long? _featureAt;
    private bool _invalidFeature;
    public void Add(double payout, bool? feature = false)
    {
        _rounds++; _profit += payout - wager;
        var balance = initialBankroll + _profit;
        _peak = System.Math.Max(_peak, balance); _drawdown = System.Math.Max(_drawdown, _peak - balance);
        _maximum = System.Math.Max(_maximum, payout);
        if (payout == 0) { _drought++; _longestDrought = System.Math.Max(_longestDrought, _drought); } else _drought = 0;
        // Ruin means unable to fund the next fixed wager. Equality can fund it.
        if (balance < wager && _ruinAt is null) _ruinAt = _rounds;
        if (feature is null) _invalidFeature = true; else if (feature.Value && _featureAt is null) _featureAt = _rounds;
        if (!double.IsFinite(_profit) || !double.IsFinite(_drawdown)) throw new ArithmeticException("Session accounting exceeds finite range.");
    }
    public void Commit(SessionEvidence evidence, bool trackFeature = false)
    {
        evidence.Add("return", (_profit + _rounds * wager) / (_rounds * wager));
        evidence.Add("profit", _profit); evidence.Add("profitable", _profit > 0 ? 1 : 0);
        evidence.Add("endingBankroll", initialBankroll + _profit); evidence.Add("drawdown", _drawdown);
        evidence.Add("ruin", _ruinAt is null && initialBankroll >= wager ? 0 : 1);
        // Censored non-ruined sessions are excluded from the first-passage mean.
        if (initialBankroll < wager) evidence.Add("ruinTime", 0); else if (_ruinAt is { } at) evidence.Add("ruinTime", at); else evidence.Exclude("ruinTime");
        evidence.Add("duration", _rounds); evidence.Add("drought", _longestDrought); evidence.Add("extreme", _maximum);
        if (trackFeature)
        {
            if (_invalidFeature) { evidence.Error("featureSeen"); evidence.Error("featureWait"); }
            else { evidence.Add("featureSeen", _featureAt is null ? 0 : 1); if (_featureAt is { } first) evidence.Add("featureWait", first); else evidence.Exclude("featureWait"); }
        }
    }
}
internal sealed class SessionEvidence
{
    private readonly Dictionary<string, MeasurementAccumulator> _metrics = new(StringComparer.Ordinal);
    private MeasurementAccumulator Get(string name)
    {
        if (!_metrics.TryGetValue(name, out var accumulator)) accumulator.Analysis = new(new() { Subject = "session", IndependentSubjects = true,
            Source = name is "ruin" or "profitable" or "featureSeen" ? "event" : "value", BinEdges = [-100, -10, 0, 1, 5, 10, 50, 100, 500, 1000] });
        return accumulator;
    }
    public void Add(string name, double value)
    {
        var accumulator = Get(name);
        accumulator.Observations++; accumulator.Add(value); accumulator.Analysis!.Add(value, null, null, null); _metrics[name] = accumulator;
    }
    public void Exclude(string name) { var accumulator = Get(name); accumulator.Observations++; accumulator.Excluded++; _metrics[name] = accumulator; }
    public void Error(string name) { var accumulator = Get(name); accumulator.Observations++; accumulator.Errors++; accumulator.FirstError ??= "The configured feature-activation subject was invalid; this complete session cannot establish feature waiting evidence."; _metrics[name] = accumulator; }
    public void Merge(SessionEvidence other)
    { foreach (var (name, source) in other._metrics) { _metrics.TryGetValue(name, out var target); target.Merge(source); _metrics[name] = target; } }
    public MeasurementSnapshot[] Snapshot() => _metrics.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value.Snapshot($"session.{p.Key}", false)).ToArray();
}
