using System.Numerics;
using SlotMath.Core.Math;
using static SlotMath.Api.Features.Evaluate.PayoutAnalysis;

namespace SlotMath.Api.Features.Evaluate;

/// <summary>An explicitly independent sequence of resets to the same graph initial state.</summary>
public sealed record IndependentRoundExposureRequest(long Rounds = 100, string? InitialReserve = null);

public sealed record ExposureTailOutput(
    string Probability,
    ExactNumberOutput LossQuantile,
    ExactNumberOutput LossExpectedShortfall);

public sealed record IndependentRoundExposureOutput
{
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public long Rounds { get; init; }
    public string? InitialReserve { get; init; }

    public string Assumptions { get; init; } =
        "Independent rounds, each reset to the same graph initial state, with the declared fixed stake. " +
        "Loss is settled payout minus received wagers. All horizon settlements continue through insolvency. " +
        "Ruin is the first post-settlement reserve strictly below zero; zero reserve is solvent. This is not a stop-on-ruin policy.";

    public ExactNumberOutput? MeanLoss { get; init; }
    public ExactNumberOutput? LossVariance { get; init; }
    public ExposureTailOutput[]? Tails { get; init; }
    public ExactNumberOutput? ProbabilityFinalLossExceedsReserve { get; init; }
    public string? RuinStatus { get; init; }
    public string? RuinReason { get; init; }
    public ExactNumberOutput? ProbabilityRuinByHorizon { get; init; }

    /// <summary>Conditional on ruin by this horizon; surviving subjects are censored.</summary>
    public ExactNumberOutput? ConditionalExpectedRuinRound { get; init; }
}

internal static class IndependentRoundExposureAnalysis
{
    public static bool IsValid(IndependentRoundExposureRequest? request) => request is null ||
                                                                            (request.Rounds is >= 0 and <= 1000000 &&
                                                                             (request.InitialReserve is null ||
                                                                              (TryRational(request.InitialReserve,
                                                                                   out Rational reserve) &&
                                                                               reserve.Sign >= 0)));

    public static IndependentRoundExposureOutput? Build(IndependentRoundExposureRequest? request,
        Dist<BigInteger>? distribution, BigInteger scale, Rational stake, string[] probabilities,
        CancellationToken cancellation)
    {
        if (request is null)
        {
            return null;
        }

        if (distribution is null)
        {
            return Unavailable(request,
                "A complete unpruned per-round payout law is required; moments cannot establish portfolio tails or ruin.");
        }

        Rational? reserve = request.InitialReserve is { } reserveText ? Rational.Parse(reserveText) : null;
        try
        {
            ExactExposureResult result = ExactOperatorExposure.Independent(distribution, scale, stake, request.Rounds,
                reserve, new ExactExposureOptions
                {
                    MaxWork = 8000000,
                    RuinWork = 2000000,
                    MaxAtoms = 16384,
                    MaximumBits = 16384,
                    MaxTime = TimeSpan.FromSeconds(5)
                }, cancellation);
            return new IndependentRoundExposureOutput
            {
                Status = "ExactLaw",
                Rounds = request.Rounds,
                InitialReserve = reserve?.ToString(),
                MeanLoss = Number(result.FinalLoss.Mean),
                LossVariance = Number(result.FinalLoss.Variance),
                Tails = probabilities.Select(text =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    Rational probability = Rational.Parse(text);
                    return new ExposureTailOutput(probability.ToString(),
                        Number(result.FinalLoss.Quantile(probability))!,
                        Number(result.FinalLoss.ExpectedShortfall(probability))!);
                }).ToArray(),
                ProbabilityFinalLossExceedsReserve =
                    reserve is { } capital ? Number(Rational.One - result.FinalLoss.Cdf(capital)) : null,
                RuinStatus = result.Ruin?.Status ?? "NotRequested",
                RuinReason = result.Ruin?.Reason,
                ProbabilityRuinByHorizon = Number(result.Ruin?.Probability),
                ConditionalExpectedRuinRound = Number(result.Ruin?.ConditionalExpectedRound)
            };
        }
        catch (ExactExposureLimitException exception)
        {
            cancellation.ThrowIfCancellationRequested();
            return Unavailable(request, exception.Message);
        }
    }

    private static IndependentRoundExposureOutput Unavailable(IndependentRoundExposureRequest request, string reason) =>
        new()
        {
            Status = "Unavailable",
            Reason = reason,
            Rounds = request.Rounds,
            InitialReserve = request.InitialReserve is { } text ? Rational.Parse(text).ToString() : null
        };
}
