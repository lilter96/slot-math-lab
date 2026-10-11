using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Math;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Games;

/// <summary>
///     The game client ported from tgslots is served from the traces the game graphs leave. For every game,
///     <c>scripts/verification/tgslots</c> drove the original game server through a few rounds and wrote down the
///     draws of each round and what the server answered (<c>*.client.golden.json</c>). Here those rounds are replayed
///     draw for draw through the graphs, and what each round paid and wrote to its state is kept in
///     <c>*.client.traces.json</c>. The frontend test <c>unit/game-server.spec.ts</c> lays those traces out as
///     server answers and compares them with the original's, so the two files together tie the client to the graphs.
///     Set <c>UPDATE_GAME_TRACES=1</c> to rewrite the trace files after a graph or a golden file has changed.
/// </summary>
public class GameClientTraceTests
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static TheoryData<string> Games =>
        ["ancient-dragon", "woodland-whisper", "le-militare", "x7-club", "nine-lives"];

    [Theory]
    [MemberData(nameof(Games))]
    public void RoundsOfTheOriginalServer_ReplayedThroughTheGraphs_LeaveTheTracesTheClientIsServedFrom(string game)
    {
        using JsonDocument golden = GameGraph.Golden($"{game}.client.golden.json");
        var graphs = new Dictionary<string, GameGraph>();
        var traces = new JsonArray();
        foreach (JsonElement round in golden.RootElement.GetProperty("rounds").EnumerateArray())
        {
            var file = round.GetProperty("graph").GetString()!;
            if (!graphs.TryGetValue(file, out GameGraph? graph))
            {
                graphs[file] = graph = GameGraph.Load(file);
            }

            (Rational win, Dict state) =
                graph.Replay(round.GetProperty("draws").EnumerateArray().Select(draw => draw.GetInt32()));
            traces.Add(new JsonObject { ["win"] = Number(win), ["trace"] = Written(graph, state) });
        }

        var name = $"{game}.client.traces.json";
        var replayed = traces.ToJsonString(Compact) + "\n";
        if (Environment.GetEnvironmentVariable("UPDATE_GAME_TRACES") == "1")
        {
            File.WriteAllText(Path.Combine(SourceData(), name), replayed);
        }
        else
        {
            Assert.True(replayed == File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Games", name)),
                $"{name} is not what the graph leaves for the rounds of {game}.client.golden.json; rewrite it with UPDATE_GAME_TRACES=1.");
        }
    }

    /// <summary>
    ///     What the round wrote: the fields of its final state that differ from the graph's initial state. The tables
    ///     of the game, which a round only reads, are left out; the frontend test puts the initial state back under
    ///     the trace, as the play endpoint returns both.
    /// </summary>
    private static JsonObject Written(GameGraph graph, Dict state)
    {
        var written = new JsonObject();
        foreach (var (key, value) in state.OrderBy(field => field.Key, StringComparer.Ordinal))
        {
            JsonNode? final = Canonical(value);
            JsonNode? initial = graph.Config.InitialState!.TryGetValue(key, out JsonElement start)
                ? Canonical(InitialStateValues.Materialize(start))
                : null;
            if (!JsonNode.DeepEquals(final, initial))
            {
                written[key] = final;
            }
        }

        return written;
    }

    /// <summary>
    ///     One form for a value whichever interpreter produced it: a number is the string of its canonical rational
    ///     (<c>"7"</c>, <c>"3/4"</c>), as the play endpoint writes whole numbers.
    /// </summary>
    private static JsonNode? Canonical(object? value) => value switch
    {
        null => null,
        bool flag => JsonValue.Create(flag),
        string text => JsonValue.Create(text),
        BigInteger integer => JsonValue.Create(integer.ToString()),
        int or long => JsonValue.Create(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture)),
        ExprValue { Kind: ExprType.Number } number => Number(new Rational(number.NumberNumerator,
            number.NumberDenominator)),
        ExprValue { Kind: ExprType.Boolean } flag => JsonValue.Create(flag.BoolValue),
        ExprValue { Kind: ExprType.String or ExprType.Symbol } text => JsonValue.Create(text.StringValue),
        ExprValue { Kind: ExprType.Array } array => new JsonArray(
            [.. array.ArrayValue!.Select(item => Canonical(item))]),
        ExprValue { Kind: ExprType.Record } record => new JsonObject(record.RecordValue!
            .OrderBy(field => field.Key, StringComparer.Ordinal)
            .Select(field => KeyValuePair.Create(field.Key, Canonical(field.Value)))),
        ExprValue { Kind: ExprType.Null } => null,
        IDictionary<string, object?> record => new JsonObject(record
            .OrderBy(field => field.Key, StringComparer.Ordinal)
            .Select(field => KeyValuePair.Create(field.Key, Canonical(field.Value)))),
        IEnumerable sequence => new JsonArray([.. sequence.Cast<object?>().Select(Canonical)]),
        _ => throw new InvalidOperationException(
            $"A round's state holds a {value.GetType().Name}, which has no canonical form.")
    };

    private static JsonNode Number(Rational value) =>
        JsonValue.Create(value.Denominator.IsOne ? value.Numerator.ToString() : value.ToString());

    /// <summary>The checked-in <c>TestData/Games</c> directory, found from where this file was compiled.</summary>
    private static string SourceData([CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", "TestData", "Games"));
}
