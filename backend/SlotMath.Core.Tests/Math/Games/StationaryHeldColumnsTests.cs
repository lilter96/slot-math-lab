using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math.Games;

public sealed class StationaryHeldColumnsTests
{
    private static JsonDocument Read(string fixture) => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestData", "Games", fixture)));

    private static Expression Expression(JsonElement root, string scope, string name) =>
        JsonSerializer.Deserialize<Expression>(root.GetProperty("mechanics").GetProperty(scope)
            .GetProperty("expressions").GetProperty(name).GetRawText(), JsonOptions.Default)!;

    private static Dictionary<string, object?> Initial(JsonElement root) => root.GetProperty("initialState")
        .EnumerateObject().ToDictionary(property => property.Name,
            property => InitialStateValues.Materialize(property.Value));

    [Theory]
    [InlineData("le-militare.super.json")]
    [InlineData("le-militare.super.small.json")]
    public void AuthoredProtectionMatchesIndependentCellFlagsForEveryArmedPattern(string fixture)
    {
        using JsonDocument document = Read(fixture);
        JsonElement root = document.RootElement;
        var columns = root.GetProperty("boardConfig").GetProperty("columns").GetInt32();
        var rows = root.GetProperty("boardConfig").GetProperty("rows").GetInt32();
        var cells = columns * rows;
        BigInteger all = (BigInteger.One << cells) - 1;
        Expression protect = Expression(root, "combat-pays", "held");
        Dictionary<string, object?> state = Initial(root);
        var context = new EvalContext
        {
            State = state
        };
        BigInteger[] removals = Enumerable.Range(0, cells).Select(cell => BigInteger.One << cell)
            .Append(BigInteger.Zero).Append(all).Append(all / 3).Append(all / 5).ToArray();
        foreach (var bonus in new[] { -1, 0, 1, 2 })
        {
            for (var armed = 0; armed < 1 << columns; armed++)
            {
                var flags = Enumerable.Range(0, cells).Select(cell => bonus == 1
                    ? (armed >> (cell / rows)) & 1
                    : (cell * 7 + armed) % 3 == 0
                        ? 1
                        : 0).ToArray();
                BigInteger counted = 0;
                for (var cell = 0; cell < cells; cell++)
                {
                    if (bonus != 1 || flags[cell] == 0 || cell % rows == 0)
                    {
                        counted |= BigInteger.One << cell;
                    }
                }

                state["bonus"] = bonus;
                state["held"] = flags;
                state["countedCells"] = counted;
                foreach (BigInteger removed in removals)
                {
                    BigInteger expected = 0;
                    for (var cell = 0; cell < cells; cell++)
                    {
                        if (flags[cell] == 0 && (removed & (BigInteger.One << cell)) != 0)
                        {
                            expected |= BigInteger.One << cell;
                        }
                    }

                    state["removed"] = removed;
                    Assert.Equal(expected, ExactExpressionEvaluator.Evaluate(protect, context).AsInteger());
                }
            }
        }
    }

    [Theory]
    [InlineData("le-militare.super.json")]
    [InlineData("le-militare.super.small.json")]
    public void OrdinaryGravityLeavesWholeProtectedColumnsUnchangedForEveryPermittedColumnRemoval(string fixture)
    {
        using JsonDocument document = Read(fixture);
        JsonElement root = document.RootElement;
        var columns = root.GetProperty("boardConfig").GetProperty("columns").GetInt32();
        var rows = root.GetProperty("boardConfig").GetProperty("rows").GetInt32();
        // The paid composition still contains the general carried-array operation.
        Expression settle = Expression(root, "paid-tumble", "settle-held");
        Dictionary<string, object?> state = Initial(root);
        var context = new EvalContext
        {
            State = state
        };
        for (var armed = 0; armed < 1 << columns; armed++)
        {
            var flags = Enumerable.Range(0, columns * rows).Select(cell => (armed >> (cell / rows)) & 1).ToArray();
            state["held"] = flags;
            for (var column = 0; column < columns; column++)
            {
                for (var removed = 0; removed < 1 << rows; removed++)
                {
                    // Protected cells cannot be removed; an unprotected column has every possible pattern.
                    if (((armed >> column) & 1) != 0 && removed != 0)
                    {
                        continue;
                    }

                    var patterns = new int[columns];
                    patterns[column] = removed;
                    state["columnRemoved"] = patterns;
                    ExprValue actual = ExactExpressionEvaluator.Evaluate(settle, context);
                    Assert.Equal(flags, actual.ArrayValue!.Select(value => (int)value.AsInteger()));
                }
            }
        }

        // The equivalent identity is omitted from the authored free-spin composition.
        Assert.DoesNotContain(root.GetProperty("mechanics").GetProperty("free-tumble").GetProperty("nodes")
                .EnumerateArray(),
            node => node.TryGetProperty("outputKey", out JsonElement key) && key.GetString() == "held");
    }
}
