using System.Numerics;
using SlotMath.Core.Math;

namespace SlotMath.Core.Measurements;

public sealed record RationalOutcome(string Value, string Probability);
public sealed record FiniteModelRequest(RationalOutcome[] Outcomes, string Cost = "1", string PrunedMass = "0", string? ProvenMaximum = null);
public sealed record RationalBounds(string Lower, string? Upper);
public sealed record FiniteModelReport(string Provenance, string RetainedMass, string PrunedMass, string Cost,
    RationalBounds Mean, RationalBounds Rtp, RationalBounds SecondMoment, RationalBounds Variance,
    RationalBounds HitProbability, string? HouseEdge, string MaximumKnownOutcome, string? ProvenMaximum,
    string? Skewness, string? ExcessKurtosis, string Assumptions)
{ public string? AuthoredInputSha256 { get; init; } public string? AlgorithmVersion { get; init; } public string? CoreBinarySha256 { get; init; } }
public sealed record MarkovModelRequest(string[][] TransientMatrix, string[] Rewards, int InitialState = 0, bool Stationary = false, string[]? Costs = null);
public sealed record MarkovModelReport(string Status, string[] ExpectedVisits, string? ExpectedDuration,
    string? ExpectedReward, int[] ReachableStates, string Detail)
{ public string? AuthoredInputSha256 { get; init; } public string? AlgorithmVersion { get; init; } public string? CoreBinarySha256 { get; init; }
  public string? AbsorptionProbability { get; init; } public string? DurationSecondMoment { get; init; } public string? DurationVariance { get; init; }
  public string[]? StationaryOccupancy { get; init; } public string? LongRunRewardPerStep { get; init; } public string? LongRunCostPerStep { get; init; } public string? LongRunReturn { get; init; } }

/// <summary>Independent finite references, using rational arithmetic throughout. These tools
/// analyze the supplied model; their assumptions never certify an unrelated compiled graph.</summary>
public static partial class FiniteModelAnalysis
{
    public static FiniteModelReport Distribution(FiniteModelRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Outcomes is not { Length: > 0 and <= 1024 }) throw new ArgumentException("A finite reference requires 1–1024 outcomes.");
        var cost = Parse(request.Cost); var pruned = Parse(request.PrunedMass); var cap = request.ProvenMaximum is null ? (Rational?)null : Parse(request.ProvenMaximum);
        if (cost <= Rational.Zero || pruned < Rational.Zero || pruned > Rational.One || cap < Rational.Zero) throw new ArgumentException("Cost must be positive; probability mass and payout bounds must be nonnegative.");
        Rational mass = Rational.Zero, first = Rational.Zero, second = Rational.Zero, third = Rational.Zero, fourth = Rational.Zero, hit = Rational.Zero, maximum = Rational.Zero;
        var support = new HashSet<Rational>();
        foreach (var atom in request.Outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (atom is null) throw new ArgumentException("Reference outcome entries cannot be null.");
            var x = Parse(atom.Value); var p = Parse(atom.Probability);
            if (x < Rational.Zero || p < Rational.Zero || p > Rational.One || cap is { } c && x > c || !support.Add(x)) throw new ArgumentException("Reference outcomes must be unique, nonnegative and within the authored bound; probabilities must be valid.");
            mass += p; first += p * x; second += p * x * x; third += p * x * x * x; fourth += p * x * x * x * x;
            Budget(mass); Budget(first); Budget(second); Budget(third); Budget(fourth);
            if (x > Rational.Zero) hit += p; if (p > Rational.Zero && x > maximum) maximum = x;
        }
        if (mass + pruned != Rational.One) throw new ArgumentException("Retained probability plus pruned mass must equal one exactly. Surviving mass is never renormalized.");
        var complete = pruned == Rational.Zero;
        Rational? firstUpper = complete ? first : cap is { } maximumBound ? first + pruned * maximumBound : null;
        Rational? secondUpper = complete ? second : cap is { } squaredBound ? second + pruned * squaredBound * squaredBound : null;
        var varianceLower = firstUpper is { } hi ? Max(Rational.Zero, second - hi * hi) : Rational.Zero;
        Rational? varianceUpper = secondUpper is { } secondHi ? Max(Rational.Zero, secondHi - first * first) : null;
        var exactVariance = second - first * first;
        string? skew = null, kurtosis = null;
        if (complete && exactVariance > Rational.Zero)
        {
            // Skewness generally needs an irrational square root. Supply the exact squared
            // skewness with the sign, avoiding a falsely rational "exact" display.
            var centralThird = third - new Rational(3, 1) * first * second + new Rational(2, 1) * first * first * first;
            var centralFourth = fourth - new Rational(4, 1) * first * third + new Rational(6, 1) * first * first * second - new Rational(3, 1) * first * first * first * first;
            skew = $"{(centralThird < Rational.Zero ? "-" : "")}sqrt({centralThird * centralThird / (exactVariance * exactVariance * exactVariance)})";
            kurtosis = (centralFourth / (exactVariance * exactVariance) - new Rational(3, 1)).ToString();
        }
        Rational? hitUpper = complete ? hit : Min(Rational.One, hit + pruned);
        return new(complete ? "Exact supplied finite model" : cap is null ? "Retained mass; unresolved upper bounds" : "Conservative interval under authored payout bound",
            mass.ToString(), pruned.ToString(), cost.ToString(), Bounds(first, firstUpper), Bounds(first / cost, firstUpper / cost), Bounds(second, secondUpper),
            complete ? Bounds(exactVariance, exactVariance) : Bounds(varianceLower, varianceUpper), Bounds(hit, hitUpper), complete ? (Rational.One - first / cost).ToString() : null,
            maximum.ToString(), cap?.ToString(), skew, kurtosis,
            "Nonnegative settled payouts. Supplied probability law, external mode cost and maximum must describe the same game. Pruned moments are unnormalized. A payout bound is an authored assumption, not a reachability proof.");
    }

    public static MarkovModelReport Markov(MarkovModelRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TransientMatrix is null || request.Rewards is null || request.TransientMatrix.Any(row => row is null)) throw new ArgumentException("State matrix and reward arrays cannot be null.");
        var n = request.TransientMatrix.Length;
        if (n is < 1 or > 32 || request.Rewards.Length != n || request.InitialState < 0 || request.InitialState >= n || request.TransientMatrix.Any(row => row.Length != n))
            throw new ArgumentException("Use a square matrix of 1–32 states, one reward per state and a valid initial state.");
        var q = request.TransientMatrix.Select(row => row.Select(Parse).ToArray()).ToArray(); var reward = request.Rewards.Select(Parse).ToArray();
        var exit = new bool[n];
        for (var i = 0; i < n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (q[i].Any(p => p < Rational.Zero || p > Rational.One) || reward[i] < Rational.Zero) throw new ArgumentException("Transition probabilities and state rewards must be nonnegative.");
            var sum = q[i].Aggregate(Rational.Zero, (a, b) => Budget(a + b)); if (sum > Rational.One) throw new ArgumentException("Transient rows must be substochastic (row sum at most one)."); exit[i] = sum < Rational.One;
        }
        if (request.Stationary)
        {
            if (request.Costs is { } authoredCosts && authoredCosts.Length != n) throw new ArgumentException("Supply one positive external cost per stationary state.");
            var costs = request.Costs?.Select(Parse).ToArray() ?? Enumerable.Repeat(Rational.One, n).ToArray();
            if (costs.Any(c => c <= Rational.Zero)) throw new ArgumentException("Stationary external costs must be positive.");
            return Stationary(q, reward, costs, request.InitialState, cancellationToken);
        }
        var reachable = new HashSet<int> { request.InitialState }; var frontier = new Queue<int>(reachable);
        while (frontier.TryDequeue(out var i)) for (var j = 0; j < n; j++) if (q[i][j] > Rational.Zero && reachable.Add(j)) frontier.Enqueue(j);
        var terminating = exit.ToArray(); bool changed;
        do { changed = false; for (var i = 0; i < n; i++) if (!terminating[i] && Enumerable.Range(0, n).Any(j => q[i][j] > Rational.Zero && terminating[j])) { terminating[i] = true; changed = true; } } while (changed);
        if (reachable.Any(i => !terminating[i])) return new("nonterminating", Enumerable.Repeat("undefined", n).ToArray(), null, null, reachable.Order().ToArray(), "A reachable closed transient class prevents almost-sure absorption. A loop budget cannot establish finite expected duration.") { AbsorptionProbability = Absorption(q, terminating, request.InitialState, cancellationToken).ToString() };
        var states = reachable.Order().ToArray(); var k = states.Length; var augmented = new Rational[k, 2 * k];
        for (var i = 0; i < k; i++) for (var j = 0; j < k; j++) { augmented[i, j] = (i == j ? Rational.One : Rational.Zero) - q[states[i]][states[j]]; augmented[i, k + j] = i == j ? Rational.One : Rational.Zero; }
        for (var col = 0; col < k; col++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pivot = col; while (pivot < k && augmented[pivot, col] == Rational.Zero) pivot++;
            if (pivot == k) throw new ArithmeticException("Transient fundamental matrix is singular.");
            if (pivot != col) for (var j = 0; j < 2 * k; j++) (augmented[pivot, j], augmented[col, j]) = (augmented[col, j], augmented[pivot, j]);
            var divisor = augmented[col, col]; for (var j = 0; j < 2 * k; j++) augmented[col, j] = Budget(augmented[col, j] / divisor);
            for (var i = 0; i < k; i++) if (i != col) { cancellationToken.ThrowIfCancellationRequested(); var factor = augmented[i, col]; for (var j = 0; j < 2 * k; j++) augmented[i, j] = Budget(augmented[i, j] - factor * augmented[col, j]); }
        }
        var initial = Array.IndexOf(states, request.InitialState); var visits = Enumerable.Repeat(Rational.Zero, n).ToArray(); Rational duration = Rational.Zero, totalReward = Rational.Zero;
        for (var j = 0; j < k; j++) { visits[states[j]] = augmented[initial, k + j]; duration = Budget(duration + visits[states[j]]); totalReward = Budget(totalReward + visits[states[j]] * reward[states[j]]); }
        var meanDurations = Enumerable.Range(0, k).Select(i => Enumerable.Range(0, k).Aggregate(Rational.Zero, (sum, j) => Budget(sum + augmented[i, k + j]))).ToArray();
        var secondDuration = new Rational(2, 1) * Enumerable.Range(0, k).Aggregate(Rational.Zero, (sum, j) => Budget(sum + augmented[initial, k + j] * meanDurations[j])) - duration;
        return new("absorbing", visits.Select(v => v.ToString()).ToArray(), duration.ToString(), totalReward.ToString(), states,
            "Exact fundamental-matrix calculation for the supplied finite state model. Rewards accrue once per transient visit; missing row mass absorbs. Unreachable states have zero visits.") { AbsorptionProbability = "1/1", DurationSecondMoment = secondDuration.ToString(), DurationVariance = (secondDuration - duration * duration).ToString() };
    }
    private static RationalBounds Bounds(Rational lower, Rational? upper) => new(lower.ToString(), upper?.ToString());
    private static Rational Min(Rational a, Rational b) => a < b ? a : b;
    private static Rational Max(Rational a, Rational b) => a > b ? a : b;
    private static Rational Budget(Rational value)
    {
        if (value.Numerator.GetBitLength() > 16384 || value.Denominator.GetBitLength() > 16384) throw new ArithmeticException("Reference calculation exceeds the 16384-bit exact-arithmetic budget. Simplify the model or its rational representation.");
        return value;
    }
    private static Rational Parse(string value)
    {
        if (value is null || value.Length is < 1 or > 80) throw new ArgumentException("Rationals must be short integer, decimal or numerator/denominator strings (maximum 80 characters).");
        Rational rational;
        var dot = value.IndexOf('.');
        if (dot >= 0 && !value.Contains('/'))
        {
            var digits = value.Remove(dot, 1); var places = value.Length - dot - 1;
            rational = new Rational(BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture), BigInteger.Pow(10, places));
        }
        else rational = Rational.Parse(value); if (rational.Numerator.GetBitLength() > 256 || rational.Denominator.GetBitLength() > 256) throw new ArgumentException("Rational input exceeds the 256-bit budget."); return rational;
    }
}
