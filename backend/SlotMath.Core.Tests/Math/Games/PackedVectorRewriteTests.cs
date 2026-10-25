using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Math.Games;

[Collection("SamplingOptions")]
public sealed class PackedVectorRewriteTests
{
    [Theory]
    [InlineData(4, SamplingTier.Interpreted)]
    [InlineData(4, SamplingTier.Compiled)]
    [InlineData(4, SamplingTier.Tiered)]
    [InlineData(21, SamplingTier.Interpreted)]
    [InlineData(21, SamplingTier.Compiled)]
    [InlineData(21, SamplingTier.Tiered)]
    public void FiniteWordsMatchIndependentReadCountReplaceAndDecodeOracles(int length, SamplingTier tier)
    {
        using JsonDocument source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "Games", "authoring.packed-vector.json")));
        JsonElement contract = length == 4 ? source.RootElement : source.RootElement.GetProperty("wide");

        Expression Expr(string name)
        {
            return JsonSerializer.Deserialize<Expression>(contract.GetProperty(name).GetRawText(),
                JsonOptions.Default)!;
        }

        var layout = new Dictionary<string, int>
        {
            ["packed"] = 0,
            ["position"] = 1,
            ["replacement"] = 2,
            ["packedIndices"] = 3,
            ["packedPosition"] = 4
        };
        var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], new Dictionary<string, object?>
        {
            ["packed"] = 0,
            ["position"] = 0,
            ["replacement"] = 5,
            ["packedIndices"] = Enumerable.Range(0, length).ToArray()
        });
        var owner = new SamplingExpressions(name => layout[name], tier);
        Func<SamplingFrame, ExprValue> read = owner.Compile(Expr("read"));
        Func<SamplingFrame, ExprValue> count = owner.Compile(Expr("count"));
        Func<SamplingFrame, ExprValue> replace = owner.Compile(Expr("replace"));
        Func<SamplingFrame, ExprValue> decode = owner.Compile(Expr("decode"));
        frame.Reset(default);
        BigInteger maximum = BigInteger.Pow(8, length) - 1;
        IEnumerable<BigInteger> words = length == 4
            ? Enumerable.Range(0, 4096).Select(value => new BigInteger(value))
            : Enumerable.Range(0, length).SelectMany(cell => Enumerable.Range(0, 8)
                    .Select(value => new BigInteger(value) * BigInteger.Pow(8, cell)))
                .Concat([
                    maximum, maximum / 7 * 3, maximum / 7 * 5,
                    Enumerable.Range(0, length).Select(cell => new BigInteger(cell % 8) * BigInteger.Pow(8, cell))
                        .Aggregate(BigInteger.Zero, (a, b) => a + b)
                ]);
        foreach (BigInteger word in words)
        {
            BigInteger digits = word;
            var cells = new int[length];
            for (var cell = 0; cell < cells.Length; cell++)
            {
                cells[cell] = (int)(digits % 8);
                digits /= 8;
            }

            frame.Cells[0].SetTyped(ExprValue.Number(word));
            for (var cell = 0; cell < cells.Length; cell++)
            {
                frame.Cells[1].SetTyped(ExprValue.Number(cell));
                Assert.Equal(cells[cell], read(frame).AsInteger());
            }

            Assert.Equal(cells.Count(value => value == 3), count(frame).AsInteger());
            BigInteger expected = cells
                .Select((value, cell) => new BigInteger(value == 3 ? 5 : value) * BigInteger.Pow(8, cell))
                .Aggregate(BigInteger.Zero, (a, b) => a + b);
            Assert.Equal(expected, replace(frame).AsInteger());
            Assert.Equal(cells.Select(value => value.ToString()),
                decode(frame).ArrayValue!.Select(value => value.StringValue));
        }
    }
}
