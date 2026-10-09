namespace SlotMath.Core.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  SampledProgress — progress snapshot emitted by the sampler during a run
//
//  Emitted every ProgressReportInterval spins.  Carries a snapshot of the
//  current streaming statistics, elapsed time, and spin position.
//  Consumed by the Hangfire job layer to push to SignalR.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A point-in-time progress snapshot from a running sampled evaluation.
/// </summary>
public sealed record SampledProgress
{
    /// <summary>Number of spins completed so far.</summary>
    public IReadOnlyList<SlotMath.Core.Measurements.MeasurementSnapshot> Measurements { get; init; } = [];
    public long SpinsCompleted { get; init; }

    /// <summary>Total number of spins requested for this run.</summary>
    public long TotalSpins { get; init; }

    /// <summary>Snapshot of streaming statistics at this point.</summary>
    public StreamingStatsSnapshot Stats { get; init; } = null!;

    /// <summary>Wall-clock time elapsed since the run started.</summary>
    public TimeSpan Elapsed { get; init; }
}
