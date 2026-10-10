using System.Numerics;
using SlotMath.Core.Math;
using SlotMath.Core.Random;

namespace SlotMath.Core.Measurements;
public sealed record ResourceImpactRequest(MarkovModelRequest Reference, int Horizon);
public sealed record ResourceImpactReport(string StopProbability, string RetainedReward, string? ReferenceReward,
    string? OmittedReward, string RetainedDuration, string ReferenceStatus, int Horizon, string Assumptions);
public sealed record SamplingAtom(string Id, string Value, string TargetProbability, string ProposalProbability, string Stratum = "all");
public sealed record StratumAllocation(string Id, int Samples);
public sealed record SamplingDesignRequest(SamplingAtom[] Atoms, string Mode = "importance", int Samples = 10000, long Seed = 42,
    string Threshold = "1", StratumAllocation[]? Allocation = null, double Confidence = .95, int ErrorFamilySize = 1);
public sealed record LikelihoodAtom(string Id, string Target, string Proposal, string Weight, long Observed) { public string? Stratum { get; init; } public string? ConditionalWeight { get; init; } }
public sealed record DesignEstimate(double Mean, double? StandardError, NumericInterval? Interval, string ExactReference);
public sealed record SamplingDesignReport(string Mode, int Samples, long Seed, bool TargetSupportCovered, LikelihoodAtom[] Likelihoods,
    DesignEstimate Reward, DesignEstimate TailProbability, string ExactEstimatorVariance, string ExactWeightSecondMoment,
    double? EffectiveSampleSize, string Assumptions);

/// <summary>Verified finite designs. The native sampler uses the supplied proposal itself;
/// user-supplied weight expressions cannot claim this verification contract.</summary>
public static partial class FiniteModelAnalysis
{
    public static ResourceImpactReport ResourceImpact(ResourceImpactRequest request, CancellationToken token = default)
    {
        if (request.Reference is null || request.Reference.Stationary || request.Horizon is < 1 or > 4096) throw new ArgumentException("Use an absorbing finite reference and a horizon of 1..4096.");
        var reference = Markov(request.Reference, token); var n = request.Reference.Rewards.Length;
        if ((long)n * n * request.Horizon > 1_000_000) throw new ArgumentException("Resource comparison exceeds one million transition operations.");
        var q = request.Reference.TransientMatrix.Select(row => row.Select(Parse).ToArray()).ToArray();
        var rewards = request.Reference.Rewards.Select(Parse).ToArray();
        var mass = Enumerable.Repeat(Rational.Zero, n).ToArray(); mass[request.Reference.InitialState] = Rational.One;
        var retained = Rational.Zero; var duration = Rational.Zero;
        for (var step = 0; step < request.Horizon; step++)
        {
            token.ThrowIfCancellationRequested(); var next = Enumerable.Repeat(Rational.Zero, n).ToArray();
            for (var i = 0; i < n; i++)
            {
                retained = Budget(retained + mass[i] * rewards[i]); duration = Budget(duration + mass[i]);
                for (var j = 0; j < n; j++) next[j] = Budget(next[j] + mass[i] * q[i][j]);
            }
            mass = next;
        }
        var alive = mass.Aggregate(Rational.Zero, (a, b) => Budget(a + b));
        return new(alive.ToString(), retained.ToString(), reference.ExpectedReward,
            reference.ExpectedReward is { } full ? Budget(Parse(full) - retained).ToString() : null,
            duration.ToString(), reference.Status, request.Horizon,
            "Exact supplied substochastic Markov reference: reward is paid before each transition. Stop probability is probability of requesting a step beyond the horizon. Omitted reward compares the same model with and without truncation. An unrelated graph, scheduler timeout or unknown uncapped model is not certified.");
    }

    public static SamplingDesignReport SamplingDesign(SamplingDesignRequest request, CancellationToken token = default)
    {
        if (request.Atoms is not { Length: > 0 and <= 128 } || request.Atoms.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 128 || string.IsNullOrWhiteSpace(x.Stratum) || x.Stratum.Length > 128)
            || request.Atoms.Select(x => x.Id).Distinct().Count() != request.Atoms.Length || request.Mode is not ("importance" or "stratified")
            || request.Samples is < 2 or > 1000000 || request.Seed is < -9007199254740991 or > 9007199254740991
            || !double.IsFinite(request.Confidence) || request.Confidence <= .5 || request.Confidence >= .999999 || request.ErrorFamilySize is < 1 or > 256)
            throw new ArgumentException("A native finite design requires 1..128 unique atoms, 2..1000000 samples, valid confidence/family and a safe seed.");
        var p = request.Atoms.Select(a => Parse(a.TargetProbability)).ToArray(); var q = request.Atoms.Select(a => Parse(a.ProposalProbability)).ToArray();
        var x = request.Atoms.Select(a => Parse(a.Value)).ToArray(); var threshold = Parse(request.Threshold);
        if (p.Any(v => v < Rational.Zero) || q.Any(v => v < Rational.Zero) || x.Any(v => v < Rational.Zero)
            || p.Aggregate(Rational.Zero, (a, b) => Budget(a + b)) != Rational.One || q.Aggregate(Rational.Zero, (a, b) => Budget(a + b)) != Rational.One
            || p.Where((v, i) => v > Rational.Zero && q[i] == Rational.Zero).Any())
            throw new ArgumentException("Target and proposal must be normalized exactly; every target-positive atom needs positive proposal support.");
        var w = p.Select((v, i) => q[i].IsZero ? Rational.Zero : Budget(v / q[i])).ToArray();
        var mean = Rational.Zero; var eventMean = Rational.Zero; var second = Rational.Zero; var weightSecond = Rational.Zero;
        for (var i = 0; i < p.Length; i++)
        { mean = Budget(mean + p[i] * x[i]); if (x[i] >= threshold) eventMean = Budget(eventMean + p[i]); second = Budget(second + q[i] * w[i] * w[i] * x[i] * x[i]); weightSecond = Budget(weightSecond + q[i] * w[i] * w[i]); }
        var observed = new long[p.Length]; var rng = new SeededRandom(request.Seed);
        double estimate = 0, eventEstimate = 0, variance = 0, eventVariance = 0, weightSum = 0, weightSquares = 0;
        var exactVariance = Rational.Zero;
        if (request.Mode == "importance")
        {
            if (request.Allocation is { Length: > 0 }) throw new ArgumentException("Importance designs do not use stratum allocations.");
            var sampler = new ExactCategorical(q); var rewards = new RunningMoments(); var events = new RunningMoments();
            for (var draw = 0; draw < request.Samples; draw++)
            {
                if ((draw & 255) == 0) token.ThrowIfCancellationRequested(); var i = sampler.Next(rng); observed[i]++;
                var weight = w[i].ToDouble(); var value = (w[i] * x[i]).ToDouble();
                if (!double.IsFinite(weight) || !double.IsFinite(value)) throw new ArithmeticException("Design exceeds finite report range.");
                rewards.Add(value, false); events.Add(x[i] >= threshold ? weight : 0, false); weightSum += weight; weightSquares += weight * weight;
            }
            estimate = rewards.Mean; eventEstimate = events.Mean;
            variance = rewards.M2 / (request.Samples - 1) / request.Samples; eventVariance = events.M2 / (request.Samples - 1) / request.Samples;
            exactVariance = Budget((second - mean * mean) / request.Samples);
        }
        else
        {
            var groups = request.Atoms.Select((a, i) => (a.Stratum, i)).GroupBy(a => a.Stratum).ToArray();
            if (groups.Length > 32 || request.Allocation is null || request.Allocation.Length != groups.Length
                || request.Allocation.Any(a => a is null || a.Samples < 2) || request.Allocation.Select(a => a.Id).Distinct().Count() != groups.Length
                || request.Allocation.Sum(a => (long)a.Samples) != request.Samples) throw new ArgumentException("Allocate at least two samples per stratum, up to 32 strata; allocations must sum to sample count.");
            foreach (var group in groups)
            {
                var n = request.Allocation.SingleOrDefault(a => a.Id == group.Key)?.Samples ?? throw new ArgumentException("Missing stratum allocation.");
                var indices = group.Select(a => a.i).ToArray(); var pm = indices.Aggregate(Rational.Zero, (a, i) => Budget(a + p[i])); var qm = indices.Aggregate(Rational.Zero, (a, i) => Budget(a + q[i]));
                if (pm <= Rational.Zero || qm <= Rational.Zero) throw new ArgumentException("Every allocated stratum requires positive target and proposal mass.");
                var conditional = indices.Select(i => Budget(q[i] / qm)).ToArray(); var sampler = new ExactCategorical(conditional);
                var rewards = new RunningMoments(); var events = new RunningMoments(); var stratumMean = Rational.Zero; var stratumSecond = Rational.Zero;
                foreach (var i in indices) { var cw = q[i].IsZero ? Rational.Zero : Budget((p[i] / pm) / (q[i] / qm)); stratumMean = Budget(stratumMean + p[i] / pm * x[i]); stratumSecond = Budget(stratumSecond + q[i] / qm * cw * cw * x[i] * x[i]); }
                for (var draw = 0; draw < n; draw++)
                {
                    if ((draw & 255) == 0) token.ThrowIfCancellationRequested(); var i = indices[sampler.Next(rng)]; observed[i]++;
                    var cw = q[i].IsZero ? Rational.Zero : Budget((p[i] / pm) / (q[i] / qm)); rewards.Add((cw * x[i]).ToDouble(), false); events.Add(x[i] >= threshold ? cw.ToDouble() : 0, false);
                }
                var mass = pm.ToDouble(); estimate += mass * rewards.Mean; eventEstimate += mass * events.Mean;
                variance += mass * mass * rewards.M2 / (n - 1) / n; eventVariance += mass * mass * events.M2 / (n - 1) / n;
                exactVariance = Budget(exactVariance + pm * pm * (stratumSecond - stratumMean * stratumMean) / n);
            }
        }
        var critical = StatisticalInference.NormalCritical((1 - request.Confidence) / request.ErrorFamilySize);
        DesignEstimate Estimate(double value, double v, Rational reference)
        {
            if (!double.IsFinite(value) || !double.IsFinite(v)) throw new ArithmeticException("Design estimate exceeds finite report range.");
            var se = System.Math.Sqrt(System.Math.Max(0, v));
            return new(value, se, v > 0 ? new(value - critical * se, value + critical * se, "Fixed-design normal approximation", "Independent proposal draws and prespecified support, allocation and hypothesis family. Zero empirical variance does not establish zero uncertainty.") : null, reference.ToString());
        }
        return new(request.Mode, request.Samples, request.Seed, true,
            request.Atoms.Select((a, i) => new LikelihoodAtom(a.Id, p[i].ToString(), q[i].ToString(), w[i].ToString(), observed[i]) { Stratum = request.Mode == "stratified" ? a.Stratum : null, ConditionalWeight = request.Mode == "stratified" ? Budget(w[i] * request.Atoms.Select((atom, index) => (atom, index)).Where(pair => pair.atom.Stratum == a.Stratum).Aggregate(Rational.Zero, (sum, pair) => Budget(sum + q[pair.index])) / request.Atoms.Select((atom, index) => (atom, index)).Where(pair => pair.atom.Stratum == a.Stratum).Aggregate(Rational.Zero, (sum, pair) => Budget(sum + p[pair.index]))).ToString() : null }).ToArray(),
            Estimate(estimate, variance, mean), Estimate(eventEstimate, eventVariance, eventMean), exactVariance.ToString(), weightSecond.ToString(),
            request.Mode == "importance" && weightSquares > 0 ? weightSum * weightSum / weightSquares : null,
            "Finite target/proposal support and likelihood ratios checked exactly; samples come from that proposal using integer rejection sampling. Stratified means use target stratum masses and fixed allocations, not pooled counts. This supplied-law experiment is separate from arbitrary graph Weight expressions. Report moments use binary64; exact reference and design variance use rationals. No optional stopping.");
    }
    private sealed class ExactCategorical
    {
        private readonly BigInteger[] _cumulative; private readonly BigInteger _total; private readonly int _bits;
        public ExactCategorical(Rational[] probabilities)
        {
            var denominator = BigInteger.One;
            foreach (var p in probabilities) { denominator = denominator / BigInteger.GreatestCommonDivisor(denominator, p.Denominator) * p.Denominator; if (denominator.GetBitLength() > 4096) throw new ArgumentException("Proposal denominator exceeds 4096 bits."); }
            _cumulative = new BigInteger[probabilities.Length]; var sum = BigInteger.Zero;
            for (var i = 0; i < probabilities.Length; i++) _cumulative[i] = sum += probabilities[i].Numerator * (denominator / probabilities[i].Denominator);
            _total = sum; _bits = checked((int)(_total - 1).GetBitLength());
        }
        public int Next(SeededRandom rng)
        {
            BigInteger value;
            do { value = BigInteger.Zero; for (var remaining = _bits; remaining > 0; remaining -= 64) { var width = System.Math.Min(64, remaining); var word = rng.NextUInt64(); if (width < 64) word &= (1UL << width) - 1; value = (value << width) + word; } } while (value >= _total);
            var low = 0; var high = _cumulative.Length - 1;
            while (low < high) { var mid = (low + high) / 2; if (value < _cumulative[mid]) high = mid; else low = mid + 1; }
            return low;
        }
    }
}
