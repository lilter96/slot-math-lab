using System.Numerics;
using System.Text.Json;
using SlotMath.Api.Features.Configs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;

namespace SlotMath.Api.Features.Play;

public sealed record PlayRequest(JsonElement Config, long Seed = 42, long RoundIndex = 0, bool Trace = false);
public sealed record PlayResponse(double Win, string WinNumerator, string WinDenominator, long Seed,
    long RoundIndex, string ConfigHash, object State);

/// <summary>Generic graph execution. The server contains no game-specific payout, bonus or reel rules.</summary>
public static class PlayEndpoints
{
    public static void MapPlay(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/play/round", async (PlayRequest request, CompiledGraphCache compiledGraphs, HttpContext context) =>
        {
            const long maxSafeInteger = 9_007_199_254_740_991;
            if (request.Seed is < -maxSafeInteger or > maxSafeInteger || request.RoundIndex is < 0 or > maxSafeInteger)
                return Results.BadRequest(new { error = "Seed and round index must be safe integers." });
            try
            {
                var config = ConfigsEndpoints.DeserializeConfig(request.Config);
                var compiled = compiledGraphs.Compile(config);
                if (!compiled.IsValid) return Results.BadRequest(new { error = "Graph validation failed.", errors = compiled.Errors });
                var initial = new Dictionary<string, object?>();
                if (request.Trace && config.InitialState?.ContainsKey("traceEnabled") == true) initial["traceEnabled"] = true;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                var (win, state) = await Task.Run(() => SampledInterpreter.RunSingle(compiled.Program!, initial, request.Seed, request.RoundIndex, deadline.Token), context.RequestAborted);
                var amount = new Rational(win, compiled.WinScale);
                return Results.Ok(new PlayResponse(amount.ToDouble(), amount.Numerator.ToString(), amount.Denominator.ToString(),
                    request.Seed, request.RoundIndex, CanonicalHash.Compute(config), ExportState(state)!));
            }
            catch (OperationCanceledException) { return Results.Json(new { error = "Graph execution cancelled or exceeded 10 seconds." }, statusCode: 408); }
            catch (Exception ex) when (ex is JsonException or ExpressionEvaluationException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).Produces<PlayResponse>().RequireRateLimiting("compute");
    }

    private static object? ExportState(object? value) => value switch
    {
        null => null,
        BigInteger integer => integer.ToString(),
        ExprValue number when number.Kind == ExprType.Number => new { numerator = number.NumberNumerator.ToString(), denominator = number.NumberDenominator.ToString(), displayValue = new Rational(number.NumberNumerator, number.NumberDenominator).ToDouble() },
        IDictionary<string, object?> fields => fields.ToDictionary(pair => pair.Key, pair => ExportState(pair.Value)),
        System.Collections.IEnumerable sequence when value is not string => sequence.Cast<object?>().Select(ExportState).ToArray(),
        _ => value,
    };
}
