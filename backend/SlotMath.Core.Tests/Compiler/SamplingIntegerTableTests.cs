using System.Collections;
using System.Globalization;
using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class SamplingIntegerTableTests
{
    [Fact]
    public void InvalidPositionsFailBeforeAnOpaqueArrayCountIsRead()
    {
        var probe = new CountProbe();
        ExprValue array = ExprValue.Array(probe);
        probe.ThrowOnCount = true;
        Assert.Equal("EVAL_TYPE_ERROR", Assert.Throws<ExpressionEvaluationException>(() =>
            CollectionFunctions.Index(array, ExprValue.Bool(false))).Code);
        Assert.Equal("EVAL_INVALID_INDEX", Assert.Throws<ExpressionEvaluationException>(() =>
            CollectionFunctions.Index(array, ExprValue.Rational(1, 2))).Code);
        Assert.Throws<InvalidOperationException>(() => CollectionFunctions.Index(array, ExprValue.Number(0)));
    }

    private static ConstantExpr N(long value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString(CultureInfo.InvariantCulture)
    };

    private static FieldAccessExpr S(string name) => new()
    {
        Target = "state",
        Path = [name]
    };

    private static CallExpr Index(Expression position) => new()
    {
        Function = "index",
        Args = [S("table"), position]
    };

    private static BinaryExpr B(BinaryOp op, Expression left, Expression right) => new()
    {
        Op = op,
        Left = left,
        Right = right
    };

    private static MapExpr Map(Expression body) => new()
    {
        StateKey = "items",
        ItemName = "item",
        IndexName = "position",
        ItemType = ExprType.Number,
        Body = body
    };

    private static ExprValue Array(params long[] values) => ExprValue.Array(values.Select(ExprValue.Number).ToArray());

    private static (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots,
        Func<SamplingFrame, ExprValue> Run) Bind(Expression expression, SamplingTier tier, long[] table,
            int items = 66, bool constant = true, SamplingShapes? shapes = null)
    {
        var slots = new Dictionary<string, int>();

        int Slot(string name)
        {
            if (!slots.TryGetValue(name, out var index))
            {
                slots[name] = index = slots.Count;
            }

            return index;
        }

        var initial = new Dictionary<string, object?>
        {
            ["items"] = Enumerable.Range(0, items).ToArray(),
            ["table"] = table,
            ["flag"] = false,
            ["position"] = 999
        };
        foreach (var name in initial.Keys)
        {
            Slot(name);
        }

        var constants = new Dictionary<string, ExprValue>();
        if (constant)
        {
            constants["table"] = Array(table);
        }

        var owner = new SamplingExpressions(Slot, tier, constants, shapes);
        Func<SamplingFrame, ExprValue> run = owner.Compile(expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], initial, true);
        frame.Reset(default);
        return (owner, frame, slots, run);
    }

    [Fact]
    public void PreparationOwnsItsSnapshotAndRetainsFullSignedBounds()
    {
        ExprValue[] values = [ExprValue.Number(long.MinValue), ExprValue.Number(7), ExprValue.Number(long.MaxValue)];
        var table = Assert.IsType<SamplingIntegerTable>(SamplingIntegerTable.Prepare(ExprValue.Array(values)));
        values[1] = ExprValue.Number(99);
        Assert.Equal(new SamplingIntegerRange(long.MinValue, long.MaxValue), table.Range);
        Assert.Equal(long.MinValue, table.Read(0));
        Assert.Equal(7, table.Read(ExprValue.Number(1)));
        Assert.Equal(long.MaxValue, table.Read(2));
        Assert.Null(SamplingIntegerTable.Prepare(ExprValue.Number(0)));
        Assert.Null(SamplingIntegerTable.Prepare(ExprValue.Array([ExprValue.Bool(false)])));
        Assert.Null(SamplingIntegerTable.Prepare(ExprValue.Array([ExprValue.String("1")])));
        Assert.Null(SamplingIntegerTable.Prepare(ExprValue.Array([ExprValue.Rational(1, 2)])));
        Assert.Null(SamplingIntegerTable.Prepare(ExprValue.Array([ExprValue.Number(BigInteger.One << 64)])));
        Assert.Null(SamplingIntegerTable.Prepare(ExprValue.Array(new ExprValue[65537])));
    }

    [Fact]
    public void PreparedIndexValidationKeepsPinnedTypeFractionAndBoundsErrors()
    {
        var table = Assert.IsType<SamplingIntegerTable>(SamplingIntegerTable.Prepare(Array(11, 17)));
        Assert.Equal(17, table.Read(ExprValue.Rational(2, 2)));
        var fraction = Assert.Throws<ExpressionEvaluationException>(() => table.Read(ExprValue.Rational(1, 2)));
        Assert.Equal("EVAL_INVALID_INDEX", fraction.Code);
        Assert.Equal("index", fraction.Location);
        Assert.Equal("EVAL_TYPE_ERROR",
            Assert.Throws<ExpressionEvaluationException>(() => table.Read(ExprValue.String("0"))).Code);
        foreach (var value in new[] { -1L, 2, long.MinValue, long.MaxValue })
        {
            var error = Assert.Throws<ExpressionEvaluationException>(() => table.Read(value));
            Assert.Equal(EvalErrorCodes.IndexOutOfRange, error.Code);
            Assert.Equal($"index[{value}]", error.Location);
        }

        var empty = Assert.IsType<SamplingIntegerTable>(SamplingIntegerTable.Prepare(Array()));
        Assert.Equal("index[0]", Assert.Throws<ExpressionEvaluationException>(() => empty.Read(0)).Location);
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void LookupArithmeticMatchesIndependentArrayResultsAndResetsScalarCaches(SamplingTier tier)
    {
        var values = Enumerable.Range(0, 31).Select(value => (long)(value * value % 23 - 11)).ToArray();
        Expression multiplied = B(BinaryOp.Mul, S("position"), N(7));
        Expression quotient = new CallExpr
        {
            Function = "floor",
            Args = [B(BinaryOp.Div, multiplied, N(31))]
        };
        Expression position = B(BinaryOp.Sub, multiplied, B(BinaryOp.Mul, quotient, N(31)));
        Expression lookup = Index(position);
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) engine = Bind(Map(B(BinaryOp.Add, B(BinaryOp.Mul, lookup, N(3)), lookup)), tier, values);
        BigInteger[] expected = Enumerable.Range(0, 66).Select(index => new BigInteger(4 * values[index * 7 % 31]))
            .ToArray();
        for (var repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(expected, engine.Run(engine.Frame).ArrayValue!.Select(value => value.AsInteger()));
            Assert.Equal(999, engine.Frame.Cells[engine.Slots["position"]].Value.AsInteger());
        }
    }

    [Fact]
    public void SharedCodeKeepsSeparateTablesAndNeverPreparesMutableArrays()
    {
        var shapes = new SamplingShapes();
        Expression expression = Map(B(BinaryOp.Add, Index(S("position")), N(1)));
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) left = Bind(expression, SamplingTier.Compiled, [3, 5], 2, shapes: shapes);
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) right = Bind(expression, SamplingTier.Compiled, [-9, -17], 2, shapes: shapes);
        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) a =
            SamplingCodegen.CompileValue(left.Owner, name => left.Slots[name], expression);
        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) b =
            SamplingCodegen.CompileValue(right.Owner, name => right.Slots[name], expression);
        Assert.Same(a.Code.Method, b.Code.Method);
        Assert.Single(a.Operands.IntegerTables);
        Assert.Single(b.Operands.IntegerTables);
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) mutable = Bind(expression, SamplingTier.Compiled, [7, 11], 2, false, shapes);
        Assert.Null(mutable.Owner.IntegerTable("table"));
        for (var repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(new BigInteger[] { 4, 6 },
                a.Code(left.Frame, a.Operands).ArrayValue!.Select(value => value.AsInteger()));
            Assert.Equal(new BigInteger[] { -8, -16 },
                b.Code(right.Frame, b.Operands).ArrayValue!.Select(value => value.AsInteger()));
            mutable.Frame.Cells[mutable.Slots["table"]].SetTyped(Array(repeat, repeat + 1));
            Assert.Equal(new BigInteger[] { repeat + 1, repeat + 2 },
                mutable.Run(mutable.Frame).ArrayValue!.Select(value => value.AsInteger()));
        }
    }

    [Theory]
    [InlineData(SamplingTier.Interpreted)]
    [InlineData(SamplingTier.Tiered)]
    [InlineData(SamplingTier.Compiled)]
    public void TableBoundsNeverAllowArithmeticOverflowOrTruncateWideFallbacks(SamplingTier tier)
    {
        Expression expression = Map(B(BinaryOp.Add, Index(S("position")), N(1)));
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) engine = Bind(expression, tier, [long.MaxValue, long.MinValue], 2);
        for (var repeat = 0; repeat < 100; repeat++)
        {
            Assert.Equal(new[] { (BigInteger)long.MaxValue + 1, (BigInteger)long.MinValue + 1 },
                engine.Run(engine.Frame).ArrayValue!.Select(value => value.AsInteger()));
        }
    }

    [Fact]
    public void LazyPositionsAndInvalidConstantArraysRetainEagerOperandErrorOrder()
    {
        Expression division = B(BinaryOp.Div, N(1), N(0));
        Expression position = new IfExpr
        {
            Condition = S("flag"),
            ThenExpr = division,
            ElseExpr = S("position")
        };
        Expression expression = Map(B(BinaryOp.Add, Index(position), N(1)));
        (SamplingExpressions Owner, SamplingFrame Frame, Dictionary<string, int> Slots, Func<SamplingFrame, ExprValue>
            Run) engine = Bind(expression, SamplingTier.Compiled, [3, 5], 2);
        for (var repeat = 0; repeat < 100; repeat++)
        {
            engine.Run(engine.Frame);
        }

        engine.Frame.Cells[engine.Slots["flag"]].SetTyped(ExprValue.Bool(true));
        var error = Assert.Throws<ExpressionEvaluationException>(() => engine.Run(engine.Frame));
        Assert.Equal(EvalErrorCodes.DivisionByZero, error.Code);
        Assert.Equal("/", error.Location);

        var slots = new Dictionary<string, int>
        {
            ["table"] = 0,
            ["missing"] = 1
        };
        var owner = new SamplingExpressions(name => slots[name], SamplingTier.Compiled,
            new Dictionary<string, ExprValue>
            {
                ["table"] = ExprValue.String("not an array")
            });
        Func<SamplingFrame, ExprValue> invalid = owner.Compile(Index(division));
        var frame = new SamplingFrame(slots, new SamplingCell[2], new Dictionary<string, object?>());
        frame.Reset(default);
        Assert.Equal(EvalErrorCodes.DivisionByZero,
            Assert.Throws<ExpressionEvaluationException>(() => invalid(frame)).Code);
        Assert.Null(owner.IntegerTable("table"));
    }

    [Fact]
    public void IteratorShadowingCannotReadAnOuterConstantTable()
    {
        var slots = new Dictionary<string, int>();

        int Slot(string name)
        {
            if (!slots.TryGetValue(name, out var index))
            {
                slots[name] = index = slots.Count;
            }

            return index;
        }

        var owner = new SamplingExpressions(Slot, SamplingTier.Compiled,
            new Dictionary<string, ExprValue>
            {
                ["table"] = Array(99)
            });
        Expression expression = new MapExpr
        {
            StateKey = "nested",
            ItemName = "table",
            ItemType = ExprType.Array,
            Body = Index(N(0))
        };
        Assert.Throws<ConstantStateConflict>(() => SamplingCodegen.CompileValue(owner, Slot, expression));
        // The plan retries without invalidated constant facts, as for other scoped aliases.
        var general = new SamplingExpressions(Slot, SamplingTier.Compiled);
        (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) native =
            SamplingCodegen.CompileValue(general, Slot, expression);
        var frame = new SamplingFrame(slots, new SamplingCell[slots.Count], new Dictionary<string, object?>
        {
            ["nested"] = new object[] { new[] { 7 }, new[] { 11 } }
        });
        frame.Reset(default);
        Assert.Empty(native.Operands.IntegerTables);
        Assert.Equal(new BigInteger[] { 7, 11 },
            native.Code(frame, native.Operands).ArrayValue!.Select(value => value.AsInteger()));
    }

    private sealed class CountProbe : IReadOnlyList<ExprValue>
    {
        internal bool ThrowOnCount;
        public int Count => ThrowOnCount ? throw new InvalidOperationException("Count was evaluated") : 1;
        public ExprValue this[int index] => ExprValue.Number(11);
        public IEnumerator<ExprValue> GetEnumerator() => Enumerable.Repeat(ExprValue.Number(11), 1).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
