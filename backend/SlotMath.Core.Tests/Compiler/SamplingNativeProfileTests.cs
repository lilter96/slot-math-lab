using System.Numerics;
using System.Text.Json;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Model;
using SlotMath.Core.Random;

namespace SlotMath.Core.Tests.Compiler;

[Collection("SamplingOptions")]
public sealed class SamplingNativeProfileTests
{
    private static ConstantExpr N(int value) => new()
    {
        Kind = ConstantKind.Integer,
        Value = value.ToString()
    };

    private static FieldAccessExpr F(string name) => new()
    {
        Target = "state",
        Path = [name]
    };

    [Fact]
    public void ProfileMapRetainsAllBindingsOfOneSharedMethodAndLocatedErrors()
    {
        SamplingNativeProfile.Configure(true);
        try
        {
            var shapes = new SamplingShapes();
            var a = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = F("a"),
                Right = N(1)
            };
            BinaryExpr b = a with
            {
                Left = F("b"),
                Right = N(2)
            };
            var owner = new SamplingExpressions(_ => 0, SamplingTier.Compiled, shapes: shapes);
            (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) first =
                SamplingCodegen.CompileValue(owner, _ => 0, a);
            (Func<SamplingFrame, SamplingOperands?, ExprValue> Code, SamplingOperands Operands) second =
                SamplingCodegen.CompileValue(owner, _ => 0, b);
            Assert.Same(first.Code, second.Code);
            Assert.StartsWith("sml_value_", first.Code.Method.Name);
            SamplingNativeSymbol[] symbols = SamplingNativeProfile.Snapshot();
            Assert.Equal(2, symbols.Length);
            Assert.All(symbols, symbol => Assert.Equal(first.Code.Method.Name, symbol.MethodName));
            Assert.Contains(symbols,
                symbol => symbol.ExpressionJson == JsonSerializer.Serialize<Expression>(a, JsonOptions.Default));
            Assert.Contains(symbols,
                symbol => symbol.ExpressionJson == JsonSerializer.Serialize<Expression>(b, JsonOptions.Default));

            SamplingFrame Frame(string field)
            {
                return new SamplingFrame(new Dictionary<string, int>
                {
                    [field] = 0
                }, new SamplingCell[1], new Dictionary<string, object?>());
            }

            SamplingFrame left = Frame("a");
            SamplingFrame right = Frame("b");
            left.Reset(default);
            right.Reset(default);
            Assert.Equal("a",
                Assert.Throws<ExpressionEvaluationException>(() => first.Code(left, first.Operands)).Location);
            Assert.Equal("b",
                Assert.Throws<ExpressionEvaluationException>(() => second.Code(right, second.Operands)).Location);
            left.Cells[0].SetTyped(ExprValue.Number(4));
            right.Cells[0].SetTyped(ExprValue.Number(4));
            Assert.Equal(5, first.Code(left, first.Operands).AsInteger());
            Assert.Equal(6, second.Code(right, second.Operands).AsInteger());
            Assert.False(SamplingNativeProfile.Truncated);
        }
        finally
        {
            SamplingNativeProfile.Configure(false);
        }
    }

    [Fact]
    public void RunNamesMapToNodeIdsWithoutChangingDrawsOrMethodReuse()
    {
        SamplingNativeProfile.Configure(false);
        try
        {
            var layout = new Dictionary<string, int>();

            int Slot(string name)
            {
                if (!layout.TryGetValue(name, out var index))
                {
                    layout[name] = index = layout.Count;
                }

                return index;
            }

            var owner = new SamplingExpressions(Slot, SamplingTier.Compiled);

            Node[] Nodes(string prefix)
            {
                return
                [
                    new DrawNode
                    {
                        Id = prefix + "draw",
                        StateWriteKey = "salt",
                        DrawWeights =
                        [
                            new DrawWeight
                            {
                                OutcomeId = "2",
                                Weight = 1,
                                Value = 2
                            },
                            new DrawWeight
                            {
                                OutcomeId = "5",
                                Weight = 2,
                                Value = 5
                            }
                        ]
                    },
                    new ModifyStateNode
                    {
                        Id = prefix + "write",
                        OutputKey = "win",
                        ExpressionId = "win"
                    }
                ];
            }

            var expressions = new Dictionary<string, Expression>
            {
                ["win"] = new BinaryExpr
                {
                    Op = BinaryOp.Add,
                    Left = new CallExpr
                    {
                        Function = "tonumber",
                        Args = [F("salt")]
                    },
                    Right = N(3)
                }
            };
            (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
                before = SamplingCodegen.CompileRun(owner, Slot, Nodes("first-"), expressions, BigInteger.One);
            Assert.Empty(SamplingNativeProfile.Snapshot());
            SamplingNativeProfile.Configure(true);
            (Func<SamplingFrame, SamplingOperands?, SeededRandom, object?, object?> Code, SamplingOperands Operands)
                after = SamplingCodegen.CompileRun(owner, Slot, Nodes("second-"), expressions, BigInteger.One);
            Assert.Same(before.Code, after.Code);
            SamplingNativeSymbol symbol = Assert.Single(SamplingNativeProfile.Snapshot());
            Assert.Equal(after.Code.Method.Name, symbol.MethodName);
            Assert.Equal(new[] { "second-draw", "second-write" }, symbol.NodeIds);
            var frame = new SamplingFrame(layout, new SamplingCell[layout.Count], new Dictionary<string, object?>());
            AliasMethod independent = WeightSet.FromIntegers([1, 2]).AliasTable;
            for (var seed = 0; seed < 64; seed++)
            {
                frame.Reset(default);
                var actual = new SeededRandom(seed);
                var expected = new SeededRandom(seed);
                actual.EnableAudit();
                expected.EnableAudit();
                var win = (independent.Sample(expected) == 0 ? 2 : 5) + 3;
                after.Code(frame, after.Operands, actual, null);
                Assert.Equal(win, frame.Cells[layout["win"]].Value.AsInteger());
                Assert.Equal(JsonSerializer.Serialize(expected.AuditSnapshot(0)),
                    JsonSerializer.Serialize(actual.AuditSnapshot(0)));
                Assert.Equal(expected.NextUInt64(), actual.NextUInt64());
                actual.EndAudit();
                expected.EndAudit();
            }
        }
        finally
        {
            SamplingNativeProfile.Configure(false);
        }
    }
}
