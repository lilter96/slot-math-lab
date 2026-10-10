using System.Text.Json;
using Hangfire;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Runs;
public sealed record ExperimentVariant(string Name, string Kind, string Key, double Value, string? NodeId = null);
public sealed record ExperimentRequest(string ConfigId, int ConfigVersion, ExperimentVariant[] Variants,
    MeasurementInput[] Measurements, ExecutionOptions? Execution = null, int Samples = 10000, long Seed = 42,
    double Confidence = .95, double Precision = .005, string[]? MetricIds = null, double[]? TailThresholds = null);
public sealed record ExperimentMember(string Name, string RunId, string ConfigId, string ConfigHash, long Seed, ExperimentVariant? Perturbation) { public double? ParameterStep { get; init; } }
public sealed record ExperimentEntry(string Id, string Status, DateTimeOffset CreatedAt, string ManifestSha256,
    ExperimentRequest Request, ExperimentMember[] Members, string? Error = null);
public sealed record ExperimentDelta(string Variant, string Metric, double? Baseline, double? Observed, double? Difference,
    double? Derivative, NumericInterval? Interval, double? SuggestedSamplesPerArm, string Assumptions);
public sealed record ExperimentReport(ExperimentEntry Experiment, RunResponse[] Runs, ExperimentDelta[] Deltas, int HypothesisFamilySize);

/// <summary>Encrypted single-writer manifest storage. Restart explicitly interrupts an
/// experiment; replay creates a new identity. Accepted mutations are never retried.</summary>
public sealed class ExperimentStore
{
    private readonly object _gate = new(); private readonly EncryptedSnapshots? _snapshots;
    private readonly Dictionary<string, ExperimentEntry> _entries;
    public ExperimentStore(EncryptedSnapshots? snapshots = null)
    {
        _snapshots = snapshots; _entries = snapshots?.Read<Dictionary<string, ExperimentEntry>>("experiments") ?? new();
        foreach (var (id, e) in _entries.ToArray()) if (e.Status is "preparing" or "pending" or "running")
            _entries[id] = e with { Status = "interrupted", Error = "Server restarted; pinned child results are retained. Explicitly replay the manifest." };
        if (_entries.Count > 0) Persist();
    }
    public ExperimentEntry? Get(string id) { lock (_gate) return _entries.GetValueOrDefault(id); }
    public ExperimentEntry[] List() { lock (_gate) return _entries.Values.OrderByDescending(e => e.CreatedAt).Take(100).ToArray(); }
    public bool CanAdmit() { lock (_gate) return _entries.Count < 1000 && _entries.Values.Count(e => e.Status is "preparing" or "pending" or "running") < 2; }
    public void Create(ExperimentEntry e) { lock (_gate) { if (!CanAdmit()) throw new ArgumentException("Experiment capacity is full."); _entries.Add(e.Id, e); Persist(); } }
    public void Status(string id, string status, string? error = null) { lock (_gate) { if (_entries.TryGetValue(id, out var e) && e.Status is "preparing" or "pending" or "running") { _entries[id] = e with { Status = status, Error = error }; Persist(); } } }
    public ExperimentEntry CompleteAdmission(string id, ExperimentMember[] members)
    {
        lock (_gate)
        {
            var current = _entries[id]; if (current.Status != "preparing") throw new InvalidOperationException("Experiment admission was interrupted.");
            var admitted = current with { Members = members, ManifestSha256 = RuntimeProvenance.AuthoredInputHash(new { request = current.Request, members }), Status = "pending" };
            _entries[id] = admitted; Persist(); return admitted;
        }
    }
    private void Persist() => _snapshots?.Write("experiments", _entries);
}

public sealed class ExperimentJobService(ExperimentStore experiments, InMemoryRunStore runs, RunJobService runner, IHostApplicationLifetime lifetime)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(string id)
    {
        var experiment = experiments.Get(id) ?? throw new InvalidOperationException("Experiment manifest missing.");
        if (experiment.Status != "pending") return;
        if (experiment.ManifestSha256 != RuntimeProvenance.AuthoredInputHash(new { request = experiment.Request, members = experiment.Members })) { experiments.Status(id, "failed", "Experiment manifest fingerprint mismatch."); CancelMembers(experiment, runs); return; }
        experiments.Status(id, "running");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var registration = deadline.Token.Register(() => { CancelMembers(experiment, runs, lifetime.ApplicationStopping.IsCancellationRequested ? "shutdown" : "resourceExpiry"); });
        try
        {
            foreach (var member in experiment.Members)
            {
                if (experiments.Get(id)?.Status == "cancelled") return;
                deadline.Token.ThrowIfCancellationRequested();
                await runner.ExecuteRunAsync(member.RunId, experiment.Request.Samples, Math.Max(100, experiment.Request.Samples / 100));
                if (runs.Get(member.RunId)?.Status != "completed") throw new InvalidOperationException($"Variant {member.Name} did not complete; comparison withheld.");
            }
            var completed = experiments.Get(id)! with { Status = "completed" };
            runs.RetainDiagnostic(completed.Members[0].RunId, "parameter-experiment", completed.Request, ExperimentAnalysis.Report(completed, runs));
            experiments.Status(id, "completed");
        }
        catch (Exception ex)
        {
            CancelMembers(experiment, runs);
            if (experiments.Get(id)?.Status != "cancelled") experiments.Status(id, deadline.IsCancellationRequested ? "interrupted" : "failed", ex.Message);
        }
    }
    public static void CancelMembers(ExperimentEntry experiment, InMemoryRunStore runs, string reason = "userCancellation")
    {
        foreach (var member in experiment.Members)
        {
            var pending = runs.Get(member.RunId)?.Status == "pending";
            runs.Cancel(member.RunId, reason);
            if (pending) runs.Update(member.RunId, "cancelled", JsonSerializer.Serialize(new { status = "cancelled", sampleCount = 0, error = "Experiment stopped before this arm started." }));
        }
    }
}

public static class ExperimentAnalysis
{
    public static GraphConfig Apply(GraphConfig graph, ExperimentVariant variant)
    {
        if (string.IsNullOrWhiteSpace(variant.Name) || variant.Name.Length > 80 || variant.Key is null || variant.Key.Length > 128 || !double.IsFinite(variant.Value)) throw new ArgumentException("Perturbations need a name, a finite value and a bounded parameter key.");
        if (variant.Kind == "initialState")
        {
            if (graph.InitialState is null || !graph.InitialState.TryGetValue(variant.Key, out var previous) || previous.ValueKind != JsonValueKind.Number)
                throw new ArgumentException("Choose an existing numeric initial-state parameter.");
            var state = new Dictionary<string, JsonElement>(graph.InitialState) { [variant.Key] = JsonSerializer.SerializeToElement(variant.Value) };
            return graph with { InitialState = state };
        }
        if (variant.Kind is "execution" or "sessionPolicy") return graph;
        var found = false;
        var nodes = graph.Nodes.Select(node => {
            if (node.Id != variant.NodeId) return node; found = true;
            if (variant.Value < 0 || variant.Value != Math.Truncate(variant.Value) || variant.Value > 9007199254740991d) throw new ArgumentException("Graph limits and weights require nonnegative safe integers.");
            return variant.Kind switch {
                "winCap" when node is MetricsSinkNode sink && variant.Value >= 1 => sink with { WinCap = (long)variant.Value },
                "loopCap" when node is LoopNode loop && variant.Value is >= 1 and <= 100000 => loop with { MaxIterations = (int)variant.Value },
                "drawWeight" when node is DrawNode draw && draw.DrawWeights?.Any(w => w.OutcomeId == variant.Key) == true && variant.Value <= int.MaxValue => draw with { DrawWeights = draw.DrawWeights.Select(w => w.OutcomeId == variant.Key ? w with { Weight = (int)variant.Value } : w).ToArray() },
                _ => throw new ArgumentException("Parameter kind does not match the selected graph node.")
            };
        }).ToArray();
        if (!found) throw new ArgumentException("Perturbation node does not exist.");
        return graph with { Nodes = nodes };
    }
    public static ExecutionOptions Execution(ExecutionOptions? value, ExperimentVariant? variant)
    {
        var execution = value ?? new();
        if (variant?.Kind == "sessionPolicy") { if (execution.Regime != "sessions") throw new ArgumentException("Policy experiments require session execution."); return execution with { SessionStop = variant.Key, StopThreshold = variant.Value }; }
        if (variant?.Kind != "execution") return execution;
        return variant.Key switch {
            "initialBankroll" => execution with { InitialBankroll = variant.Value }, "wager" => execution with { Wager = variant.Value },
            "stopThreshold" => execution with { StopThreshold = variant.Value },
            "sessionLength" when variant.Value == Math.Truncate(variant.Value) && variant.Value is >= 1 and <= 65536 => execution with { SessionLength = (int)variant.Value },
            _ => throw new ArgumentException("Execution parameter must be initialBankroll, wager, stopThreshold or sessionLength.")
        };
    }
    public static ExperimentReport Report(ExperimentEntry experiment, InMemoryRunStore store)
    {
        var request = experiment.Request; var runs = experiment.Members.Select(m => store.Get(m.RunId)).ToArray();
        var deltas = new List<ExperimentDelta>(); var ids = request.MetricIds ?? []; var tails = request.TailThresholds ?? [];
        var family = Math.Max(1, (runs.Length - 1) * (2 + ids.Length * (1 + tails.Length)));
        var z = StatisticalInference.NormalCritical((1 - request.Confidence) / family);
        if (runs.FirstOrDefault() is { Status: "completed", Progress: { } baseline })
            for (var i = 1; i < runs.Length; i++) if (runs[i] is { Status: "completed", Progress: { } other } variant)
            {
                var member = experiment.Members[i];
                var independent = variant.Execution is not { PersistentKeys.Length: > 0 } && variant.Execution is not { SessionStop: not "fixedHorizon" } && runs[0]!.Execution is not { PersistentKeys.Length: > 0 } && runs[0]!.Execution is not { SessionStop: not "fixedHorizon" };
                void Add(string metric, double? b, double? v, double? bVariance, double? vVariance, long bn, long vn, bool allowed)
                {
                    double? difference = b is { } bval && v is { } vval ? vval - bval : null;
                    var se = allowed && bVariance is >= 0 && vVariance is >= 0 && bn > 1 && vn > 1 ? Math.Sqrt(bVariance.Value / bn + vVariance.Value / vn) : (double?)null;
                    NumericInterval? interval = difference is { } d && se is > 0 ? new(d - z * se.Value, d + z * se.Value, "Independent-arm fixed-count normal approximation", "Prespecified variants and family allocation; no paired common-random-number claim.") : null;
                    double? derivative = member.ParameterStep is { } step && step != 0 && difference is { } delta ? delta / step : null;
                    deltas.Add(new(member.Name, metric, b, v, difference, derivative, interval,
                        bVariance is { } bv && vVariance is { } vv && bv + vv > 0 && allowed ? Math.Ceiling(z * z * (bv + vv) / (request.Precision * request.Precision)) : null,
                        allowed ? "Independent seed per arm; sample planning is approximate and based on observed variance. Zero observed variance does not certify precision." : "Descriptive delta only: this population does not establish independent fixed-count subject inference."));
                }
                Add("round.return", baseline.RunningRtp, other.RunningRtp, baseline.Volatility * baseline.Volatility, other.Volatility * other.Volatility, baseline.SampleCount, other.SampleCount, independent);
                Add("round.hit", baseline.HitFrequency, other.HitFrequency, baseline.HitFrequency * (1 - baseline.HitFrequency), other.HitFrequency * (1 - other.HitFrequency), baseline.SampleCount, other.SampleCount, independent);
                foreach (var id in ids)
                {
                    var sessionMetric = id.StartsWith("session.", StringComparison.Ordinal);
                    var b = (sessionMetric ? baseline.Execution?.SessionMetrics : baseline.Measurements)?.FirstOrDefault(m => m.Id == id); var v = (sessionMetric ? other.Execution?.SessionMetrics : other.Measurements)?.FirstOrDefault(m => m.Id == id);
                    var definition = variant.Measurements.FirstOrDefault(m => m.Id == id);
                    var allowed = sessionMetric ? baseline.Execution?.Regime == "sessions" && other.Execution?.Regime == "sessions" : independent && definition?.Options is { IndependentSubjects: true, Weight: null };
                    if (id == "session.payoutPerTurnover")
                    {
                        double? RatioVariance(MeasurementSnapshot? metric)
                        {
                            var a = metric?.Analysis; var pair = a?.Pair;
                            if (pair?.Ratio is not { } ratio || pair.MeanY is not > 0 || pair.SampleVarianceY is null || pair.Covariance is null || a?.Moments.SampleVariance is null) return null;
                            var variance = (a.Moments.SampleVariance.Value + ratio * ratio * pair.SampleVarianceY.Value - 2 * ratio * pair.Covariance.Value) / (pair.MeanY.Value * pair.MeanY.Value);
                            return double.IsFinite(variance) ? Math.Max(0, variance) : null;
                        }
                        Add(id + ".ratio", b?.Analysis?.Pair?.Ratio, v?.Analysis?.Pair?.Ratio, RatioVariance(b), RatioVariance(v), b?.Count ?? 0, v?.Count ?? 0, allowed);
                    }
                    else Add(id + ".mean", b?.Mean, v?.Mean, b?.Analysis?.Moments.SampleVariance, v?.Analysis?.Moments.SampleVariance, b?.Count ?? 0, v?.Count ?? 0, allowed);
                    foreach (var t in tails)
                    {
                        var bp = b?.Analysis?.Tails.FirstOrDefault(x => x.Threshold == t)?.Probability; var vp = v?.Analysis?.Tails.FirstOrDefault(x => x.Threshold == t)?.Probability;
                        Add(id + $".tail[{t:R}]", bp, vp, bp * (1 - bp), vp * (1 - vp), b?.Count ?? 0, v?.Count ?? 0, allowed);
                    }
                }
            }
        return new(experiment, runs.Where(r => r is not null).Select(r => RunResponse.From(r!)).ToArray(), deltas.ToArray(), family);
    }
}

public static class ExperimentEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/experiments", (ExperimentStore experiments) => Results.Ok(experiments.List()));
        group.MapGet("/experiments/{id}", (string id, ExperimentStore experiments, InMemoryRunStore runs) => experiments.Get(id) is { } e ? Results.Ok(ExperimentAnalysis.Report(e, runs)) : Results.NotFound()).Produces<ExperimentReport>();
        group.MapDelete("/experiments/{id}", (string id, ExperimentStore experiments, InMemoryRunStore runs) => {
            var e = experiments.Get(id); if (e is null) return Results.NotFound();
            if (e.Status is not ("pending" or "running")) return Results.Conflict(new { error = "Experiment is terminal." });
            experiments.Status(id, "cancelled"); ExperimentJobService.CancelMembers(e, runs); return Results.Ok(experiments.Get(id));
        });
        group.MapPost("/experiments", (ExperimentRequest request, ExperimentStore experiments, InMemoryConfigStore configs, InMemoryRunStore runs,
            CompiledGraphCache compiler, IBackgroundJobClient jobs, IWebHostEnvironment environment) => {
            string? admissionId = null; var members = new List<ExperimentMember>();
            try
            {
                if (!experiments.CanAdmit()) return Results.Json(new { error = "Experiment capacity is full." }, statusCode: 429);
                if (request.Variants is not { Length: > 0 and <= 8 } || request.Variants.Any(v => v is null) || request.Variants.Select(v => v.Name).Distinct().Count() != request.Variants.Length
                    || request.Samples < 2 || (long)(request.Variants.Length + 1) * request.Samples > 10000000 || request.ConfigVersion < 1 || string.IsNullOrWhiteSpace(request.ConfigId)
                    || request.Seed < -9007199254740991 || request.Seed > 9007199254740991 - request.Variants.Length
                    || !double.IsFinite(request.Confidence) || request.Confidence <= .5 || request.Confidence >= .999999 || !double.IsFinite(request.Precision) || request.Precision <= 0
                    || request.Measurements is null || request.Measurements.Length > 32 || request.Measurements.Any(m => m is null)
                    || request.MetricIds is { Length: > 4 } || request.TailThresholds is { Length: > 4 } || request.TailThresholds?.Any(t => !double.IsFinite(t)) == true)
                    throw new ArgumentException("Use 1..8 named variants, at most 10 million total round slots, four comparison metrics/tails, valid precision and confidence.");
                var baseline = configs.GetVersion(request.ConfigId, request.ConfigVersion) ?? throw new ArgumentException("Pinned baseline config version not found.");
                var plan = request.Measurements.Select(m => m.ToCore()).ToArray();
                if ((request.MetricIds ?? []).Any(id => !plan.Any(m => m.Id == id) && !new[] { "session.payoutPerTurnover", "session.return", "session.profit", "session.ruin", "session.duration", "session.featureSeen", "session.drawdown", "session.extreme" }.Contains(id)) || (request.MetricIds ?? []).Distinct().Count() != (request.MetricIds ?? []).Length
                    || (request.TailThresholds ?? []).Distinct().Count() != (request.TailThresholds ?? []).Length)
                    throw new ArgumentException("Comparison IDs must be unique pinned measurements; thresholds must be unique.");
                var variants = new ExperimentVariant?[] { null }.Concat(request.Variants).ToArray();
                var candidates = variants.Select(v => (Graph: v is null ? baseline.Config : ExperimentAnalysis.Apply(baseline.Config, v), Execution: ExperimentAnalysis.Execution(request.Execution, v))).ToArray();
                foreach (var candidate in candidates)
                {
                    candidate.Execution.Validate(request.Samples, 1);
                    if (environment.IsProduction() && (candidate.Graph.Plugins.Length > 0 || candidate.Graph.Nodes.OfType<MapNode>().Any(n => n.TransformId?.StartsWith("plugin:") == true))) throw new ArgumentException("Plugin execution is disabled in production.");
                    var compiled = compiler.Compile(candidate.Graph, plan); if (!compiled.IsValid) throw new ArgumentException(string.Join("; ", compiled.Errors.Select(e => e.Message)));
                    if (candidate.Execution.PersistentKeys.Any(k => compiled.MeasurementSchema?.Fields.Any(f => f.Name == k) != true)) throw new ArgumentException("Persistent keys must exist in each candidate graph.");
                    if ((candidate.Execution.PersistentKeys.Length > 0 || candidate.Execution.SessionStop != "fixedHorizon") && plan.Any(m => m.Options is { IndependentSubjects: true } or { IndependentParents: true })) throw new ArgumentException("Dependent or stopped paid rounds cannot assert independent-round inference.");
                    if (candidate.Execution.FeatureMetricId is { } feature && !plan.Any(m => m.Id == feature && m.Options is { Source: "event" or "count", Subject: "round", Reduction: "any" } || m.Id == feature && m.NodeId is null && m.Options is { Source: "event", Subject: "observation" })) throw new ArgumentException("Feature stopping requires a complete paid-round activation metric.");
                }
                admissionId = Guid.NewGuid().ToString("N");
                experiments.Create(new(admissionId, "preparing", DateTimeOffset.UtcNow, RuntimeProvenance.AuthoredInputHash(request), request, []));
                double Original(ExperimentVariant v) => v.Kind switch {
                    "initialState" => baseline.Config.InitialState![v.Key].GetDouble(),
                    "winCap" => baseline.Config.Nodes.OfType<MetricsSinkNode>().Single(n => n.Id == v.NodeId).WinCap!.Value,
                    "loopCap" => baseline.Config.Nodes.OfType<LoopNode>().Single(n => n.Id == v.NodeId).MaxIterations,
                    "drawWeight" => baseline.Config.Nodes.OfType<DrawNode>().Single(n => n.Id == v.NodeId).DrawWeights!.Single(w => w.OutcomeId == v.Key).Weight,
                    "execution" => v.Key switch { "initialBankroll" => (request.Execution ?? new()).InitialBankroll, "wager" => (request.Execution ?? new()).Wager, "stopThreshold" => (request.Execution ?? new()).StopThreshold, _ => (request.Execution ?? new()).SessionLength },
                    _ => throw new ArgumentException("Unsupported perturbation")
                };
                for (var i = 0; i < candidates.Length; i++)
                {
                    var c = candidates[i]; var id = configs.Create(c.Graph with { Id = null }); var pinned = configs.GetLatest(id)!; var seed = request.Seed + i;
                    var run = runs.Create(id, seed, pinned.Version, request.Samples, CanonicalHash.Compute(pinned.Config), 1, plan, c.Execution, externalEvidence: pinned.Config.EvidenceInputs);
                    runs.CreateCancellationToken(run.Id); members.Add(new(variants[i]?.Name ?? "Baseline", run.Id, id, run.ConfigHash!, seed, variants[i]) { ParameterStep = variants[i] is { Kind: not "sessionPolicy" } perturbation ? perturbation.Value - Original(perturbation) : null });
                }
                var experiment = experiments.CompleteAdmission(admissionId, members.ToArray());
                jobs.Enqueue<ExperimentJobService>(job => job.ExecuteAsync(experiment.Id));
                return Results.Accepted($"/api/runs/experiments/{experiment.Id}", experiment);
            }
            catch (Exception ex)
            {
                if (admissionId is not null)
                {
                    var pending = experiments.Get(admissionId)!;
                    ExperimentJobService.CancelMembers(pending with { Members = members.ToArray() }, runs);
                    experiments.Status(admissionId, "failed", "Admission failed; allocated child identities were cancelled.");
                }
                if (ex is ArgumentException or JsonException or FormatException or ArithmeticException) return Results.BadRequest(new { error = ex.Message });
                throw;
            }
        }).Produces<ExperimentEntry>(202).RequireRateLimiting("compute");
    }
}
