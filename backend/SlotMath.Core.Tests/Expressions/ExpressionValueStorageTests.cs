using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;

namespace SlotMath.Core.Tests.Expressions;

public class ExpressionValueStorageTests
{
    [Fact]
    public void IntegerAndTextBoundariesPreserveExactValuesAndKinds()
    {
        BigInteger[] boundaries = [long.MinValue, long.MinValue - BigInteger.One, -(BigInteger.One << 47) - 1,
            -(BigInteger.One << 47), -1, 0, 1, (BigInteger.One << 47) - 1, BigInteger.One << 47,
            long.MaxValue, long.MaxValue + BigInteger.One, ulong.MaxValue];
        foreach (var number in boundaries)
        {
            var value = ExprValue.Number(number);
            Assert.Equal(ExprType.Number, value.Kind);
            Assert.Equal(number, value.AsInteger());
            Assert.Equal(number, value.ToStateObject());
            Assert.Equal(ExprValue.Rational(number * 7, 7), value);
            Assert.Equal(ExprValue.Rational(number * 7, 7).GetHashCode(), value.GetHashCode());
            foreach (var text in new[] { number.ToString(), number.Sign >= 0 ? "+" + number : number.ToString() })
            {
                var stringValue = ExprValue.String(text);
                var symbol = ExprValue.Symbol(text);
                Assert.Equal(text, stringValue.StringValue);
                Assert.Equal(text, symbol.StringValue);
                Assert.Equal(ExprType.String, stringValue.Kind);
                Assert.Equal(ExprType.Symbol, symbol.Kind);
                Assert.Equal(number, CollectionFunctions.ToNumber(stringValue).AsInteger());
                Assert.Equal(number, CollectionFunctions.ToNumber(symbol).AsInteger());
            }
        }
        Assert.Equal(ExprValue.Number(0), default(ExprValue));
        Assert.NotEqual(ExprValue.Number(0), ExprValue.Null);
        Assert.False(ExprValue.Bool(false).BoolValue);
        Assert.True(ExprValue.Bool(true).BoolValue);
        Assert.NotEqual(ExprValue.Bool(false), ExprValue.Bool(true));
        Assert.Equal(ExprValue.Rational(-7, 13), ExprValue.Rational(14, -26));
        Assert.Null(ExprValue.String(null!).StringValue);
        Assert.Null(ExprValue.Array(null!).ArrayValue);
    }

    [Fact]
    public void InPlaceCellsReplaceRawStateAndNormalizeNestedSymbols()
    {
        var values = new[] { ExprValue.Number(long.MinValue), ExprValue.Rational(1, 3), ExprValue.Bool(false),
            ExprValue.Bool(true), ExprValue.String("text"), ExprValue.Symbol("symbol"), ExprValue.Null,
            ExprValue.Array([ExprValue.Null, ExprValue.Symbol("nested"), ExprValue.Number(ulong.MaxValue)]),
            ExprValue.Record(new Dictionary<string, ExprValue> { ["field"] = ExprValue.Symbol("record") }) };
        var cell = SamplingCell.FromRaw(new object()); // Deferred unsupported raw value.
        foreach (var value in values)
        {
            var expected = SamplingCell.Typed(value);
            cell.SetTyped(value);
            Assert.True(cell.Present);
            Assert.False(cell.HasRaw);
            Assert.Null(cell.Raw);
            Assert.Equal(expected.Value, cell.Value);
            Assert.Equal(expected.Readable, cell.Readable);
            Assert.False(cell.Value.ContainsSymbols);
        }
    }

    [Fact]
    public void CellExportsPreserveRawTypesDeferredValuesAndExactNumericKinds()
    {
        Assert.Equal(0, Assert.IsType<int>(SamplingCell.FromRaw(0).Export()));
        Assert.Equal(0L, Assert.IsType<long>(SamplingCell.FromRaw(0L).Export()));
        Assert.Equal(0, Assert.IsType<int>(SamplingCell.FromInt(0).Export()));
        foreach (var number in new BigInteger[] { 0, 1, -1, long.MinValue, long.MaxValue, ulong.MaxValue })
            Assert.Equal(number, Assert.IsType<BigInteger>(SamplingCell.Typed(ExprValue.Number(number)).Export()));
        Assert.Equal(ExprValue.Rational(-7, 13), Assert.IsType<ExprValue>(SamplingCell.Typed(ExprValue.Rational(-7, 13)).Export()));
        Assert.False(Assert.IsType<bool>(SamplingCell.Typed(ExprValue.Bool(false)).Export()));
        Assert.Equal("0", Assert.IsType<string>(SamplingCell.Typed(ExprValue.String("0")).Export()));
        Assert.Null(SamplingCell.Typed(ExprValue.Null).Export());

        var opaque = new object();
        var deferred = SamplingCell.FromRaw(opaque);
        Assert.Same(opaque, deferred.Export());
        deferred.SetTyped(ExprValue.Number(0));
        Assert.Equal(BigInteger.Zero, Assert.IsType<BigInteger>(deferred.Export()));
        Assert.False(deferred.HasRaw);
        Assert.Null(deferred.Raw);
    }
}
