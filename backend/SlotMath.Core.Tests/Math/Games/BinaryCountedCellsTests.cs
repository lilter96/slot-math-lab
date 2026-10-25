using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math.Games;

public sealed class BinaryCountedCellsTests
{
    [Theory]
    [InlineData("le-militare.super.json")]
    [InlineData("le-militare.super.small.json")]
    public void AuthoredMasksMatchIndependentCellWeightsForEveryArmedStateAndCellBasis(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Games", fixture);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        var columns = root.GetProperty("boardConfig").GetProperty("columns").GetInt32();
        var rows = root.GetProperty("boardConfig").GetProperty("rows").GetInt32();
        var cells = columns * rows;
        JsonElement initial = root.GetProperty("initialState");
        Assert.False(initial.TryGetProperty("cellCounts", out _));
        var exclusions = initial.GetProperty("nonCountingColumnCells").EnumerateArray()
            .Select(value => value.GetInt64()).ToArray();
        JsonElement mechanics = root.GetProperty("mechanics");
        var countsJson = mechanics.GetProperty("free-spin").GetProperty("expressions").GetProperty("counts")
            .GetRawText();
        var sizeJson = mechanics.GetProperty("pay-group").GetProperty("expressions").GetProperty("size")
            .GetRawText();
        var counts = JsonSerializer.Deserialize<Expression>(countsJson, JsonOptions.Default)!;
        var size = JsonSerializer.Deserialize<Expression>(sizeJson, JsonOptions.Default)!;
        BigInteger all = (BigInteger.One << cells) - 1;
        foreach (var bonus in new[] { -1, 0, 1, 2 })
        {
            for (var pattern = 0; pattern < 1 << columns; pattern++)
            {
                var armed = Enumerable.Range(0, columns).Select(column => (pattern >> column) & 1).ToArray();
                var weights = Enumerable.Range(0, cells).Select(cell =>
                    bonus == 1 && armed[cell / rows] == 1 && cell % rows != 0 ? 0 : 1).ToArray();
                var state = new Dictionary<string, object?>
                {
                    ["armed"] = armed,
                    ["bonus"] = bonus,
                    ["nonCountingColumnCells"] = exclusions
                };
                var context = new EvalContext
                {
                    State = state
                };
                BigInteger mask = ExactExpressionEvaluator.Evaluate(counts, context).AsInteger();
                BigInteger expectedMask = Enumerable.Range(0, cells).Aggregate(BigInteger.Zero,
                    (value, cell) => value + weights[cell] * (BigInteger.One << cell));
                Assert.Equal(expectedMask, mask);
                state["countedCells"] = mask;
                IEnumerable<BigInteger> groups = Enumerable.Range(0, cells).Select(cell => BigInteger.One << cell)
                    .Append(BigInteger.Zero).Append(all).Append(all / 3);
                foreach (BigInteger group in groups)
                {
                    state["group"] = group;
                    var expected = Enumerable.Range(0, cells).Where(cell => (group & (BigInteger.One << cell)) != 0)
                        .Sum(cell => weights[cell]);
                    Assert.Equal(new BigInteger(expected),
                        ExactExpressionEvaluator.Evaluate(size, context).AsInteger());
                }
            }
        }
    }
}
