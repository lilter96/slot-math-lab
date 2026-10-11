using System.Diagnostics;
using System.Numerics;
using System.Runtime.ExceptionServices;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Math.Structured;

// ═══════════════════════════════════════════════════════════════════════════
//  StructuredExactEvaluator — exact results for games too large to enumerate
//
//  The classic exact interpreter walks every outcome of every draw; a game
//  with five 80-stop reels and a feature of 27 dependent spins has more
//  outcomes than can be visited. This evaluator computes the same exact
//  rational expectation from the same compiled graph by symbolic execution
//  (SymbolicExecutor) and structured inference (Inference).
//
//  It never approximates. A graph it cannot model exactly, a statement that
//  can fail on a reachable outcome, or a budget overrun is reported as
//  Declined, and the caller keeps whatever it had before.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>What the structured evaluator needs from a compilation: the resolved graph and its initial state.</summary>
internal sealed record StructuredModel(
    GraphConfig Config,
    IReadOnlyDictionary<string, object?> InitialValues,
    BigInteger Cap);

public sealed record StructuredExactOptions
{
    /// <summary>Deterministic work limit (terms built, law entries, substitutions).</summary>
    public long MaxWork { get; init; } = 400_000_000;

    /// <summary>Extra work allowed for the full payout distribution once the expectation is known.</summary>
    public long DistributionWork { get; init; } = 4_000_000;

    /// <summary>Extra work allowed for the exact hit frequency when the distribution was not built.</summary>
    public long HitFrequencyWork { get; init; } = 40_000_000;

    /// <summary>Extra work allowed for the exact second moment when the distribution was not built.</summary>
    public long VarianceWork { get; init; } = 4_000_000;

    /// <summary>
    ///     Loops whose iteration cap is above this are solved as Markov reward chains instead of being unrolled.
    ///     Unrolling keeps a state that factors over independent parts factored, which a chain cannot, so short
    ///     loops are better unrolled.
    /// </summary>
    public int ChainLoopsAbove { get; init; } = 32;

    /// <summary>
    ///     Work allowed for solving one loop with the amounts it accumulates kept in the chain state, which gives
    ///     their full law; beyond it the amounts are carried by their moments.
    /// </summary>
    public long LoopLawWork { get; init; } = 2_000_000;

    /// <summary>
    ///     A loop solved as a chain is cut short of its cap once the probability that it is still running is below
    ///     this and solving it exactly has become long: its numbers have grown past
    ///     <see cref="PruneLoopsAboveBits" /> or one run has taken more than <see cref="PruneLoopsAboveWork" />. The
    ///     result is then an enclosure of width at most twice the pruned mass times the declared win cap (D5), not
    ///     one rational. Null: never.
    /// </summary>
    public Rational? PruneLoopMassBelow { get; init; } = new Rational(BigInteger.One, BigInteger.Pow(10, 30));

    /// <summary>The length of a chain's exact numbers, in bits, up to which no loop is cut short.</summary>
    public int PruneLoopsAboveBits { get; init; } = 16_384;

    /// <summary>The work one run of a chain may take before the loop may be cut short.</summary>
    public long PruneLoopsAboveWork { get; init; } = 64_000_000;

    /// <summary>Optional wall-clock guard on top of the work limit.</summary>
    public TimeSpan? MaxTime { get; init; }

    internal StructuredFault Fault { get; init; }

    internal LoopMode Loops { get; init; } = LoopMode.Auto;

    /// <summary>Derive the second moment from the graph's structure even when the distribution gives it.</summary>
    internal bool AlwaysStructuralSecondMoment { get; init; }

    /// <summary>
    ///     Derive the hit frequency from the graph's structure even when the distribution gives it, so that tests can
    ///     compare the two.
    /// </summary>
    internal bool AlwaysStructuralHitFrequency { get; init; }
}

/// <summary>
///     How one loop of the graph was evaluated. <see cref="ExpectedCapHits" /> is the exact expected number of times
///     per round the loop is cut off by its iteration cap rather than ended by its stop condition; where the loop
///     was pruned (<see cref="PrunedMass" /> above zero) it counts the pruned executions too, which the cap might
///     not have reached.
/// </summary>
public sealed record StructuredLoopResult(
    string LoopId,
    string Evaluation,
    int IterationCap,
    int Runs,
    int States,
    Rational? ExpectedCapHits,
    Rational PrunedMass);

public sealed record StructuredExactResult
{
    /// <summary>False when the evaluator declined; <see cref="DeclinedReason" /> says why.</summary>
    public bool Succeeded { get; init; }

    public string? DeclinedReason { get; init; }

    /// <summary>E[round payout] in sub-credit units (credits × win scale), after the win cap.</summary>
    public Rational ExpectedWin { get; init; }

    /// <summary>The exact payout distribution in sub-credit units, when it was small enough to build.</summary>
    public Dist<BigInteger>? Distribution { get; init; }

    /// <summary>
    ///     P(round payout &gt; 0), exact: from the distribution, or from the graph's structure when that was too large to
    ///     build.
    /// </summary>
    public Rational? HitFrequency { get; init; }

    /// <summary>The hit frequency as derived without the distribution, when that derivation ran.</summary>
    internal Rational? StructuralHitFrequency { get; init; }

    /// <summary>
    ///     E[round payout²] in squared sub-credit units, exact: from the distribution, or from the graph's structure
    ///     (bilinearity over the parts of the payout) when the distribution is not available.
    /// </summary>
    public Rational? SecondMoment { get; init; }

    internal Rational? StructuralSecondMoment { get; init; }

    public IReadOnlyList<StructuredLoopResult> Loops { get; init; } = [];

    /// <summary>Why loops were not summarized the way that was tried first, over every attempt.</summary>
    internal IReadOnlyList<string> LoopNotes { get; init; } = [];

    /// <summary>
    ///     An upper bound on the probability of the rounds in which a loop was cut short of its cap (D5). Zero: every
    ///     figure is exact. Above zero: every figure is that of the game with those rounds ended early, and the
    ///     game's own figure is enclosed: the expectation within this mass times the win cap, a probability within
    ///     this mass.
    /// </summary>
    public Rational PrunedMass { get; init; } = Rational.Zero;

    /// <summary>The declared round win cap in sub-credit units.</summary>
    public BigInteger WinCap { get; init; }

    /// <summary>Proven bounds of the payout in sub-credit units.</summary>
    public BigInteger? MinWin { get; init; }

    public BigInteger? MaxWin { get; init; }

    public long RandomVariables { get; init; }
    public long Terms { get; init; }
    public long Laws { get; init; }
    public long Work { get; init; }
    public TimeSpan Elapsed { get; init; }

    /// <summary>The exact variance of the payout in squared credits per unit stake, when the second moment is known.</summary>
    public Rational? Variance(BigInteger winScale) => SecondMoment is { } second
        ? (second - ExpectedWin * ExpectedWin) / new Rational(winScale * winScale, 1)
        : null;

    /// <summary>The exact RTP: E[payout] in credits per unit stake.</summary>
    public RtpMetric Rtp(BigInteger winScale)
    {
        Rational rtp = ExpectedWin / new Rational(winScale, 1);
        if (PrunedMass.IsZero)
        {
            return new RtpMetric(ProvenanceTag.Exact)
            {
                RationalNumerator = rtp.Numerator,
                RationalDenominator = rtp.Denominator,
                DisplayValue = rtp.ToDouble()
            };
        }

        (Rational low, Rational high) = RtpInterval(winScale);
        var (lo, hi) = (System.Math.BitDecrement(low.ToDouble()), System.Math.BitIncrement(high.ToDouble()));
        var mass = PrunedMass.ToDouble();
        return new RtpMetric(ProvenanceTag.Interval(lo, hi, mass, BoundSource.DeclaredWinCap))
        {
            RationalNumerator = rtp.Numerator,
            RationalDenominator = rtp.Denominator,
            DisplayValue = rtp.ToDouble(),
            LoDisplay = lo,
            HiDisplay = hi,
            PrunedMass = mass
        };
    }

    /// <summary>
    ///     The exact enclosure of the RTP. A pruned round pays between nothing and the win cap, in the game and in
    ///     the game with that round ended early alike, so the two expectations differ by at most the pruned mass
    ///     times the cap.
    /// </summary>
    public (Rational Low, Rational High) RtpInterval(BigInteger winScale)
    {
        var unit = new Rational(winScale, 1);
        Rational rtp = ExpectedWin / unit;
        Rational slack = PrunedMass * new Rational(WinCap, 1) / unit;
        Rational low = rtp - slack;
        return (low < Rational.Zero ? Rational.Zero : low, rtp + slack);
    }
}

public static class StructuredExactEvaluator
{
    // Terms nest as deep as the graph's longest dependency chain, and the recursions over them use the stack.
    private const int StackBytes = 256 * 1024 * 1024;

    public static StructuredExactResult Evaluate(CompileResult compiled, StructuredExactOptions? options = null,
        CancellationToken cancellation = default)
    {
        if (compiled.Structured is not { } model)
        {
            return Declined("the compilation carries no graph", default);
        }

        options ??= new StructuredExactOptions();
        StructuredExactResult? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = Run(model, compiled.WinScale, options, cancellation);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }, StackBytes)
        {
            IsBackground = true,
            Name = "structured-exact"
        };
        thread.Start();
        thread.Join();
        if (failure is OperationCanceledException)
        {
            cancellation.ThrowIfCancellationRequested();
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result!;
    }

    private static StructuredExactResult Declined(string reason, TimeSpan elapsed) =>
        new()
        {
            DeclinedReason = reason,
            Elapsed = elapsed
        };

    private static StructuredExactResult Run(StructuredModel model, BigInteger winScale, StructuredExactOptions options,
        CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        var budget = new WorkBudget(options.MaxWork, options.MaxTime, cancellation);

        // Summarizing a loop by moments gives up the law of what it accumulates, and summarizing it at all gives up
        // the fields it leaves behind. A graph that needs either is evaluated again without that step; each attempt
        // is exact or declines, so the first that succeeds is the answer.
        LoopMode[] modes = options.Loops == LoopMode.Auto
            ? [LoopMode.Auto, LoopMode.Law, LoopMode.Unroll]
            : [options.Loops];
        StructuredExactResult result = Declined("no evaluation ran", clock.Elapsed);
        var notes = new List<string>();
        var skipLaw = false;
        foreach (LoopMode mode in modes)
        {
            if (mode == LoopMode.Law && skipLaw)
            {
                continue;
            }

            result = Attempt(model, winScale, options, mode, budget, clock, notes, out var summarized,
                out var byMoments);
            if (result.Succeeded || !summarized || budget.Used >= budget.Limit)
            {
                break;
            }

            notes.Add($"{mode}: {result.DeclinedReason}");
            skipLaw = !byMoments;
        }

        return result with
        {
            LoopNotes = notes
        };
    }

    private static StructuredExactResult Attempt(StructuredModel model, BigInteger winScale,
        StructuredExactOptions options, LoopMode mode, WorkBudget budget, Stopwatch clock, List<string> notes,
        out bool summarized, out bool byMoments)
    {
        var factory = new TermFactory(budget)
        {
            Fault = options.Fault
        };
        var inference = new Inference(factory);
        var executor = new SymbolicExecutor(model.Config, winScale, factory, inference,
            new LoopOptions(mode, options.ChainLoopsAbove, options.LoopLawWork, options.PruneLoopMassBelow,
                options.PruneLoopsAboveBits, options.PruneLoopsAboveWork, options.VarianceWork,
                options.HitFrequencyWork));
        BigInteger cap = options.Fault == StructuredFault.CapIgnored ? model.Cap * 1_000_000 : model.Cap;
        var limit = budget.Limit;
        try
        {
            Term payout = executor.Run(model.InitialValues);

            // No statement may fail on an outcome of positive probability: the reference would abort the round there.
            var settled = new HashSet<(int, int)>();
            foreach (Obligation obligation in executor.Obligations)
            {
                if (options.Fault == StructuredFault.ErrorsIgnored)
                {
                    break;
                }

                if (!obligation.Result.CanErr || !settled.Add((obligation.Guard.Id, obligation.Result.Id)))
                {
                    continue;
                }

                if (inference.LawOf(obligation.Result).ErrMass.IsZero)
                {
                    continue;
                }

                Term guarded = factory.If(obligation.Guard, factory.Tuple([obligation.Result]), factory.NullT);
                if (!inference.LawOf(guarded).Values.All(Sound))
                {
                    return Declined($"{obligation.Where} can fail on a reachable outcome", clock.Elapsed);
                }
            }

            // The payout must be a whole non-negative number of sub-credit units; the cap then applies as min(payout, cap).
            Inference.Bound? bound = inference.BoundOf(payout);
            var proven = bound is { } b && b.Step.Denominator.IsOne && ExprValue.CompareNumbers(b.Lo, V.Zero) >= 0;
            var capInactive = proven && ExprValue.CompareNumbers(bound!.Value.Hi, ExprValue.Number(cap)) <= 0;

            Rational? expected = capInactive ? inference.Expect(payout) : null;
            Dist<BigInteger>? distribution = null;
            if (capInactive)
            {
                budget.Limit = System.Math.Min(limit, budget.Used + options.DistributionWork);
            }

            try
            {
                Law law = inference.LawOf(payout);
                var builder = new DistBuilder<BigInteger>();
                for (var i = 0; i < law.Values.Length; i++)
                {
                    ExprValue value = law.Values[i];
                    if (V.IsErr(value))
                    {
                        return Declined("the payout can fail on a reachable outcome", clock.Elapsed);
                    }

                    if (value.Kind != ExprType.Number || !value.NumberDenominator.IsOne)
                    {
                        return Declined("the payout exceeds the declared paytable precision", clock.Elapsed);
                    }

                    if (value.NumberNumerator.Sign < 0)
                    {
                        return Declined("the payout can be negative", clock.Elapsed);
                    }

                    builder.Add(BigInteger.Min(value.NumberNumerator, cap), law.Mass[i], law.Total);
                }

                distribution = builder.Build();
                (BigInteger numerator, BigInteger denominator) = distribution.ExpectedBigIntegerValue();
                var fromLaw = new Rational(numerator, denominator);
                if (expected is { } byLinearity && byLinearity != fromLaw)
                {
                    throw new StructuredInconsistencyException(
                        "Structured exact evaluation is inconsistent: the expectation by linearity differs from the distribution's mean.");
                }

                expected = fromLaw;
            }
            catch (Exception exception) when (expected is not null && exception is StructuredBudgetException
                                                  or StructuredLawUnavailableException)
            {
                // The expectation stands on its own; the full distribution is simply not available.
            }
            finally
            {
                budget.Limit = limit;
            }

            Rational? hits = null;
            Rational? second = null;
            if (distribution is not null)
            {
                BigInteger positive = BigInteger.Zero;
                BigInteger squares = BigInteger.Zero;
                foreach (Dist<BigInteger>.Entry entry in distribution.Entries)
                {
                    squares += entry.Value * entry.Value * entry.Numerator;
                    if (entry.Value.Sign > 0)
                    {
                        positive += entry.Numerator;
                    }
                }

                hits = new Rational(positive, distribution.Denominator);
                second = new Rational(squares, distribution.Denominator);
            }

            // With the cap inactive the payout is the uncapped amount, already shown to fail on no reachable outcome,
            // and what the distribution would give can be derived from the structure of the amount instead.
            Rational? structuralHits = null;
            if (capInactive && (distribution is null || options.AlwaysStructuralHitFrequency))
            {
                structuralHits = Optional(budget, options.HitFrequencyWork,
                    () => inference.Probability(inference.Positive(payout)));
            }

            Rational? structuralSecond = null;
            if (capInactive && (distribution is null || options.AlwaysStructuralSecondMoment))
            {
                structuralSecond = Optional(budget, options.VarianceWork, () => inference.SecondMoment(payout));
                if (second is { } fromLaw && structuralSecond is { } byBilinearity && fromLaw != byBilinearity)
                {
                    throw new StructuredInconsistencyException(
                        "Structured exact evaluation is inconsistent: the second moment by bilinearity differs from the distribution's.");
                }
            }

            var loops = new List<StructuredLoopResult>();
            Rational pruned = Rational.Zero;
            foreach (IGrouping<string, LoopRun> runs in executor.Loops.GroupBy(run => run.LoopId))
            {
                LoopRun first = runs.First();
                Rational? capHits = null;
                if (first.Mode != "unrolled")
                {
                    capHits = Optional(budget, options.LoopLawWork, () =>
                    {
                        Rational sum = Rational.Zero;
                        Term atCap = factory.Const(ExprValue.String("modelLimit"));
                        foreach (LoopRun run in runs)
                        {
                            sum += inference.Probability(factory.If(run.Guard,
                                factory.Cmp(CompareOp.Eq, run.Reason, atCap), factory.FalseT));
                        }

                        return sum;
                    });
                }

                Rational cut = runs.Aggregate(Rational.Zero, (sum, run) => sum + run.Pruned);
                pruned += cut;
                loops.Add(new StructuredLoopResult(first.LoopId, first.Mode, first.Cap, runs.Count(),
                    runs.Max(run => run.States), capHits, cut));
            }

            return new StructuredExactResult
            {
                Succeeded = true,
                ExpectedWin = expected!.Value,
                Distribution = distribution,
                HitFrequency = hits ?? structuralHits,
                StructuralHitFrequency = structuralHits,
                SecondMoment = second ?? structuralSecond,
                StructuralSecondMoment = structuralSecond,
                Loops = loops,
                PrunedMass = pruned,
                WinCap = cap,
                MinWin = proven ? bound!.Value.Lo.NumberNumerator : null,
                MaxWin = proven ? BigInteger.Min(bound!.Value.Hi.NumberNumerator, cap) : null,
                RandomVariables = factory.Rvs.Count(v => !v.Lumped),
                Terms = factory.TermCount,
                Laws = inference.Stats.Laws,
                Work = budget.Used,
                Elapsed = clock.Elapsed
            };
        }
        catch (StructuredDeclinedException declined)
        {
            return Declined(declined.Message, clock.Elapsed);
        }
        catch (StructuredBudgetException exceeded)
        {
            return Declined(exceeded.Message, clock.Elapsed);
        }
        catch (InsufficientExecutionStackException)
        {
            return Declined("the graph's dependency chain is too deep", clock.Elapsed);
        }
        finally
        {
            budget.Limit = limit;
            notes.AddRange(executor.Notes);
            summarized = executor.Loops.Any(run => run.Mode != "unrolled");
            byMoments = executor.Loops.Any(run => run.Mode == "chain-moments");
        }
    }

    /// <summary>A metric the result does not depend on: computed within its own allowance, or left out.</summary>
    private static Rational? Optional(WorkBudget budget, long allowance, Func<Rational> metric)
    {
        var limit = budget.Limit;
        budget.Limit = System.Math.Min(limit, budget.Used + allowance);
        try
        {
            return metric();
        }
        catch (Exception exception) when (exception is StructuredBudgetException or StructuredDeclinedException
                                              or InsufficientExecutionStackException)
        {
            return budget.Used > limit ? throw new StructuredBudgetException("work") : null;
        }
        finally
        {
            budget.Limit = limit;
        }
    }

    /// <summary>A guarded statement result: the statement did not run (null) or ran without failing.</summary>
    private static bool Sound(ExprValue value) => value.Kind == ExprType.Null ||
                                                  (value.Kind == ExprType.Array && !V.IsErr(value.ArrayValue![0]));
}
