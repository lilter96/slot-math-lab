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
public sealed record EnumeratedMeasurement(string Id, string EligiblePerRound, string ValidPerRound, string ExcludedPerRound, string InvalidPerRound,
    string KnownSumPerRound, string? ConditionalMean, EnumeratedValue[] Support, bool SupportComplete, string? FirstError);
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
        var bindings = plans.Select(d => new MeasurementBinding<EvalContext>(Bind(d.Value), Bind(d.Filter), Bind(d.Options!.Group), Bind(d.Options.Pair), Bind(d.Options.Weight), Bind(d.Options.AwardId), Bind(d.Options.EntryFilter), Bind(d.Options.ExitFilter))).ToArray();
        var points = plans.SelectMany((d, i) => new[] { d.NodeId, d.Options!.EntryNodeId, d.Options.ExitNodeId }.OfType<string>().Distinct().Select(node => (node, i)))
            .GroupBy(p => p.node).ToDictionary(g => g.Key, g => g.Select(p => p.i).ToArray());
        var outputs = plans.Select(d => new Law(d.Id, d.Options!.SupportLimit)).ToArray();
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
                    completed++; retained += path.Mass; payoutSum += path.Mass * payout;
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
            "Exact branch probabilities and canonical integer/sub-credit round payouts. Measurement values use the production collector's IEEE-754 conversion and reductions; their probability masses are rational. Repeated observations retain expected exposure per paid round. Conditional means are withheld for cut paths, invalid subjects or incomplete value support. This checks compiled-IR behavior; use an independently authored specification as another oracle.") { MaximumKnownPayout = completed > 0 ? maximumKnown.ToString() : null, ReachableMaximum = unresolved == Rational.Zero ? maximumKnown.ToString() : null };
    }
    private sealed record Continuation(IFlatMapNode? Map, ISettlementNode? Settlement);
    private sealed record Trace(string Node, Dict State, Trace? Previous);
    private sealed record Path(object Current, Dict State, Rational Mass, List<Continuation> Stack, Trace? Trace, BigInteger? Raw);
    private sealed class UntypedPure(object value) : IPureNode { public object ValueUntyped => value; }
    private sealed class Law(string id, int limit)
    {
        private Rational _eligible = Rational.Zero, _valid = Rational.Zero, _excluded = Rational.Zero, _invalid = Rational.Zero, _sum = Rational.Zero;
        private readonly SortedDictionary<double, Rational> _support = new();
        private bool _complete = true; private string? _error;
        public void Add(MeasurementSnapshot snapshot, Rational mass)
        {
            _eligible += mass * new Rational(snapshot.Observations, BigInteger.One); _valid += mass * new Rational(snapshot.Count, BigInteger.One);
            _excluded += mass * new Rational(snapshot.Excluded, BigInteger.One); _invalid += mass * new Rational(snapshot.Errors, BigInteger.One); _error ??= snapshot.FirstError;
            if (snapshot.Sum is { } sum) { var value = new SettlementContext(sum, sum, 1).Read("payout"); _sum += mass * new Rational(value.NumberNumerator, value.NumberDenominator); }
            if (!snapshot.Analysis!.SupportComplete) { _complete = false; _support.Clear(); }
            if (_complete) foreach (var atom in snapshot.Analysis.Support)
            {
                if (!_support.ContainsKey(atom.Value) && _support.Count >= limit) { _complete = false; _support.Clear(); break; }
                _support[atom.Value] = _support.GetValueOrDefault(atom.Value, Rational.Zero) + mass * new Rational(atom.Count, BigInteger.One);
            }
        }
        public EnumeratedMeasurement Snapshot(bool allPaths) => new(id, _eligible.ToString(), _valid.ToString(), _excluded.ToString(), _invalid.ToString(), _sum.ToString(),
            allPaths && _complete && _invalid == Rational.Zero && _valid > Rational.Zero ? (_sum / _valid).ToString() : null,
            _support.Select(p => new EnumeratedValue(p.Key, p.Value.ToString())).ToArray(), allPaths && _complete, _error);
    }
}
