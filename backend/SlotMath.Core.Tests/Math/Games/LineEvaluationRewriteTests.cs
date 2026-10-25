using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math.Games;

[Collection("SamplingOptions")]
public sealed class LineEvaluationRewriteTests
{
    [Theory]
    [InlineData("ancient-dragon.json", SamplingTier.Interpreted)]
    [InlineData("ancient-dragon.json", SamplingTier.Compiled)]
    [InlineData("ancient-dragon.json", SamplingTier.Tiered)]
    [InlineData("woodland-whisper.json", SamplingTier.Compiled)]
    [InlineData("x7-club.json", SamplingTier.Compiled)]
    public void EveryFourSymbolLineMatchesAnIndependentPrefixOracle(string file, SamplingTier tier)
    {
        var config = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestData", "Games", file)), JsonOptions.Default)!;
        var mapping = Assert.IsType<MapExpr>(config.Mechanics!["line-evaluation"].Expressions["evaluate"]);
        var wild = int.Parse(config.InitialState!["wildSymbol"].GetString()!, CultureInfo.InvariantCulture);
        var symbols = config.InitialState["linePays"].GetArrayLength();
        var width = config.InitialState["paylineCells"][0].GetArrayLength();
        int[] alphabet = [wild, .. Enumerable.Range(0, symbols).Where(symbol => symbol != wild).Take(3)];
        var layout = new Dictionary<string, int>
        {
            ["board"] = 0,
            ["line"] = 1,
            ["wildSymbol"] = 2
        };
        var packed = config.InitialState.ContainsKey("boardPacked");
        if (packed)
        {
            layout["boardPacked"] = 3;
        }

        var initial = new Dictionary<string, object?>
        {
            ["board"] = new string[width],
            ["line"] = Enumerable.Range(0, width).ToArray(),
            ["wildSymbol"] = wild.ToString(CultureInfo.InvariantCulture)
        };
        if (packed)
        {
            initial["boardPacked"] = BigInteger.Zero;
            var bits = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Log2(symbols)));
            initial["line"] = Enumerable.Range(0, width).Select(cell => -cell * bits).ToArray();
        }

        var owner = new SamplingExpressions(name => layout[name], tier);
        Func<SamplingFrame, ExprValue> evaluate = owner.Compile(mapping.Body);
        var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], initial);
        frame.Reset(default);
        var combinations = (int)System.Math.Pow(alphabet.Length, width);
        for (var code = 0; code < combinations; code++)
        {
            var digits = code;
            var line = new int[width];
            for (var column = 0; column < width; column++)
            {
                line[column] = alphabet[digits % alphabet.Length];
                digits /= alphabet.Length;
            }

            var expectedSymbol = line.FirstOrDefault(symbol => symbol != wild, wild);
            var expectedCount = line.TakeWhile(symbol => symbol == expectedSymbol || symbol == wild).Count();
            frame.Cells[0].SetTyped(ExprValue.Array(line.Select(symbol =>
                ExprValue.String(symbol.ToString(CultureInfo.InvariantCulture))).ToArray()));
            if (packed)
            {
                var bits = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Log2(symbols)));
                BigInteger word = line.Select((symbol, cell) => new BigInteger(symbol) << (cell * bits))
                    .Aggregate(BigInteger.Zero, (a, b) => a | b);
                frame.Cells[3].SetTyped(ExprValue.Number(word));
            }

            var expected = new BigInteger(expectedSymbol * (width + 1) + expectedCount);
            Assert.Equal(expected, evaluate(frame).AsInteger());
        }
    }

    [Fact]
    public void AStoppedPrefixDoesNotReadLaterNullCells()
    {
        var config = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "Games", "x7-club.json")), JsonOptions.Default)!;
        Expression expression = ((MapExpr)config.Mechanics!["line-evaluation"].Expressions["evaluate"]).Body;
        var wild = int.Parse(config.InitialState!["wildSymbol"].GetString()!, CultureInfo.InvariantCulture);
        var different = Enumerable.Range(0, config.InitialState["linePays"].GetArrayLength())
            .Where(symbol => symbol != wild).Take(2).ToArray();
        var width = config.InitialState["paylineCells"][0].GetArrayLength();
        var layout = new Dictionary<string, int>
        {
            ["board"] = 0,
            ["line"] = 1,
            ["wildSymbol"] = 2
        };
        var initial = new Dictionary<string, object?>
        {
            ["board"] = Enumerable.Range(0, width).Select(cell => cell < 2
                ? (object?)different[cell].ToString(CultureInfo.InvariantCulture)
                : null).ToArray(),
            ["line"] = Enumerable.Range(0, width).ToArray(),
            ["wildSymbol"] = wild.ToString(CultureInfo.InvariantCulture)
        };
        var frame = new SamplingFrame(layout, new SamplingCell[3], initial);
        frame.Reset(default);
        Func<SamplingFrame, ExprValue> evaluate =
            new SamplingExpressions(name => layout[name], SamplingTier.Compiled).Compile(expression);
        Assert.Equal(new BigInteger(different[0] * (width + 1) + 1), evaluate(frame).AsInteger());
    }
}
