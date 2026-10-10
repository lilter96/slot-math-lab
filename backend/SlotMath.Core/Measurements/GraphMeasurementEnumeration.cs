using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using SlotMath.Core.Model;
using SlotMath.Core.Monad;
using SlotMath.Core.Random;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Measurements;
public sealed record EnumerationBudget(int MaximumPaths = 10000, int MaximumOperations = 250000, int MaximumFrontier = 2048);
public sealed record EnumeratedValue(double Value, string MassPerPaidRound);
public sealed record EnumeratedJointValue(double X, double Y, string MassPerPaidRound);
public sealed record EnumeratedJointLaw(string PairedPerRound, EnumeratedJointValue[] Support, bool Complete,
    string? MeanX, string? MeanY, string? VarianceX, string? VarianceY, string? Covariance, string? VarianceSum, string? VarianceDifference);
public sealed record EnumeratedCohort(string Key, string ValidPerRound, string KnownSumPerRound, string? ConditionalMean,
    EnumeratedValue[] Support, bool SupportComplete, EnumeratedJointLaw? Pair)
{ public EnumeratedParentExposure? ParentExposure { get; init; } }
public sealed record EnumeratedParentExposure(string KnownMatchingPaidRoundsPerRound, string? KnownMatchingEpisodesPerRound, bool Complete);
public sealed record EnumeratedMeasurement(string Id, string EligiblePerRound, string ValidPerRound, string ExcludedPerRound, string InvalidPerRound,
    string KnownSumPerRound, string? ConditionalMean, EnumeratedValue[] Support, bool SupportComplete, string? FirstError)
{
    public EnumeratedCohort[] Groups { get; init; } = [];
    public bool GroupsComplete { get; init; } = true;
    public EnumeratedJointLaw? Pair { get; init; }
    public string Weighting { get; init; } = "Unweighted occurrence law under the authored graph";
    public string? KnownAssertionViolationsPerRound { get; init; }
    public string? AssertionStatus { get; init; }
    public EnumeratedParentExposure? ParentExposure { get; init; }
}
public sealed record GraphEnumerationReport(string Status, int CompletedPaths, int Operations, string RetainedRoundMass, string UnresolvedRoundMass,
    RationalBounds RoundMean, EnumeratedMeasurement[] Measurements, string NumericalSemantics)
{ public string? MaximumKnownPayout { get; init; } public string? ReachableMaximum { get; init; } }

/// <summary>Independent forward traversal of the canonical IR. Small models retain the full
/// subject law, including repeated reveals and absent-feature round zeros. A cut path keeps
/// its probability mass unresolved; conditional evidence is never renormalized into a proof.</summary>
public static class GraphMeasurementEnumeration
{
    public static GraphEnumerationReport Evaluate(CompileResult compiled, IReadOnlyList<MeasurementDefinition> definitions, BigInteger maximumPayout,
        EnumerationBudget? requestedBudget = null, CancellationToken cancellationToken = default)
    {
        if (!compiled.IsValid || compiled.ReferenceProgram is null) throw new ArgumentException("Use a valid canonical compiled graph.");
        if (maximumPayout.Sign < 0 || maximumPayout.GetBitLength() > 256) throw new ArgumentException("Enumeration requires a finite proven final payout bound.");
        var budget = requestedBudget ?? new();
        if (budget.MaximumPaths is < 1 or > 100000 || budget.MaximumOperations is < 1 or > 1000000 || budget.MaximumFrontier is < 1 or > 8192) throw new ArgumentException("Enumeration exceeds its bounded path, operation or frontier budget.");
        var plans = definitions.Select(d => d with { Options = (d.Options ?? new()) with { IndependentSubjects = false, IndependentParents = false, ReferenceMean = null, ReferenceDistribution = [] } }).ToArray();
        Func<EvalContext, ExprValue>? Bind(Expression? expression) => expression is null ? null : ctx => ExactExpressionEvaluator.Evaluate(expression, ctx);
        var bindings = plans.Select(d => new MeasurementBinding<EvalContext>(Bind(d.Value), Bind(d.Filter), Bind(d.Options!.Group), Bind(d.Options.Pair), Bind(d.Options.Weight), Bind(d.Options.AwardId), Bind(d.Options.EntryFilter), Bind(d.Options.ExitFilter), Bind(d.Options.ExitReason))).ToArray();
        var points = plans.SelectMany((d, i) => new[] { d.NodeId, d.Options!.EntryNodeId, d.Options.ExitNodeId }.OfType<string>().Distinct().Select(node => (node, i)))
            .GroupBy(p => p.node).ToDictionary(g => g.Key, g => g.Select(p => p.i).ToArray());
        var outputs = plans.Select(d => new Law(d.Id, d.Options!)).ToArray();
        var frontier = new Stack<Path>(); frontier.Push(new(compiled.ReferenceProgram, new(), Rational.One, [], null, null));
        Rational retained = Rational.Zero, unresolved = Rational.Zero, payoutSum = Rational.Zero, maximumKnown = Rational.Zero;
        var operations = 0; var completed = 0;
        while (frontier.TryPop(out var path))
        {
            var current = path.Current; var state = path.State; var stack = path.Stack; var trace = path.Trace; var raw = path.Raw;
            for (;;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (completed >= budget.MaximumPaths || ++operations > budget.MaximumOperations) { unresolved += path.Mass; break; }
                if (current is IFlatMapNode flat) { stack.Add(new(flat, null)); current = flat.SourceUntyped; continue; }
                if (current is IPureNode pure)
                {
                    if (stack.Count > 0)
                    {
                        var continuation = stack[^1]; stack.RemoveAt(stack.Count - 1);
                        if (continuation.Settlement is { } settlement) { raw = (BigInteger)pure.ValueUntyped; current = new UntypedPure(settlement.Settle(pure.ValueUntyped)); }
                        else current = continuation.Map!.ApplyUntyped(pure.ValueUntyped);
                        continue;
                    }
                    var payout = new Rational((BigInteger)pure.ValueUntyped, compiled.WinScale);
                    if (payout < Rational.Zero || payout > new Rational(maximumPayout, BigInteger.One)) throw new InvalidOperationException("Canonical payout violates the proven enumeration bound.");
                    if (payout > maximumKnown) maximumKnown = payout;
                    completed++; retained = Checked(retained + path.Mass); payoutSum = Checked(payoutSum + path.Mass * payout);
                    var collector = new MeasurementCollector(plans); collector.Begin();
                    var orderedTrace = new Stack<Trace>(); for (var item = trace; item is not null; item = item.Previous) orderedTrace.Push(item);
                    foreach (var observation in orderedTrace) foreach (var index in points[observation.Node]) collector.Point(index, observation.Node, new EvalContext { State = observation.State }, bindings[index]);
                    for (var i = 0; i < plans.Length; i++) collector.CompleteRound(i, new EvalContext { State = state, Measurement = new(payout.ToDouble(), raw is { } r ? new Rational(r, compiled.WinScale).ToDouble() : null, plans[i].Options!.Stake) }, bindings[i], payout.ToDouble(), raw is { } rawWin ? new Rational(rawWin, compiled.WinScale).ToDouble() : null);
                    collector.Commit(); var snapshots = MeasurementCollector.Snapshot(plans, collector.Total, false);
                    for (var i = 0; i < snapshots.Length; i++) outputs[i].Add(snapshots[i], path.Mass);
                    break;
                }
                if (current is IDrawNode draw)
                {
                    var weights = (WeightSet)draw.WeightsUntyped(state);
                    if (weights.Count > budget.MaximumFrontier - frontier.Count) { unresolved += path.Mass; break; }
                    var positive = weights.Numerators.Count(n => n > 0);
                    if (frontier.Count + positive > budget.MaximumFrontier) { unresolved += path.Mass; break; }
                    for (var i = weights.Count - 1; i >= 0; i--) if (weights.Numerators[i] > 0)
                    {
                        var mass = path.Mass * new Rational(weights.Numerators[i], weights.NumeratorSum);
                        if (mass.Numerator.GetBitLength() > 8192 || mass.Denominator.GetBitLength() > 8192) throw new ArithmeticException("Enumeration probability exceeds its exact-arithmetic budget.");
                        frontier.Push(new(draw.NextUntyped(i), state, mass, new(stack), trace, raw));
                    }
                    break;
                }
                if (current is IGetStateNode get) { current = get.NextUntyped(state); continue; }
                if (current is IPutStateNode put) { state = (Dict)put.ValueUntyped; current = put.NextUntyped; continue; }
                if (current is IModifyStateNode modify) { state = (Dict)modify.ApplyUntyped(state); current = modify.NextUntyped; continue; }
                if (current is ISettlementNode cap) { stack.Add(new(null, cap)); current = cap.RawUntyped; continue; }
                if (current is IAnnotationNode annotation) { if (current is IObservationNode point && points.ContainsKey(point.NodeId)) trace = new(point.NodeId, state, trace); current = annotation.InnerUntyped; continue; }
                if (current is ILoopNode loop) { current = loop.DesugarUntyped; continue; }
                if (current is IEmitNode emit) { current = emit.NextUntyped; continue; }
                if (current is ITruncateNode) { unresolved += path.Mass; break; }
                throw new InvalidOperationException($"Unsupported enumeration node: {current.GetType().Name}.");
            }
        }
        if (retained + unresolved != Rational.One) throw new InvalidOperationException("Enumeration probability conservation failed.");
        var capValue = new Rational(maximumPayout, BigInteger.One);
        return new(unresolved == Rational.Zero ? "Enumerated" : "BoundedEnumeration", completed, operations, retained.ToString(), unresolved.ToString(),
            new(payoutSum.ToString(), (payoutSum + unresolved * capValue).ToString()), outputs.Select(o => o.Snapshot(unresolved == Rational.Zero)).ToArray(),
            "Exact branch probabilities and canonical integer/sub-credit round payouts. Measurement values use the production collector's IEEE-754 conversion and reductions; their probability masses are rational. Repeated observations retain expected exposure per paid round. Cohort and paired laws retain the same exposure convention. Joint moments use exact rational identities of the observed binary64 values. Conditional means are withheld for cut paths, invalid subjects or incomplete value support. Likelihood weights do not transform these graph/proposal occurrence laws into a target law. This checks compiled-IR behavior; use an independently authored specification as another oracle.") { MaximumKnownPayout = completed > 0 ? maximumKnown.ToString() : null, ReachableMaximum = unresolved == Rational.Zero ? maximumKnown.ToString() : null };
    }
    private sealed record Continuation(IFlatMapNode? Map, ISettlementNode? Settlement);
    private sealed record Trace(string Node, Dict State, Trace? Previous);
    private sealed record Path(object Current, Dict State, Rational Mass, List<Continuation> Stack, Trace? Trace, BigInteger? Raw);
    private sealed class UntypedPure(object value) : IPureNode { public object ValueUntyped => value; }
    private static Rational Binary(double value)
    { var exact = new SettlementContext(value, value, 1).Read("payout"); return new(exact.NumberNumerator, exact.NumberDenominator); }
    private static Rational Checked(Rational value)
    {
        if (value.Numerator.GetBitLength() > 16384 || value.Denominator.GetBitLength() > 16384)
            throw new ArithmeticException("Enumeration law exceeds its 16384-bit exact-arithmetic budget; reduce the authored model.");
        return value;
    }
    private sealed class OccurrenceLaw(int limit)
    {
        public Rational Valid { get; private set; } = Rational.Zero;
        public Rational Sum { get; private set; } = Rational.Zero;
        private Rational _matchingPaidParents = Rational.Zero, _matchingEpisodes = Rational.Zero;
        private bool _hasEpisodes;
        private readonly SortedDictionary<double, Rational> _support = new();
        private readonly SortedDictionary<(double X, double Y), Rational> _joint = new();
        private Rational _paired = Rational.Zero;
        private bool _complete = true, _jointComplete = true, _hasPair;
        public void Add(MeasurementAnalysis analysis, Rational mass)
        {
            Valid = Checked(Valid + mass * new Rational(analysis.Count, BigInteger.One));
            if (analysis.ParentExposure is { } exposure)
            {
                _matchingPaidParents = Checked(_matchingPaidParents + mass * new Rational(exposure.PaidRoundsWithMatchingChildren, BigInteger.One));
                if (exposure.EpisodesWithMatchingChildren is { } episodes)
                { _hasEpisodes = true; _matchingEpisodes = Checked(_matchingEpisodes + mass * new Rational(episodes, BigInteger.One)); }
            }
            if (analysis.Sum is { } sum) Sum = Checked(Sum + mass * Binary(sum));
            if (!analysis.SupportComplete) { _complete = false; _support.Clear(); }
            if (_complete) foreach (var atom in analysis.Support)
            {
                if (!_support.ContainsKey(atom.Value) && _support.Count >= limit) { _complete = false; _support.Clear(); break; }
                _support[atom.Value] = Checked(_support.GetValueOrDefault(atom.Value, Rational.Zero) + mass * new Rational(atom.Count, BigInteger.One));
            }
            if (analysis.Pair is not { } pair) return;
            _hasPair = true; _paired = Checked(_paired + mass * new Rational(pair.Count, BigInteger.One));
            if (pair.Joint is not { Complete: true } joint) { _jointComplete = false; _joint.Clear(); return; }
            if (_jointComplete) foreach (var atom in joint.Support)
            {
                var key = (atom.X, atom.Y);
                if (!_joint.ContainsKey(key) && _joint.Count >= limit) { _jointComplete = false; _joint.Clear(); break; }
                _joint[key] = Checked(_joint.GetValueOrDefault(key, Rational.Zero) + mass * new Rational(atom.Count, BigInteger.One));
            }
        }
        public bool Complete(bool validPaths) => validPaths && _complete;
        public EnumeratedParentExposure Parents(bool allPaths, bool episodePopulation) => new(_matchingPaidParents.ToString(),
            episodePopulation || _hasEpisodes ? _matchingEpisodes.ToString() : null, allPaths);
        public string? Mean(bool validPaths) => Complete(validPaths) && Valid > Rational.Zero ? (Sum / Valid).ToString() : null;
        public EnumeratedValue[] Support() => _support.Select(p => new EnumeratedValue(p.Key, p.Value.ToString())).ToArray();
        public EnumeratedJointLaw? Pair(bool validPaths)
        {
            if (!_hasPair) return null;
            var complete = validPaths && _jointComplete;
            Rational sx = Rational.Zero, sy = Rational.Zero, sxx = Rational.Zero, syy = Rational.Zero, sxy = Rational.Zero;
            if (complete && _paired > Rational.Zero)
            {
                foreach (var (key, mass) in _joint)
                {
                    var x = Binary(key.X); var y = Binary(key.Y);
                    sx = Checked(sx + x * mass); sy = Checked(sy + y * mass); sxx = Checked(sxx + x * x * mass); syy = Checked(syy + y * y * mass); sxy = Checked(sxy + x * y * mass);
                }
                var mx = Checked(sx / _paired); var my = Checked(sy / _paired);
                var vx = Checked(sxx / _paired - mx * mx); var vy = Checked(syy / _paired - my * my); var cov = Checked(sxy / _paired - mx * my);
                return new(_paired.ToString(), SupportJoint(), true, mx.ToString(), my.ToString(), vx.ToString(), vy.ToString(), cov.ToString(),
                    (vx + vy + 2 * cov).ToString(), (vx + vy - 2 * cov).ToString());
            }
            return new(_paired.ToString(), SupportJoint(), complete, null, null, null, null, null, null, null);
        }
        private EnumeratedJointValue[] SupportJoint() => _joint.Select(p => new EnumeratedJointValue(p.Key.X, p.Key.Y, p.Value.ToString())).ToArray();
    }
    private sealed class Law(string id, MeasurementOptions options)
    {
        private Rational _eligible = Rational.Zero, _excluded = Rational.Zero, _invalid = Rational.Zero, _violations = Rational.Zero;
        private readonly OccurrenceLaw _overall = new(options.SupportLimit);
        private readonly SortedDictionary<string, OccurrenceLaw> _groups = new(StringComparer.Ordinal);
        private bool _groupsComplete = true; private string? _error;
        public void Add(MeasurementSnapshot snapshot, Rational mass)
        {
            _eligible = Checked(_eligible + mass * new Rational(snapshot.Observations, BigInteger.One));
            _excluded = Checked(_excluded + mass * new Rational(snapshot.Excluded, BigInteger.One)); _invalid = Checked(_invalid + mass * new Rational(snapshot.Errors, BigInteger.One)); _error ??= snapshot.FirstError;
            if (snapshot.Analysis!.Assertion is { } assertion) _violations = Checked(_violations + mass * new Rational(assertion.Violations, BigInteger.One));
            _overall.Add(snapshot.Analysis!, mass);
            if (!snapshot.Analysis!.GroupsComplete) { _groupsComplete = false; _groups.Clear(); }
            if (_groupsComplete) foreach (var (key, analysis) in snapshot.Analysis.Groups)
            {
                if (!_groups.TryGetValue(key, out var group))
                {
                    if (_groups.Count >= options.GroupLimit) { _groupsComplete = false; _groups.Clear(); break; }
                    _groups.Add(key, group = new(options.SupportLimit));
                }
                group.Add(analysis, mass);
            }
        }
        public EnumeratedMeasurement Snapshot(bool allPaths)
        {
            var valid = allPaths && _invalid == Rational.Zero;
            return new(id, _eligible.ToString(), _overall.Valid.ToString(), _excluded.ToString(), _invalid.ToString(), _overall.Sum.ToString(),
                _overall.Mean(valid), _overall.Support(), _overall.Complete(allPaths), _error)
            {
                GroupsComplete = allPaths && _groupsComplete,
                Groups = _groups.Select(p => new EnumeratedCohort(p.Key, p.Value.Valid.ToString(), p.Value.Sum.ToString(), p.Value.Mean(valid), p.Value.Support(), p.Value.Complete(allPaths), p.Value.Pair(valid))
                    { ParentExposure = p.Value.Parents(allPaths && _groupsComplete, options.Subject == "episode") }).ToArray(),
                ParentExposure = _overall.Parents(allPaths, options.Subject == "episode"),
                Pair = _overall.Pair(valid),
                Weighting = options.Weight is null ? "Unweighted occurrence law under the authored graph" : "Authored graph/proposal occurrence law; target likelihood-weighted law is not enumerated",
                KnownAssertionViolationsPerRound = options.Assertion == "zero" ? _violations.ToString() : null,
                AssertionStatus = options.Assertion != "zero" ? null : _invalid > Rational.Zero ? "invalid" : _violations > Rational.Zero ? "discrepancy" : !allPaths ? "unresolved" : _overall.Valid == Rational.Zero ? "insufficient" : "noViolationsInEnumeratedModel"
            };
        }
    }
}
