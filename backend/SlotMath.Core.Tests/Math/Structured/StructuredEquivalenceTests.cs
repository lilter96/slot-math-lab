using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsCheck;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Math.Regime;
using SlotMath.Core.Math.Structured;
using SlotMath.Core.Model;
using Xunit.Abstractions;
using Xunit.Sdk;
using Dict = System.Collections.Generic.Dictionary<string, object?>;

namespace SlotMath.Core.Tests.Math.Structured;

/// <summary>
///     D25 gate for the structured exact evaluator: on generated graphs its payout distribution is the classic
///     exact interpreter's, value for value and mass for mass, and it never answers where the classic one fails.
/// </summary>
public class StructuredEquivalenceTests(ITestOutputHelper output)
{
    private const int Depth = 3;
    private const long FeatureBranches = 40_000;

    // Case i is generated from its own PCG stream of one pinned seed, so the suite is the same on every run.
    private const ulong Seed = 0x534C4F544D415448;

    private static readonly Gen<Expression>[] Num = new Gen<Expression>[Depth + 1],
        Bool = new Gen<Expression>[Depth + 1],
        Text = new Gen<Expression>[Depth + 1],
        List = new Gen<Expression>[Depth + 1];

    private static readonly Gen<Stmt[]>[] Block = new Gen<Stmt[]>[Depth + 1];
    private static readonly Gen<Game> Games;

    // Games built the way slot features are, for the chain solver: a counter of spins left that an iteration takes
    // one from and a draw adds to, a multiplier that moves with the draws, wins that depend on both, a second
    // amount collected on the side, and a payment after the loop that depends on how it ended.
    private static readonly Gen<Game> Features = Gen.Int[0, 5].Array[14].Select(Gen.Int[0, 5].Array[14], Gen.Int[0, 7])
        .Select((first, second, shape) =>
        {
            Stmt[] feature = Feature(first);
            return Fixture(shape switch
            {
                // Two features in a row, each with its own chain.
                0 => [.. feature, .. Feature(second)],
                // A feature some rounds do not reach: its entry state has no law of its own.
                1 =>
                [
                    new DrawStmt("s", CoinPays),
                    new BranchStmt(Cmp(CompareOp.Neq, F("s"), S("0")), feature, [.. Coin])
                ],
                _ => feature
            });
        });

    // ── D9 reference fixtures, authored as graphs ────────────────────────

    private static readonly (string, int, int)[] CoinPays = [("3", 1, 0), ("1", 3, 0), ("0", 4, 0)];

    // ── Reels and the built-in evaluators ─────────────────────────────

    private static readonly Gen<int[]> Reels = Gen.Int[0, 11].Array[64];

    // ── Games of the usual size ───────────────────────────────────────

    private static readonly string[] Faces = ["S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7", "W"];

    private static readonly int[][] TwentyLines =
    [
        [1, 1, 1, 1, 1], [0, 0, 0, 0, 0], [2, 2, 2, 2, 2], [0, 1, 2, 1, 0], [2, 1, 0, 1, 2], [0, 0, 1, 2, 2],
        [2, 2, 1, 0, 0], [1, 0, 0, 0, 1], [1, 2, 2, 2, 1], [0, 1, 1, 1, 0], [2, 1, 1, 1, 2], [1, 0, 1, 2, 1],
        [1, 2, 1, 0, 1], [0, 1, 0, 1, 0], [2, 1, 2, 1, 2], [1, 1, 0, 1, 1], [1, 1, 2, 1, 1], [0, 0, 2, 0, 0],
        [2, 2, 0, 2, 2], [0, 2, 0, 2, 0]
    ];

    static StructuredEquivalenceTests()
    {
        // The graphs are well typed, as the validator requires. What still fails at run time is what a round can
        // meet: a zero divisor, an index out of range or fractional, text that is not a number, an empty min/max,
        // a field no statement has written yet ("late"), and a value of another type put into a field by PutState.
        Gen<string> digit = Gen.OneOfConst("0", "1", "2", "3");
        Gen<string> textKey = Gen.OneOfConst("s", "t");

        // Operands inside iteration forms. Iteration forms are not nested: their static cost multiplies.
        Gen<Expression> flatNum = Gen.Frequency((3, Gen.Int[-2, 6].Select(N)), (3, Gen.OneOfConst("x", "y").Select(F)),
            (2, textKey.Select(k => Call("tonumber", F(k)))));
        Gen<Expression> flatBool = Gen.Frequency((1, Gen.Const(F("flag"))),
            (3, textKey.Select(digit).Select((k, v) => Cmp(CompareOp.Eq, F(k), S(v)))),
            (2, flatNum.Select(flatNum).Select((x, y) => Cmp(CompareOp.Lt, x, y))));
        Gen<Expression> flatText = Gen.Frequency((2, digit.Select(S)), (3, textKey.Select(F)));

        for (var d = 0; d <= Depth; d++)
        {
            Gen<Expression> numLeaf = Gen.Frequency(
                (4, Gen.Int[-2, 6].Select(N)), (4, Gen.OneOfConst("x", "y", "win").Select(F)),
                (1, Gen.Const(F("late"))),
                (3, textKey.Select(k => Call("tonumber", F(k)))), (1, Gen.Const(Call("length", F("arr")))),
                (1,
                    digit.Select(v => (Expression)new AggregateExpr
                    {
                        Func = AggregateFunc.Count,
                        StateKey = "arr",
                        ItemName = "cell",
                        Predicate = Cmp(CompareOp.Eq, F("cell"), S(v))
                    })));
            Gen<Expression> boolLeaf = Gen.Frequency((1, Gen.Bool.Select(B)), (2, Gen.Const(F("flag"))),
                (4, textKey.Select(digit).Select((k, v) => Cmp(CompareOp.Eq, F(k), S(v)))));
            Gen<Expression> textLeaf = Gen.Frequency((2, digit.Select(S)), (4, textKey.Select(F)));
            Gen<Expression> listLeaf = Gen.Const(F("arr"));
            if (d == 0)
            {
                (Num[d], Bool[d], Text[d], List[d]) = (numLeaf, boolLeaf, textLeaf, listLeaf);
                continue;
            }

            (Gen<Expression> n, Gen<Expression> b, Gen<Expression> t, Gen<Expression> l) =
                (Num[d - 1], Bool[d - 1], Text[d - 1], List[d - 1]);
            // Positions computed from data and divisions by data fail often, so they are the rare forms.
            Num[d] = Gen.Frequency(
                (12, numLeaf),
                (14,
                    Gen.OneOfConst(BinaryOp.Add, BinaryOp.Sub, BinaryOp.Mul).Select(n, n)
                        .Select((op, x, y) => Bin(op, x, y))),
                (1, n.Select(n).Select((x, y) => Bin(BinaryOp.Div, x, y))),
                (2, n.Select(Gen.Int[1, 4]).Select((x, k) => Bin(BinaryOp.Div, x, N(k)))),
                (8, b.Select(n, n).Select(If)),
                (4, Gen.OneOfConst("min", "max").Select(n, n).Select((fn, x, y) => Call(fn, x, y))),
                (2, Gen.OneOfConst("abs", "floor", "ceil", "round").Select(n).Select((fn, x) => Call(fn, x))),
                (4, t.Select(x => Call("tonumber", x))),
                (1, l.Select(n).Select((a, i) => Call("tonumber", Call("index", a, i)))),
                (4, l.Select(Gen.Int[0, 1]).Select((a, i) => Call("tonumber", Call("index", a, N(i))))),
                (2, l.Select(a => Call("length", a))),
                (2, Gen.OneOfConst(AggregateFunc.Sum, AggregateFunc.Count, AggregateFunc.Max, AggregateFunc.Min,
                    AggregateFunc.Product).Select(digit, flatBool, flatNum).Select((fn, v, p, add) =>
                    (Expression)new AggregateExpr
                    {
                        Func = fn,
                        StateKey = "arr",
                        ItemName = "cell",
                        Predicate = Bin(BinaryOp.Or, Cmp(CompareOp.Neq, F("cell"), S(v)), p),
                        ValueExpr = Bin(BinaryOp.Add, Call("tonumber", F("cell")), add)
                    })),
                (2,
                    flatNum.Select(flatNum).Select((init, v) => (Expression)new FoldExpr
                    {
                        StateKey = "arr",
                        AccName = "acc",
                        ItemName = "cell",
                        IndexName = "at",
                        Init = init,
                        Body = Bin(BinaryOp.Add, F("acc"),
                            Bin(BinaryOp.Mul, Call("tonumber", F("cell")), Bin(BinaryOp.Add, F("at"), v)))
                    })));
            Bool[d] = Gen.Frequency(
                (4, boolLeaf),
                (6,
                    Gen.OneOfConst(CompareOp.Eq, CompareOp.Neq, CompareOp.Lt, CompareOp.Gt, CompareOp.Lte,
                        CompareOp.Gte).Select(n, n).Select((op, x, y) => Cmp(op, x, y))),
                (2, Gen.OneOfConst(CompareOp.Eq, CompareOp.Neq).Select(t, t).Select((op, x, y) => Cmp(op, x, y))),
                (4, Gen.OneOfConst(BinaryOp.And, BinaryOp.Or).Select(b, b).Select((op, x, y) => Bin(op, x, y))),
                (2, b.Select(x => (Expression)new NotExpr
                {
                    Expr = x
                })),
                (1, l.Select(t).Select((a, x) => Call("contains", a, x))),
                (1, b.Select(b, b).Select(If)));
            Text[d] = Gen.Frequency(
                (8, textLeaf),
                (6, b.Select(t, t).Select(If)),
                (4, n.Select(x => Call("tostring", x))),
                (1, l.Select(n).Select((a, i) => Call("index", a, i))),
                (3, l.Select(a => Call("index", a, N(0)))));
            List[d] = Gen.Frequency(
                (4, listLeaf),
                (3, l.Select(t).Select((a, x) => Call("append", a, x))),
                (2,
                    flatText.Select(body => (Expression)new MapExpr
                    {
                        StateKey = "arr",
                        ItemName = "cell",
                        IndexName = "at",
                        Body = If(Cmp(CompareOp.Eq, F("at"), N(1)), body, F("cell"))
                    })),
                (1,
                    digit.Select(flatBool).Select((v, p) => (Expression)new FilterExpr
                    {
                        StateKey = "arr",
                        ItemName = "cell",
                        Predicate = Bin(BinaryOp.Or, Cmp(CompareOp.Neq, F("cell"), S(v)), p)
                    })));
        }

        // A draw: up to four outcomes, some of zero weight (never all of them), written to a text field or only passed on.
        // Half the draws use one of two fixed tables, so that a graph often holds independent draws of one law:
        // the evaluator names variables by their law, and must still keep such draws apart.
        Gen<(string, int, int)[]> table = Gen.Frequency(
            (1, Gen.Const(new[] { ("1", 1, 1), ("2", 1, 2) })),
            (1, Gen.Const(new[] { ("0", 1, 0), ("1", 2, 1), ("3", 1, 3) })),
            (2, digit.Select(Gen.Frequency((1, Gen.Const(0)), (6, Gen.Int[1, 4])), Gen.Int[0, 5]).Array[1, 4]
                .Select(outcomes =>
                {
                    (string, int, int)[] distinct = outcomes.DistinctBy(o => o.Item1)
                        .Select(o => (o.Item1, o.Item2, o.Item3)).ToArray();
                    if (distinct.All(o => o.Item2 == 0))
                    {
                        distinct[0].Item2 = 1;
                    }

                    return distinct;
                })));
        Gen<Stmt> draw = Gen
            .Frequency((3, Gen.Const<string?>("s")), (3, Gen.Const<string?>("t")), (1, Gen.Const((string?)null)))
            .Select(table)
            .Select((key, outcomes) => (Stmt)new DrawStmt(key, outcomes));
        // Two independent draws of one law, combined by something that is not a sum.
        Gen<Stmt> twin = table.Select(Gen.OneOfConst("x", "y", "win"), Gen.Int[0, 5], Num[1])
            .Select((outcomes, key, form, extra) =>
            {
                (Expression first, Expression second) = (Call("tonumber", F("s")), Call("tonumber", F("t")));
                return (Stmt)new SeqStmt([
                    new DrawStmt("s", outcomes), new DrawStmt("t", outcomes), new ModifyStmt(key, form switch
                    {
                        0 => Bin(BinaryOp.Mul, first, second),
                        1 => If(Cmp(CompareOp.Eq, F("s"), F("t")), extra, F(key)),
                        2 => Call("max", first, second),
                        3 => Bin(BinaryOp.Add, F(key), Bin(BinaryOp.Mul, first, Bin(BinaryOp.Sub, second, extra))),
                        // Sums whose summands share both draws: they are dependent and must not be convolved.
                        4 => Bin(BinaryOp.Add, Bin(BinaryOp.Mul, first, second), Call("max", first, second)),
                        _ => Bin(BinaryOp.Add,
                            Bin(BinaryOp.Add, F(key), If(Cmp(CompareOp.Eq, F("s"), F("t")), N(2), N(0))),
                            Bin(BinaryOp.Mul, first, second))
                    })
                ]);
            });

        Gen<Stmt> Modify(int d)
        {
            return Gen.Frequency(
                (6,
                    Gen.Frequency((3, Gen.Const("x")), (3, Gen.Const("y")), (4, Gen.Const("win")),
                        (1, Gen.Const("late"))).Select(Num[d]).Select((key, e) => (Stmt)new ModifyStmt(key, e))),
                (2, textKey.Select(Text[d]).Select((key, e) => (Stmt)new ModifyStmt(key, e))),
                (1, Bool[d].Select(e => (Stmt)new ModifyStmt("flag", e))),
                (2, List[d].Select(e => (Stmt)new ModifyStmt("arr", e))));
        }

        Gen<Stmt> data = digit.Array[0, 4].Select(values => (Stmt)new DataStmt("arr", values));
        Gen<Stmt> stateIo =
            Gen.Frequency((1, Gen.OneOfConst("x", "win", "s", "absent").Select(k => (Stmt)new GetStmt(k))),
                (1, Gen.OneOfConst("x", "win", "put").Select(k => (Stmt)new PutStmt(k))));

        for (var d = 0; d <= Depth; d++)
        {
            // A data node turns numeric text into numbers, which most expressions over the array then reject: keep it rare.
            Gen<Stmt> simple = Gen.Frequency((15, draw), (18, Modify(System.Math.Min(d + 1, Depth))), (1, data),
                (3, stateIo), (6, twin));
            if (d == 0)
            {
                Block[d] = simple.Array[1, 3];
                continue;
            }

            Gen<Stmt[]> inner = Block[d - 1];
            // An arm is a block, nothing at all (the chain ends there), or empty (it goes straight on); a branch keeps at least one edge.
            Gen<Stmt[]> arm = Gen.Frequency((6, inner), (1, Gen.Const(Array.Empty<Stmt>())));
            Gen<Stmt[]?> other = Gen.Frequency((5, inner.Select(s => (Stmt[]?)s)), (1, Gen.Const((Stmt[]?)null)),
                (1, Gen.Const<Stmt[]?>([])));
            Gen<Stmt> branch = Bool[2].Select(arm, other, Gen.Bool).Select((c, a, o, swap) =>
                (Stmt)(swap ? new BranchStmt(c, o, a) : new BranchStmt(c, a, o)));
            Gen<Stmt> loop = Gen.Int[1, 4].Select(Gen.Int[0, 5],
                    Gen.Frequency((1, Gen.Const((Expression?)null)), (2, Bool[2].Select(e => (Expression?)e))), inner)
                .Select((max, bound, stop, body) => (Stmt)new LoopStmt(max, bound, stop, body));
            Gen<Stmt> statement = Gen.Frequency((6, simple), (3, branch), (2, loop));
            Gen<Stmt> fan = inner.Select(inner).Select((a, b) => (Stmt)new FanStmt(a, b));
            Block[d] = statement.Array[1, 4]
                .Select(Gen.Frequency((7, Gen.Const((Stmt?)null)), (1, fan.Select(s => (Stmt?)s))))
                .Select((body, last) => last is null ? body : [.. body, last]);
        }

        Games = Block[Depth]
            .Select(Gen.Frequency((5, Gen.Const((Stmt[]?)null)), (1, Block[1].Select(s => (Stmt[]?)s))), Gen.Bool,
                Gen.Frequency((1, Gen.Int[1, 6]), (3, Gen.Const(100000)))).Select(Gen.Int[-1, 4].Select(Gen.Int[0, 3],
                digit, Gen.Bool, digit.Array[2, 4], Gen.Frequency((7, Gen.Const(false)), (1, Gen.Const(true)))))
            .Select((shape, state) => new Game(shape.Item1, shape.Item2, shape.Item3, shape.Item4, state.Item1,
                state.Item2, state.Item3, state.Item4, state.Item5, state.Item6));
    }

    private static Stmt[] Coin =>
    [
        new DrawStmt("s", CoinPays), new ModifyStmt("win", Bin(BinaryOp.Add, F("win"), Call("tonumber", F("s"))))
    ];

    private static Stmt[] Feature(int[] c)
    {
        Expression pay = Call("tonumber", F("s"));
        Expression move = Call("tonumber", F("t"));
        (string, int, int)[] pays = [("0", 1 + c[0], 0), ("1", 1 + c[1] % 3, 0), ("2", c[2] % 2, 0)];
        (string, int, int)[] moves = [("0", 2 + c[3], 0), ("1", c[4] % 3, 0), ("3", c[5] % 2, 0)];
        var main = new List<Stmt>();
        // The spins the feature starts with: fixed, or drawn, so that the chain starts from several states.
        if (c[6] < 3)
        {
            main.Add(new ModifyStmt("x", N(1 + c[6])));
        }
        else
        {
            main.Add(new DrawStmt("t", [("1", 2, 0), ("2", 1, 0), ("0", c[6] - 3, 0)]));
            main.Add(new ModifyStmt("x", move));
        }

        var body = new List<Stmt>
        {
            new DrawStmt("s", pays),
            new DrawStmt("t", moves),
            new ModifyStmt("win", Bin(BinaryOp.Add, F("win"), c[7] switch
            {
                0 => pay,
                // Multiplied by the state, by the draw that moves the state, or by both.
                1 => Bin(BinaryOp.Mul, pay, F("y")),
                2 => Bin(BinaryOp.Mul, pay, move),
                3 => If(Cmp(CompareOp.Eq, F("t"), S("3")), N(2), pay),
                4 => Bin(BinaryOp.Mul, pay, Bin(BinaryOp.Add, F("y"), move)),
                _ => Bin(BinaryOp.Sub, Bin(BinaryOp.Mul, pay, N(2)), If(Cmp(CompareOp.Eq, F("t"), S("0")), pay, N(0)))
            }))
        };
        switch (c[8])
        {
            case 1 or 2:
                body.Add(new ModifyStmt("y",
                    If(Cmp(CompareOp.Gte, F("y"), N(3)), N(3), Bin(BinaryOp.Add, F("y"), move))));
                break;
            case 3:
                body.Add(new ModifyStmt("y", If(Cmp(CompareOp.Eq, F("s"), S("0")), N(1), N(2))));
                break;
        }

        switch (c[9])
        {
            case 1 or 2: body.Add(new ModifyStmt("late", Bin(BinaryOp.Add, F("late"), move))); break;
            case 3:
                body.Add(new ModifyStmt("late", Bin(BinaryOp.Add, F("late"), Bin(BinaryOp.Mul, pay, F("y"))))); break;
        }

        body.Add(new ModifyStmt("x", Bin(BinaryOp.Add, Bin(BinaryOp.Sub, F("x"), N(1)), c[10] switch
        {
            0 => N(0),
            1 or 2 => If(Cmp(CompareOp.Eq, F("t"), S("3")), N(2), N(0)),
            _ => move
        })));
        Expression stop = Cmp(CompareOp.Lte, F("x"), N(0));
        main.Add(new LoopStmt(2 + c[11] % 4, c[12] == 0 ? 2 + c[11] % 4 : 0,
            c[12] == 1 ? Bin(BinaryOp.Or, stop, Cmp(CompareOp.Gte, F("y"), N(3))) : stop, [.. body]));
        // What the loop left behind is paid on, or not looked at again.
        switch (c[13])
        {
            case 1:
                main.Add(new ModifyStmt("win",
                    Bin(BinaryOp.Add, F("win"), If(Cmp(CompareOp.Gt, F("x"), N(0)), N(5), N(0)))));
                break;
            case 2: main.Add(new ModifyStmt("win", Bin(BinaryOp.Add, F("win"), F("late")))); break;
            case 3:
                main.Add(new ModifyStmt("win",
                    Bin(BinaryOp.Add, F("win"), Bin(BinaryOp.Mul, F("late"), F("y"))))); break;
            case 4:
                main.Add(new ModifyStmt("win",
                    Bin(BinaryOp.Add, F("win"), If(Cmp(CompareOp.Gt, F("late"), N(3)), N(2), N(0)))));
                break;
        }

        return [.. main];
    }

    // ── Expression AST shorthands ────────────────────────────────────────
    private static Expression N(int n) => new ConstantExpr
    {
        Kind = ConstantKind.Integer,
        Value = n.ToString()
    };

    private static Expression S(string s) => new ConstantExpr
    {
        Kind = ConstantKind.String,
        Value = s
    };

    private static Expression B(bool b) =>
        new ConstantExpr
        {
            Kind = ConstantKind.Boolean,
            Value = b ? "true" : "false"
        };

    private static Expression F(string name) => new FieldAccessExpr
    {
        Path = [name],
        Target = "state"
    };

    private static Expression Bin(BinaryOp op, Expression a, Expression b) =>
        new BinaryExpr
        {
            Op = op,
            Left = a,
            Right = b
        };

    private static Expression Cmp(CompareOp op, Expression a, Expression b) =>
        new CompareExpr
        {
            Op = op,
            Left = a,
            Right = b
        };

    private static Expression If(Expression c, Expression a, Expression b) =>
        new IfExpr
        {
            Condition = c,
            ThenExpr = a,
            ElseExpr = b
        };

    private static Expression Call(string name, params Expression[] args) =>
        new CallExpr
        {
            Function = name,
            Args = args
        };

    private static GraphConfig Graph(Game game)
    {
        var emitter = new Emitter();
        emitter.Nodes.Add(new MetricsSinkNode
        {
            Id = "sink",
            WinCap = game.Cap,
            WinStateKey = game.WinKey ? "win" : null,
            Inputs = new Dictionary<string, Port>
            {
                ["in"] = new()
                {
                    Name = "in",
                    Type = PortType.Number
                }
            }
        });
        emitter.Emit(game.Main, "sink");
        if (game.Second is not null)
        {
            emitter.Emit(game.Second, "sink");
        }

        var initial = new Dictionary<string, JsonElement>
        {
            ["x"] = JsonSerializer.SerializeToElement(game.X),
            ["win"] = JsonSerializer.SerializeToElement(0),
            ["s"] = JsonSerializer.SerializeToElement(game.Text),
            ["flag"] = JsonSerializer.SerializeToElement(game.Flag),
            ["arr"] = JsonSerializer.SerializeToElement(game.Items),
            ["y"] = JsonSerializer.SerializeToElement(game.Y),
            ["t"] = JsonSerializer.SerializeToElement(game.Items[0])
        };
        // "late" is declared, so reading it type-checks, but it exists only once a statement has written it.
        if (!game.OmitLate)
        {
            initial["late"] = JsonSerializer.SerializeToElement(game.Y);
        }

        return new GraphConfig
        {
            SchemaVersion = "1.0",
            Nodes = [.. emitter.Nodes],
            Edges = [.. emitter.Edges],
            Expressions = emitter.Expressions,
            InitialState = initial,
            StateSchema =
            [
                new StateFieldSchema
                {
                    Name = "late",
                    Type = "number"
                }
            ]
        };
    }

    /// <summary>
    ///     With <paramref name="loops" /> other than Auto every loop, whatever its cap, is solved as a chain in that
    ///     mode (or unrolled where it is no chain), so that the generated graphs exercise the chain solver.
    /// </summary>
    private static (Verdict Verdict, string Note) Compare(GraphConfig graph,
        StructuredFault fault = StructuredFault.None, LoopMode loops = LoopMode.Auto, bool chainEveryLoop = false,
        long branches = 10_000, Rational? prune = null)
    {
        CompileResult compiled;
        // A draw whose weights are all zero is refused by the compiler with an exception rather than an error list.
        try
        {
            compiled = new GraphCompiler().Compile(graph);
        }
        catch (ArgumentException exception)
        {
            return (Verdict.Invalid, exception.GetType().Name + ": " + exception.Message);
        }

        if (!compiled.IsValid)
        {
            return (Verdict.Invalid, compiled.Errors[0].Code + ": " + compiled.Errors[0].Message);
        }

        Dist<BigInteger>? classic = null;
        Exception? failure = null;
        try
        {
            // The branch limit alone bounds the reference, so the verdict does not depend on the machine. It is kept
            // low: enumeration near the limit takes seconds and gigabytes on graphs the evaluator under test finishes in a millisecond.
            classic = ExactInterpreter.Evaluate(compiled.ReferenceProgram!, new Dict(), StateHasher.CanonicalHash,
                    new ExactConfig
                    {
                        Budget = new Budget
                        {
                            MaxBranches = branches,
                            MaxTime = TimeSpan.FromHours(1)
                        }
                    })
                .ValueDistribution();
        }
        catch (BudgetExceededException)
        {
            return (Verdict.ClassicBudget, "");
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
            new StructuredExactOptions
            {
                MaxWork = 10_000_000,
                DistributionWork = 10_000_000,
                HitFrequencyWork = 10_000_000,
                VarianceWork = 10_000_000,
                Fault = fault,
                Loops = loops,
                ChainLoopsAbove = chainEveryLoop ? 0 : 32,
                // With a mass to prune below, chains are cut short however small their numbers still are.
                PruneLoopMassBelow = prune,
                PruneLoopsAboveBits = prune is null ? int.MaxValue : 0,
                PruneLoopsAboveWork = long.MaxValue,
                AlwaysStructuralHitFrequency = true,
                AlwaysStructuralSecondMoment = true
            });
        if (!result.Succeeded)
        {
            return (failure is null ? Verdict.DeclinedWhereClassicAnswers : Verdict.DeclinedWhereClassicFails,
                result.DeclinedReason!);
        }

        Assert.True(failure is null,
            $"The structured evaluator answered where the classic interpreter fails: {failure?.GetType().Name}: {failure?.Message}");
        // The classic distribution is over uncapped payouts; the cap applies when metrics are computed.
        BigInteger cap = new BigInteger(graph.Nodes.OfType<MetricsSinkNode>().Single().WinCap!.Value) *
                         compiled.WinScale;
        var expected = new SortedDictionary<BigInteger, Rational>();
        foreach ((BigInteger value, Rational mass) in StructuredLabDemoTests.Normalized(classic!))
        {
            BigInteger capped = BigInteger.Min(value, cap);
            expected[capped] = (expected.TryGetValue(capped, out Rational sum) ? sum : Rational.Zero) + mass;
        }

        Rational mean = expected.Aggregate(Rational.Zero,
            (total, entry) => total + new Rational(entry.Key, 1) * entry.Value);
        if (!result.PrunedMass.IsZero)
        {
            return (Verdict.Enclosed, Enclosed(result, expected, mean, prune!.Value, compiled.WinScale));
        }

        Assert.Equal(mean, result.ExpectedWin);
        // The hit frequency has two derivations, from the distribution and from the graph's structure; each must be the reference's.
        Rational hits = expected.Where(entry => entry.Key.Sign > 0)
            .Aggregate(Rational.Zero, (total, entry) => total + entry.Value);
        if (result.HitFrequency is { } reported)
        {
            Assert.Equal(hits, reported);
        }

        if (result.StructuralHitFrequency is { } structural)
        {
            Assert.Equal(hits, structural);
        }

        // So has the second moment: from the distribution and by bilinearity over the parts of the payout.
        Rational second = expected.Aggregate(Rational.Zero,
            (total, entry) => total + new Rational(entry.Key * entry.Key, 1) * entry.Value);
        if (result.SecondMoment is { } reportedSecond)
        {
            Assert.Equal(second, reportedSecond);
        }

        if (result.StructuralSecondMoment is { } structuralSecond)
        {
            Assert.Equal(second, structuralSecond);
        }

        var tags = new List<string>();
        if (result.StructuralHitFrequency is not null)
        {
            tags.Add("structural hit frequency");
        }

        if (result.StructuralSecondMoment is not null)
        {
            tags.Add("structural second moment");
        }

        tags.AddRange(result.Loops.Select(loop => loop.Evaluation).Where(mode => mode != "unrolled").Distinct());
        var note = string.Join(';', tags);
        if (result.Distribution is null)
        {
            return (Verdict.EqualMeanOnly, note);
        }

        Assert.NotNull(result.HitFrequency);
        Assert.Equal(expected, StructuredLabDemoTests.Normalized(result.Distribution));
        return (Verdict.Equal, note);
    }

    /// <summary>
    ///     A result with loops cut short is that of another game, equal to this one outside the pruned rounds. Both
    ///     pay at most <c>most</c> in such a round, so every figure of the reference lies within the pruned mass
    ///     times the most that figure can differ by.
    /// </summary>
    private static string Enclosed(StructuredExactResult result, SortedDictionary<BigInteger, Rational> expected,
        Rational mean, Rational prune, BigInteger winScale)
    {
        Rational pruned = result.PrunedMass;
        Assert.True(pruned <= prune * new Rational(result.Loops.Sum(loop => loop.Runs), 1),
            $"Pruned mass {pruned.ToDouble():R} is above what was asked for.");
        Assert.Equal(pruned, result.Loops.Aggregate(Rational.Zero, (sum, loop) => sum + loop.PrunedMass));
        Assert.NotNull(result.MaxWin);
        var most = new Rational(BigInteger.Max(expected.Keys.Max(), result.MaxWin!.Value), 1);

        static Rational Gap(Rational a, Rational b)
        {
            return a < b ? b - a : a - b;
        }

        Assert.True(Gap(mean, result.ExpectedWin) <= pruned * most,
            $"The mean is off by {Gap(mean, result.ExpectedWin).ToDouble():R}, more than the pruned mass allows.");
        // The declared enclosure is the same statement with the win cap for the most a round pays.
        (Rational low, Rational high) = result.RtpInterval(winScale);
        Rational rtp = mean / new Rational(winScale, 1);
        Assert.True(low <= rtp && rtp <= high, "The RTP is outside its declared enclosure.");
        RtpMetric metric = result.Rtp(winScale);
        Assert.Equal(Provenance.ExactInterval, metric.Provenance.Provenance);
        Assert.Equal(BoundSource.DeclaredWinCap, metric.Provenance.BoundSource);
        Assert.True(metric.LoDisplay <= rtp.ToDouble() && rtp.ToDouble() <= metric.HiDisplay);

        Rational hits = expected.Where(entry => entry.Key.Sign > 0)
            .Aggregate(Rational.Zero, (total, entry) => total + entry.Value);
        foreach (Rational? reported in new[] { result.HitFrequency, result.StructuralHitFrequency })
        {
            Assert.True(reported is null || Gap(hits, reported.Value) <= pruned,
                "The hit frequency is off by more than the pruned mass.");
        }

        Rational second = expected.Aggregate(Rational.Zero,
            (total, entry) => total + new Rational(entry.Key * entry.Key, 1) * entry.Value);
        foreach (Rational? reported in new[] { result.SecondMoment, result.StructuralSecondMoment })
        {
            Assert.True(reported is null || Gap(second, reported.Value) <= pruned * most * most,
                "The second moment is off by more than the pruned mass allows.");
        }

        return "pruned";
    }

    private static Game Case(int index, out string seed, Gen<Game>? source = null)
    {
        var pcg = new PCG((uint)index, Seed);
        seed = pcg.ToString();
        return (source ?? Games).Generate(pcg, null, out _);
    }

    /// <summary>
    ///     Compare the first <paramref name="count" /> pinned cases. Returns the verdict counts, the first failing case
    ///     and how many fail.
    /// </summary>
    private static (SortedDictionary<Verdict, int> Verdicts, ConcurrentDictionary<string, int> Notes, int? Failed, int
        Failures) Run(int count, StructuredFault fault, bool all = false, LoopMode loops = LoopMode.Auto,
            bool chainEveryLoop = false, Gen<Game>? source = null, Rational? prune = null,
            Func<int, GraphConfig>? graphs = null)
    {
        // The feature games loop over a state the reference has to enumerate, which takes it more branches.
        var branches = source is null && graphs is null ? 10_000 : FeatureBranches;
        var failures = 0;
        var verdicts = new ConcurrentDictionary<Verdict, int>();
        var notes = new ConcurrentDictionary<string, int>();
        var failed = int.MaxValue;
        // An evaluation blocks its caller while it runs on its own thread, and the pool answers blocked workers with
        // more workers; the heaviest cases hold gigabytes each, so their number at one time is fixed here.
        var workers = new ParallelOptions
        {
            MaxDegreeOfParallelism = System.Math.Min(Environment.ProcessorCount, 8)
        };
        Parallel.For(0, count, workers, index =>
        {
            if (!all && index > Volatile.Read(ref failed))
            {
                return;
            }

            try
            {
                (Verdict verdict, var note) =
                    Compare(graphs?.Invoke(index) ?? Graph(Case(index, out _, source)), fault, loops, chainEveryLoop,
                        branches, prune);
                verdicts.AddOrUpdate(verdict, 1, (_, n) => n + 1);
                if (verdict is Verdict.Invalid)
                {
                    notes.AddOrUpdate($"{verdict}: {Regex.Replace(note, "'[^']*'", "'…'")}", 1, (_, n) => n + 1);
                }

                if (verdict is Verdict.DeclinedWhereClassicAnswers)
                {
                    notes[$"{verdict} in case {index}: {note}"] = 1;
                }

                if (verdict is Verdict.Equal or Verdict.EqualMeanOnly or Verdict.Enclosed && note.Length > 0)
                {
                    foreach (var tag in note.Split(';'))
                    {
                        notes.AddOrUpdate(tag, 1, (_, n) => n + 1);
                    }
                }
            }
            catch (Exception exception) when (exception is XunitException or InvalidOperationException
                                                  or StructuredInconsistencyException)
            {
                Interlocked.Increment(ref failures);
                int seen;
                while (index < (seen = Volatile.Read(ref failed)) &&
                       Interlocked.CompareExchange(ref failed, index, seen) != seen)
                {
                }
            }
        });
        return (new SortedDictionary<Verdict, int>(verdicts), notes, failed == int.MaxValue ? null : failed, failures);
    }

    [Fact]
    public void GeneratedGraphs_Pbt_10000_DistributionEqualsClassicInterpreter()
    {
        (SortedDictionary<Verdict, int> verdicts, ConcurrentDictionary<string, int> notes, var failed, _) =
            Run(10_000, StructuredFault.None);
        if (failed is { } index)
        {
            // Reproduce the failing case under CsCheck so that it is shrunk and reported with its seed.
            Case(index, out var seed);
            Games.Sample(game => { Compare(Graph(game)); }, seed: seed, iter: 2000,
                print: game => $"case {index}: {JsonSerializer.Serialize(Graph(game), JsonOptions.Default)}");
            Assert.Fail($"Case {index} failed but did not reproduce.");
        }

        foreach ((Verdict verdict, var count) in verdicts)
        {
            output.WriteLine($"{verdict}: {count}");
        }

        foreach (var (note, count) in notes.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                     .Take(25))
        {
            output.WriteLine($"  {count} × {note}");
        }

        var equal = verdicts.GetValueOrDefault(Verdict.Equal);
        Assert.True(equal >= 6500,
            $"Only {equal} of 10000 generated graphs were compared distribution for distribution.");
        Assert.True(verdicts.GetValueOrDefault(Verdict.DeclinedWhereClassicFails) >= 1000,
            "Too few generated graphs exercise evaluation errors.");
        var structural = notes.GetValueOrDefault("structural hit frequency");
        Assert.True(structural >= 5000,
            $"Only {structural} generated graphs had their hit frequency derived from the graph's structure.");
        Assert.True(
            verdicts.GetValueOrDefault(Verdict.Invalid) + verdicts.GetValueOrDefault(Verdict.ClassicBudget) +
            verdicts.GetValueOrDefault(Verdict.DeclinedWhereClassicAnswers) <= 600,
            "Too many generated graphs are not compared.");
    }

    // Feature games, every loop solved as a chain. With the amounts kept in the chain state the whole distribution
    // is compared; with the amounts carried by their moments, the mean, the second moment and the hit frequency.
    [Theory]
    [InlineData(LoopMode.Law)]
    [InlineData(LoopMode.Moments)]
    internal void FeatureGames_Pbt_5000_LoopsSolvedAsChains_EqualClassicInterpreter(LoopMode loops)
    {
        (SortedDictionary<Verdict, int> verdicts, ConcurrentDictionary<string, int> notes, var failed, _) =
            Run(5_000, StructuredFault.None, false, loops, true, Features);
        if (failed is { } index)
        {
            Case(index, out var seed, Features);
            Features.Sample(game => { Compare(Graph(game), StructuredFault.None, loops, true, FeatureBranches); },
                seed: seed, iter: 2000,
                print: game => $"case {index}: {JsonSerializer.Serialize(Graph(game), JsonOptions.Default)}");
            Assert.Fail($"Case {index} failed but did not reproduce.");
        }

        foreach ((Verdict verdict, var count) in verdicts)
        {
            output.WriteLine($"{verdict}: {count}");
        }

        foreach (var (note, count) in notes.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                     .Take(12))
        {
            output.WriteLine($"  {count} × {note}");
        }

        var compared = verdicts.GetValueOrDefault(Verdict.Equal) + verdicts.GetValueOrDefault(Verdict.EqualMeanOnly);
        Assert.True(compared >= 3700, $"Only {compared} of 5000 feature games were compared.");
        var chained = notes.GetValueOrDefault(loops == LoopMode.Law ? "chain" : "chain-moments");
        Assert.True(chained >= 3500, $"Only {chained} of 5000 feature games had a loop solved as a chain.");
        Assert.True(notes.GetValueOrDefault("structural second moment") >= 3500,
            "Too few feature games had their second moment derived from the graph's structure.");
        Assert.True(notes.GetValueOrDefault("structural hit frequency") >= 3500,
            "Too few feature games had their hit frequency derived from the graph's structure.");
    }

    // The first of the general pinned cases with every loop tried as a chain: whatever a generated loop is, the
    // chain solver describes it exactly or leaves it to unrolling, and the result is the reference's either way.
    [Theory]
    [InlineData(LoopMode.Law)]
    [InlineData(LoopMode.Moments)]
    internal void GeneratedGraphs_Pbt_2000_LoopsSolvedAsChains_EqualClassicInterpreter(LoopMode loops)
    {
        (SortedDictionary<Verdict, int> verdicts, ConcurrentDictionary<string, int> notes, var failed, _) =
            Run(2_000, StructuredFault.None, false, loops, true);
        if (failed is { } index)
        {
            Case(index, out var seed);
            Games.Sample(game => { Compare(Graph(game), StructuredFault.None, loops, true); }, seed: seed, iter: 2000,
                print: game => $"case {index}: {JsonSerializer.Serialize(Graph(game), JsonOptions.Default)}");
            Assert.Fail($"Case {index} failed but did not reproduce.");
        }

        foreach ((Verdict verdict, var count) in verdicts)
        {
            output.WriteLine($"{verdict}: {count}");
        }

        foreach (var (note, count) in notes.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                     .Take(25))
        {
            output.WriteLine($"  {count} × {note}");
        }
    }

    private static Game Fixture(params Stmt[] main) =>
        new(main, null, true, 1_000_000, 0, 1, "0", false, ["0", "0"], false);

    private StructuredExactResult Evaluate(Game game, out CompileResult compiled)
    {
        compiled = new GraphCompiler().Compile(Graph(game));
        Assert.True(compiled.IsValid, string.Join("; ", compiled.Errors.Select(e => $"{e.NodeId}: {e.Message}")));
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
            new StructuredExactOptions
            {
                AlwaysStructuralHitFrequency = true
            });
        output.WriteLine(
            $"succeeded={result.Succeeded} declined={result.DeclinedReason} elapsed={result.Elapsed.TotalMilliseconds:F0}ms work={result.Work} values={result.Distribution?.Count}");
        Assert.True(result.Succeeded, result.DeclinedReason);
        return result;
    }

    [Fact]
    public void RefA_Coin_RtpIsThreeQuarters_VarianceFifteenSixteenths()
    {
        StructuredExactResult result = Evaluate(Fixture(Coin), out CompileResult compiled);
        var scale = new Rational(compiled.WinScale, 1);
        Assert.Equal(new Rational(3, 4), result.ExpectedWin / scale);
        Assert.Equal(new Rational(1, 2), result.HitFrequency);
        Assert.Equal(new Rational(1, 2), result.StructuralHitFrequency);
        Rational second = StructuredLabDemoTests.Normalized(result.Distribution!).Aggregate(Rational.Zero,
            (total, entry) =>
                total + new Rational(entry.Key, 1) / scale * (new Rational(entry.Key, 1) / scale) * entry.Value);
        Assert.Equal(new Rational(15, 16), second - new Rational(9, 16));
        Assert.Equal(Verdict.Equal, Compare(Graph(Fixture(Coin))).Verdict);
    }

    // A set of positions is read through its operands: the mask is drawn, and the win is the size of the set it
    // makes with position zero. Masks 1, 2, 5 and 12 with weights 1, 3, 4 and 2 make sets of 1, 2, 2 and 3 positions.
    [Fact]
    public void BitSetOverADrawnMask_ReadsItsOperands_AndEqualsTheClassicInterpreter()
    {
        (string, int, int)[] masks = [("1", 1, 0), ("2", 3, 0), ("5", 4, 0), ("12", 2, 0)];
        Expression size = new BitSetExpr
        {
            Op = BitSetOp.Count,
            Args =
            [
                new BitSetExpr
                {
                    Op = BitSetOp.Union,
                    Args = [F("x"), N(1)]
                }
            ]
        };
        Game game = Fixture(new DrawStmt("s", masks), new ModifyStmt("x", Call("tonumber", F("s"))),
            new ModifyStmt("win", Bin(BinaryOp.Add, F("win"), size)));
        StructuredExactResult result = Evaluate(game, out CompileResult compiled);
        Assert.Equal(new Rational(21, 10), result.ExpectedWin / new Rational(compiled.WinScale, 1));
        Assert.Equal(new Rational(1, 1), result.HitFrequency);
        Assert.Equal(Verdict.Equal, Compare(Graph(game)).Verdict);
    }

    // x is the number of free spins left: a trigger (1 in 20) awards three, and so does every retrigger.
    private static Game Retrigger(int cap)
    {
        (string, int, int)[] trigger = [("3", 1, 0), ("0", 19, 0)];
        return Fixture([
            .. Coin, new DrawStmt("s", trigger), new ModifyStmt("x", Call("tonumber", F("s"))),
            new LoopStmt(cap, 0, Cmp(CompareOp.Lte, F("x"), N(0)),
            [
                .. Coin, new DrawStmt("s", trigger),
                new ModifyStmt("x", Bin(BinaryOp.Add, Bin(BinaryOp.Sub, F("x"), N(1)), Call("tonumber", F("s"))))
            ])
        ]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void RefB_Retrigger_ShortCaps_EqualTheClassicDistribution(int cap)
    {
        (Verdict verdict, var note) = Compare(Graph(Retrigger(cap)));
        output.WriteLine($"cap {cap}: {verdict} {note}");
        Assert.Equal(Verdict.Equal, verdict);
    }

    // The fixture at its cap of 1,000 (D6, D9), which no unrolling reaches: the loop is solved as a Markov reward
    // chain over the number of spins left. Every figure is checked against a recurrence written here, independently
    // of the evaluator, over the probability that the loop is still running, and against the closed forms of the
    // branching process (Wald's identity for the limit).
    [Fact]
    public void RefB_Retrigger_FullCap_IsExact_AgainstAnIndependentRecurrence()
    {
        const int cap = 1000;
        CompileResult compiled = new GraphCompiler().Compile(Graph(Retrigger(cap)));
        Assert.True(compiled.IsValid);
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
            new StructuredExactOptions
            {
                MaxTime = TimeSpan.FromSeconds(120)
            });
        StructuredLoopResult? loop = result.Loops.SingleOrDefault();
        output.WriteLine(
            $"succeeded={result.Succeeded} declined={result.DeclinedReason} elapsed={result.Elapsed.TotalMilliseconds:F0}ms work={result.Work} notes={string.Join(" | ", result.LoopNotes)} loop={loop?.Evaluation} states={loop?.States}");
        Assert.True(result.Succeeded, result.DeclinedReason);
        Assert.NotNull(loop);
        Assert.NotEqual("unrolled", loop.Evaluation);

        Rational[] alive = RetriggerAlive(cap);

        // N iterations run, each paying an independent coin c; the base spin pays one more.
        Rational iterations = Rational.Zero;
        Rational iterationsSquared = Rational.Zero;
        Rational allBlank = Rational.One - alive[0];
        var half = new Rational(1, 2);
        Rational blank = half;
        for (var t = 0; t < cap; t++)
        {
            iterations += alive[t];
            iterationsSquared += new Rational(2 * t + 1, 1) * alive[t];
            // P(N = t + 1), each of its spins blank with probability 1/2.
            Rational exactly = t + 1 < cap ? alive[t] - alive[t + 1] : alive[t];
            allBlank += exactly * blank;
            blank *= half;
        }

        var (coinMean, coinSecond) = (new Rational(3, 4), new Rational(3, 2));
        Rational coinVariance = coinSecond - coinMean * coinMean;
        Assert.Equal(new Rational(15, 16), coinVariance);
        Rational mean = coinMean * (Rational.One + iterations);
        Rational variance = coinVariance * (Rational.One + iterations) +
                            coinMean * coinMean * (iterationsSquared - iterations * iterations);

        var unit = new Rational(compiled.WinScale, 1);
        Rational rtp = result.ExpectedWin / unit;
        Assert.Equal(mean, rtp);
        Assert.Equal(variance, result.Variance(compiled.WinScale));
        Assert.Equal(Rational.One - half * allBlank, result.HitFrequency);
        Assert.Equal(alive[cap - 1], loop.ExpectedCapHits);

        // Galton–Watson with offspring mean m = 3/20: 3/20 / (1 - m) spins expected without a cap, so 15/17 in the
        // limit; the cap takes away less than 10^-9 of it and is reached with probability at most 10^-15.
        var limit = new Rational(15, 17);
        Assert.Equal(limit, coinMean * (Rational.One + new Rational(3, 20) / (Rational.One - new Rational(3, 20))));
        Assert.True(rtp <= limit && rtp > limit - new Rational(1, 1_000_000_000), $"RTP {rtp.ToDouble():R}");
        Assert.True(loop.ExpectedCapHits <= new Rational(1, BigInteger.Pow(10, 15)));
        output.WriteLine(
            $"rtp={rtp.ToDouble():R} limit-rtp={(limit - rtp).ToDouble():E3} variance={variance.ToDouble():R} hit={result.HitFrequency!.Value.ToDouble():R} capHits={loop.ExpectedCapHits!.Value.ToDouble():E3}");
    }

    /// <summary>
    ///     alive[t] = P(the retrigger loop runs iteration t), by a recurrence over the spins left, which go down one
    ///     per spin and up three with probability 1/20. Written here, independently of the evaluator.
    /// </summary>
    private static Rational[] RetriggerAlive(int cap)
    {
        var alive = new Rational[cap];
        var spins = new BigInteger[2 * cap + 4];
        spins[3] = 1;
        spins[0] = 19;
        BigInteger scale = 20;
        for (var t = 0; t < cap; t++)
        {
            var next = new BigInteger[spins.Length];
            BigInteger running = BigInteger.Zero;
            for (var left = 1; left <= 3 + 2 * t; left++)
            {
                running += spins[left];
                next[left - 1] += spins[left] * 19;
                next[left + 2] += spins[left];
            }

            next[0] += spins[0] * 20;
            alive[t] = new Rational(running, scale);
            spins = next;
            scale *= 20;
        }

        return alive;
    }

    // A loop cut short of its cap (D5): the retrigger loop is ended as soon as fewer than one round in 10^9 is still
    // in it. The figures are then those of that shorter game, the exact ones lie in the declared enclosure, and
    // the pruned mass is the probability, by the independent recurrence, of being in the loop at that iteration.
    [Fact]
    public void RefB_Retrigger_Pruned_EnclosesTheExactValue_AndNamesItsMass()
    {
        const int cap = 1000;
        CompileResult compiled = new GraphCompiler().Compile(Graph(Retrigger(cap)));
        var epsilon = new Rational(1, 1_000_000_000);
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
            new StructuredExactOptions
            {
                PruneLoopMassBelow = epsilon,
                PruneLoopsAboveBits = 0
            });
        Assert.True(result.Succeeded, result.DeclinedReason);
        Rational[] alive = RetriggerAlive(cap);
        // The chain runs from each entry state on its own, so it is the loop's rounds that are measured against
        // epsilon: the one in twenty that trigger.
        var cut = Array.FindIndex(alive, mass => mass * new Rational(20, 1) < epsilon);
        output.WriteLine(
            $"pruned={result.PrunedMass.ToDouble():E3} after {cut} iterations, elapsed={result.Elapsed.TotalMilliseconds:F0}ms work={result.Work}");
        Assert.InRange(cut, 1, cap - 1);
        Assert.Equal(alive[cut], result.PrunedMass);
        Assert.Equal(alive[cut], result.Loops.Single().PrunedMass);

        // The shorter game is exact for what it is: the loop with a cap of `cut` iterations.
        Rational shorter = new Rational(3, 4) *
                           (Rational.One + alive.Take(cut).Aggregate(Rational.Zero, (sum, mass) => sum + mass));
        Rational exact = new Rational(3, 4) *
                         (Rational.One + alive.Aggregate(Rational.Zero, (sum, mass) => sum + mass));
        var unit = new Rational(compiled.WinScale, 1);
        Assert.Equal(shorter, result.ExpectedWin / unit);
        Assert.NotEqual(exact, shorter);

        (Rational low, Rational high) = result.RtpInterval(compiled.WinScale);
        Assert.True(low <= exact && exact <= high);
        Assert.Equal(result.PrunedMass * new Rational(2 * 1_000_000, 1), high - low);
        RtpMetric rtp = result.Rtp(compiled.WinScale);
        Assert.Equal(Provenance.ExactInterval, rtp.Provenance.Provenance);
        Assert.Equal(BoundSource.DeclaredWinCap, rtp.Provenance.BoundSource);
        Assert.True(rtp.LoDisplay <= exact.ToDouble() && exact.ToDouble() <= rtp.HiDisplay);

        // Negative control: the pruned rounds do pay, so the shorter game's value passed off as exact would be wrong.
        Assert.True(exact - shorter > result.PrunedMass * new Rational(1, 2));
    }

    // The largest cap a loop may declare (D6). Numbers of a hundred thousand steps would run to a million bits, so
    // with the default options the loops are cut short where under 10^-30 of the rounds are still in them, and the
    // enclosures hold the limits of the uncapped games, 15/17 and 94/965, which the capped values are within
    // 10^-170 of.
    [Fact]
    public void RefB_RefC_AtTheLargestCap_AreEnclosedWithin1e24()
    {
        const int cap = 100_000;
        foreach ((Game game, Rational limit) in new[]
                 {
                     (Retrigger(cap), new Rational(15, 17)), (MiniCascade(cap), new Rational(94, 965))
                 })
        {
            CompileResult compiled = new GraphCompiler().Compile(Graph(game));
            Assert.True(compiled.IsValid);
            StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
                new StructuredExactOptions
                {
                    MaxTime = TimeSpan.FromSeconds(120)
                });
            output.WriteLine(
                $"limit {limit}: succeeded={result.Succeeded} declined={result.DeclinedReason} pruned={result.PrunedMass.ToDouble():E3} elapsed={result.Elapsed.TotalMilliseconds:F0}ms work={result.Work}");
            Assert.True(result.Succeeded, result.DeclinedReason);
            Assert.False(result.PrunedMass.IsZero);
            Assert.True(result.PrunedMass < new Rational(1, BigInteger.Pow(10, 30)));
            (Rational low, Rational high) = result.RtpInterval(compiled.WinScale);
            Assert.True(low <= limit && limit <= high);
            Assert.True(high - low <= new Rational(2, BigInteger.Pow(10, 24)));
            Assert.Equal(Provenance.ExactInterval, result.Rtp(compiled.WinScale).Provenance.Provenance);
        }
    }

    // The short caps again with the loop forced into a chain, where the classic interpreter can still check every
    // figure, the distribution included when the chain keeps the win in its state.
    [Theory]
    [InlineData(LoopMode.Law, 1)]
    [InlineData(LoopMode.Law, 6)]
    [InlineData(LoopMode.Moments, 1)]
    [InlineData(LoopMode.Moments, 3)]
    [InlineData(LoopMode.Moments, 6)]
    internal void RefB_Retrigger_ShortCaps_AsAChain_EqualTheClassicInterpreter(LoopMode loops, int cap)
    {
        (Verdict verdict, var note) = Compare(Graph(Retrigger(cap)), StructuredFault.None, loops, true);
        output.WriteLine($"cap {cap} as {loops}: {verdict} {note}");
        Assert.Equal(loops == LoopMode.Law ? Verdict.Equal : Verdict.EqualMeanOnly, verdict);
        Assert.Contains(loops == LoopMode.Law ? "chain" : "chain-moments", note.Split(';'));
        Assert.Contains("structural second moment", note.Split(';'));
        Assert.Contains("structural hit frequency", note.Split(';'));
    }

    private static Game MiniCascade(int cap)
    {
        // Three cells from {P: 2, Q: 3, R: 5}; three of a kind pays (P 5, Q 2) and redraws, at most ten draws.
        (string, int, int)[] cell = [("1", 2, 0), ("2", 3, 0), ("0", 5, 0)];
        Expression triple =
            If(
                Bin(BinaryOp.And, Cmp(CompareOp.Eq, F("x"), F("y")),
                    Cmp(CompareOp.Eq, F("y"), Call("tonumber", F("s")))), F("x"), N(0));
        return Fixture(new LoopStmt(cap, 0, Cmp(CompareOp.Eq, F("late"), N(0)),
        [
            new DrawStmt("s", cell), new ModifyStmt("x", Call("tonumber", F("s"))),
            new DrawStmt("s", cell), new ModifyStmt("y", Call("tonumber", F("s"))),
            new DrawStmt("s", cell), new ModifyStmt("late", triple),
            new ModifyStmt("win",
                Bin(BinaryOp.Add, F("win"),
                    If(Cmp(CompareOp.Eq, F("late"), N(1)), N(5), If(Cmp(CompareOp.Eq, F("late"), N(2)), N(2), N(0)))))
        ]));
    }

    [Fact]
    public void RefC_MiniCascade_EqualsClassicAndTheClosedForm()
    {
        Game game = MiniCascade(10);
        StructuredExactResult result = Evaluate(game, out CompileResult compiled);
        Rational rtp = result.ExpectedWin / new Rational(compiled.WinScale, 1);

        // 94/1000 per draw, a further draw with probability 35/1000, ten draws at most.
        var again = new Rational(35, 1000);
        Rational tenth = Enumerable.Repeat(again, 10).Aggregate(Rational.One, (product, factor) => product * factor);
        Assert.Equal(new Rational(94, 1000) * (Rational.One - tenth) / (Rational.One - again), rtp);
        var limit = new Rational(94, 965);
        Assert.True(rtp <= limit && limit - rtp <= new Rational(1, 1_000_000_000_000));
        Assert.Equal(Verdict.Equal, Compare(Graph(game)).Verdict);
    }

    // The cascade as a chain: at the fixture's cap against the classic interpreter, and at a cap of thousands, with
    // nothing pruned, against the closed form, where the win is a geometric sum.
    [Theory]
    [InlineData(LoopMode.Law)]
    [InlineData(LoopMode.Moments)]
    internal void RefC_MiniCascade_AsAChain_EqualsClassicAndTheClosedForm(LoopMode loops)
    {
        (Verdict verdict, var note) = Compare(Graph(MiniCascade(10)), StructuredFault.None, loops, true);
        output.WriteLine($"{loops}: {verdict} {note}");
        Assert.Equal(loops == LoopMode.Law ? Verdict.Equal : Verdict.EqualMeanOnly, verdict);
        Assert.Contains(loops == LoopMode.Law ? "chain" : "chain-moments", note.Split(';'));

        if (loops == LoopMode.Law)
        {
            return;
        }

        const int cap = 4_000;
        CompileResult compiled = new GraphCompiler().Compile(Graph(MiniCascade(cap)));
        Assert.True(compiled.IsValid);
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled,
            new StructuredExactOptions
            {
                MaxTime = TimeSpan.FromSeconds(120),
                PruneLoopMassBelow = null
            });
        output.WriteLine(
            $"cap {cap}: succeeded={result.Succeeded} declined={result.DeclinedReason} elapsed={result.Elapsed.TotalMilliseconds:F0}ms work={result.Work} loops={string.Join(" | ", result.Loops.Select(loop => loop.Evaluation))}");
        Assert.True(result.Succeeded, result.DeclinedReason);
        var again = new Rational(35, 1000);
        var power = new Rational(BigInteger.Pow(35, cap), BigInteger.Pow(1000, cap));
        Rational draws = (Rational.One - power) / (Rational.One - again);
        Assert.Equal(new Rational(94, 1000) * draws, result.ExpectedWin / new Rational(compiled.WinScale, 1));
        Assert.Equal(new Rational(35, 1000), result.HitFrequency);
        // The cap is reached by the rounds whose every draw but the last paid.
        Assert.Equal(power / again, result.Loops.Single().ExpectedCapHits);
    }

    // Negative controls: each deliberate defect in the evaluator must be caught by the same pinned cases.
    [Theory]
    [InlineData(StructuredFault.MergeKeepsThenArm)]
    [InlineData(StructuredFault.DependentSumsConvolved)]
    [InlineData(StructuredFault.PseudoVariablesShared)]
    [InlineData(StructuredFault.ErrorsIgnored)]
    [InlineData(StructuredFault.CapIgnored)]
    [InlineData(StructuredFault.HitFromFirstPart)]
    [InlineData(StructuredFault.ProductsTakenIndependent)]
    internal void SeededDefect_FailsTheGate(StructuredFault fault)
    {
        var (_, _, failed, failures) = Run(2_000, fault, true);
        output.WriteLine($"{fault}: {failures} of 2000 cases fail, the first is case {failed}");
        Assert.True(failures >= 10, $"Only {failures} of 2000 cases catch {fault}.");
    }

    // Loops cut short of their cap, here as soon as fewer than one round in three is still running: every figure of the
    // reference must lie in the enclosure the pruned mass declares. A result that forgets what it pruned claims to
    // be exact and is not: that is the negative control.
    [Theory]
    [InlineData(LoopMode.Law, StructuredFault.None)]
    [InlineData(LoopMode.Moments, StructuredFault.None)]
    [InlineData(LoopMode.Moments, StructuredFault.PrunedMassDropped)]
    internal void FeatureGames_Pbt_PrunedLoops_EncloseTheClassicInterpreter(LoopMode loops, StructuredFault fault)
    {
        var prune = new Rational(1, 3);
        (SortedDictionary<Verdict, int> verdicts, _, var failed, var failures) =
            Run(3_000, fault, true, loops, true, Features, prune);
        foreach ((Verdict verdict, var count) in verdicts)
        {
            output.WriteLine($"{verdict}: {count}");
        }

        output.WriteLine($"{failures} of 3000 feature games fail, the first is case {failed}");
        if (fault != StructuredFault.None)
        {
            Assert.True(failures >= 100, $"Only {failures} of 3000 feature games catch {fault}.");
            Assert.Equal(0, verdicts.GetValueOrDefault(Verdict.Enclosed));
            return;
        }

        if (failed is { } index)
        {
            Case(index, out var seed, Features);
            Features.Sample(
                game => { Compare(Graph(game), fault, loops, true, FeatureBranches, prune); }, seed: seed, iter: 2000,
                print: game => $"case {index}: {JsonSerializer.Serialize(Graph(game), JsonOptions.Default)}");
            Assert.Fail($"Case {index} failed but did not reproduce.");
        }

        var enclosed = verdicts.GetValueOrDefault(Verdict.Enclosed);
        Assert.True(enclosed >= 300, $"Only {enclosed} of 3000 feature games had a loop cut short.");
    }

    // And the defects of the chain solver by the feature games, whose loops are solved as chains.
    [Theory]
    [InlineData(StructuredFault.ChainRewardsUnconditional, LoopMode.Moments)]
    [InlineData(StructuredFault.ChainCrossTermDropped, LoopMode.Moments)]
    [InlineData(StructuredFault.ChainZeroMassUnconditional, LoopMode.Moments)]
    [InlineData(StructuredFault.ChainCapOffByOne, LoopMode.Law)]
    [InlineData(StructuredFault.ChainCapOffByOne, LoopMode.Moments)]
    [InlineData(StructuredFault.ProductsTakenIndependent, LoopMode.Moments)]
    internal void SeededChainDefect_FailsTheGate(StructuredFault fault, LoopMode loops)
    {
        var (_, _, failed, failures) = Run(1_000, fault, true, loops, true, Features);
        output.WriteLine($"{fault} as {loops}: {failures} of 1000 feature games fail, the first is case {failed}");
        Assert.True(failures >= 10, $"Only {failures} of 1000 feature games catch {fault} as {loops}.");
    }

    private static GraphConfig ReelCase(int index, out string seed)
    {
        var pcg = new PCG((uint)index, Seed);
        seed = pcg.ToString();
        return ReelGraph(Reels.Generate(pcg, null, out _));
    }

    /// <summary>
    ///     A game on reel strips paid by a built-in evaluator: a few short reels over symbols that include a wild and
    ///     one that reads as a number, a paytable with whole and fractional payouts and sometimes two entries for one
    ///     symbol, paylines that are straight, crooked, short or off the board, and around them the shapes a game puts
    ///     an evaluator in: two evaluators on one board, a multiplier that is a constant or drawn, respins in a loop,
    ///     an evaluation behind a branch, a win cap that binds.
    /// </summary>
    private static GraphConfig ReelGraph(int[] c)
    {
        var cols = 2 + c[0] % 3;
        var rows = 1 + c[1] % 3;
        string[] pool = ["A", "B", "C", "W", "7"];
        ReelStrip[] strips = Enumerable.Range(0, cols).Select(col => new ReelStrip
        {
            Id = $"r{col}",
            Name = $"R{col}",
            Symbols = Enumerable.Range(0, 2 + c[2 + col] % 4).Select(stop => pool[c[8 + col * 6 + stop] % pool.Length])
                .ToArray()
        }).ToArray();

        string Pay(int symbol, int count)
        {
            var units = (1 + c[(34 + symbol * 3 + count) % c.Length] % 4) * count;
            return c[32] % 5 == 0 && count % 2 == 0 ? $"{units}.5" : units.ToString(CultureInfo.InvariantCulture);
        }

        var paid = new List<int>
        {
            0,
            1,
            2
        };
        if (c[33] % 3 == 0)
        {
            paid.Add(3);
        }

        if (c[33] % 2 == 0)
        {
            paid.Add(4);
        }

        if (c[34] % 6 == 0)
        {
            paid.Add(0);
        }

        var from = c[35] % 4 == 0 ? 1 : 2;
        PaytableEntry[] entries = paid.Select((symbol, at) => new PaytableEntry
        {
            SymbolId = pool[symbol],
            Counts = Enumerable.Range(from, System.Math.Max(cols - from + 1, 1)).ToArray(),
            Payouts = Enumerable.Range(from, System.Math.Max(cols - from + 1, 1))
                .Select(count => Pay(symbol + at, count))
                .ToArray()
        }).ToArray();

        Payline[] lines = Enumerable.Range(0, 1 + c[36] % 4).Select(line => new Payline
        {
            Positions = Enumerable.Range(0, c[37 + line] % 5 == 0 ? cols - 1 : cols)
                .Select(col => c[41 + line * 4 + col] % (c[57] % 7 == 0 ? rows + 1 : rows)).ToArray()
        }).ToArray();

        string[] transforms = ["lines", "ways", "cluster"];
        var transform = transforms[c[58] % 3];
        Expression? multiplier = (c[59] % 4) switch
        {
            0 => N(2),
            1 => If(Cmp(CompareOp.Eq, F("t"), S("2")), N(3), N(1)),
            _ => null
        };
        var eval = new EvalStmt(transform, multiplier, c[60] % 2 == 0);
        var coin = new DrawStmt("t", [("1", 1 + c[61] % 3, 0), ("2", 1, 0), ("3", 2, 1)]);
        Stmt[] main = (c[62] % 6) switch
        {
            0 => [coin, new SpinStmt(), eval, new SpinStmt(), new EvalStmt(transforms[(c[58] + 1) % 3], null, false)],
            1 => [coin, new LoopStmt(2 + c[63] % 2, 0, null, [new SpinStmt(), eval])],
            2 =>
            [
                coin, new SpinStmt(),
                new BranchStmt(Cmp(CompareOp.Eq, F("t"), S("1")), [eval],
                    [new EvalStmt(transforms[(c[58] + 2) % 3], multiplier, false)])
            ],
            3 => [coin, new SpinStmt(), new EvalStmt(null, multiplier, false)],
            _ => [coin, new SpinStmt(), eval]
        };

        var emitter = new Emitter
        {
            Typed = true
        };
        emitter.Nodes.Add(new MetricsSinkNode
        {
            Id = "sink",
            WinCap = c[63] % 5 == 0 ? 3 : 1_000_000,
            Inputs = new Dictionary<string, Port>
            {
                ["in"] = new()
                {
                    Name = "in",
                    Type = PortType.Number
                },
                ["board"] = new()
                {
                    Name = "board",
                    Type = PortType.Board
                },
                ["wins"] = new()
                {
                    Name = "wins",
                    Type = PortType.Wins
                }
            }
        });
        emitter.Emit(main, "sink");

        return new GraphConfig
        {
            SchemaVersion = "1.0",
            Symbols =
                pool.Select(id => new Symbol
                {
                    Id = id,
                    Name = id,
                    Kind = id == "W" && c[56] % 4 != 0 ? SymbolKind.Wild : SymbolKind.Standard
                }).ToArray(),
            Paytables =
            [
                new Paytable
                {
                    Id = "pt",
                    Entries = entries
                }
            ],
            PaylineSets =
            [
                new PaylineSet
                {
                    Id = "ps",
                    Paylines = lines
                }
            ],
            ReelStrips = strips,
            ReelSets =
            [
                new ReelSet
                {
                    Id = "rs",
                    Name = "Main",
                    StripIds = strips.Select(strip => strip.Id).ToArray()
                }
            ],
            BoardConfig = new BoardConfig
            {
                Rows = rows,
                Columns = cols
            },
            Nodes = [.. emitter.Nodes],
            Edges = [.. emitter.Edges],
            Expressions = emitter.Expressions,
            InitialState = new Dictionary<string, JsonElement>
            {
                ["t"] = JsonSerializer.SerializeToElement("1")
            }
        };
    }

    // Reel-strip draws and the built-in evaluators against the reference, which draws one board out of the whole
    // product of the reels and runs the evaluator on it: every payout distribution must be the same.
    [Fact]
    public void ReelGames_Pbt_5000_OpaqueTransformsDecline_UnnamedMapsEqualClassicInterpreter()
    {
        (SortedDictionary<Verdict, int> verdicts, ConcurrentDictionary<string, int> notes, var failed, _) =
            Run(5_000, StructuredFault.None, graphs: index => ReelCase(index, out _));
        if (failed is { } index)
        {
            ReelCase(index, out var seed);
            Reels.Sample(choices => { Compare(ReelGraph(choices), branches: FeatureBranches); }, seed: seed,
                iter: 2000,
                print: choices => $"case {index}: {JsonSerializer.Serialize(ReelGraph(choices), JsonOptions.Default)}");
            Assert.Fail($"Case {index} failed but did not reproduce.");
        }

        foreach ((Verdict verdict, var count) in verdicts)
        {
            output.WriteLine($"{verdict}: {count}");
        }

        foreach (var (note, count) in notes.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                     .Take(25))
        {
            output.WriteLine($"  {count} × {note}");
        }

        var equal = verdicts.GetValueOrDefault(Verdict.Equal);
        Assert.True(equal >= 500, $"Only {equal} unnamed-map reel games were compared distribution for distribution.");
        // Every named transform declines before executing or interpreting its mechanic.
        for (var caseIndex = 0; caseIndex < 5000; caseIndex++)
        {
            GraphConfig graph = ReelCase(caseIndex, out _);
            if (!graph.Nodes.OfType<MapNode>().Any(node => node.TransformId is not null))
            {
                continue;
            }

            CompileResult compiled = new GraphCompiler().Compile(graph);
            if (!compiled.IsValid)
            {
                continue;
            }

            StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled);
            Assert.False(result.Succeeded);
            Assert.Contains("opaque transform", result.DeclinedReason);
        }
    }

    [Theory]
    [InlineData("lines")]
    [InlineData("ways")]
    [InlineData("cluster")]
    public void OpaqueMap_DeclinesAndClassicInterpreterStillPays(string transform)
    {
        ReelStrip[] strips = Enumerable.Range(0, 5).Select(reel => new ReelStrip
        {
            Id = $"r{reel}",
            Name = $"R{reel}",
            Symbols = ["S0"]
        }).ToArray();
        CompileResult compiled = new GraphCompiler().Compile(FaceGame(
            [new SpinStmt(), new EvalStmt(transform, null, false)], strips, null));
        Assert.True(compiled.IsValid);
        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled);
        Assert.False(result.Succeeded);
        Assert.Contains("opaque transform", result.DeclinedReason);
        Dist<BigInteger> classic = ExactInterpreter
            .Evaluate(compiled.ReferenceProgram!, new Dict(), StateHasher.CanonicalHash).ValueDistribution();
        Assert.True(classic.ExpectedBigIntegerValue().Num > 0);
    }

    private static int FacePay(int face, int count) => (face + 1) * (count == 3 ? 1 : count == 4 ? 3 : 10);

    private static GraphConfig FaceGame(Stmt[] main, ReelStrip[] strips, Dictionary<string, JsonElement>? initial)
    {
        var emitter = new Emitter
        {
            Typed = true
        };
        emitter.Nodes.Add(new MetricsSinkNode
        {
            Id = "sink",
            WinCap = 100_000_000,
            Inputs = new Dictionary<string, Port>
            {
                ["in"] = new()
                {
                    Name = "in",
                    Type = PortType.Number
                },
                ["board"] = new()
                {
                    Name = "board",
                    Type = PortType.Board
                },
                ["wins"] = new()
                {
                    Name = "wins",
                    Type = PortType.Wins
                }
            }
        });
        emitter.Emit(main, "sink");
        return new GraphConfig
        {
            SchemaVersion = "1.0",
            Symbols = Faces.Select(id => new Symbol
            {
                Id = id,
                Name = id,
                Kind = id == "W" ? SymbolKind.Wild : SymbolKind.Standard
            }).ToArray(),
            Paytables =
            [
                new Paytable
                {
                    Id = "pt",
                    Entries = Enumerable.Range(0, 8).Select(face => new PaytableEntry
                    {
                        SymbolId = Faces[face],
                        Counts = [3, 4, 5],
                        Payouts = new[] { 3, 4, 5 }.Select(count =>
                            FacePay(face, count).ToString(CultureInfo.InvariantCulture)).ToArray()
                    }).ToArray()
                }
            ],
            PaylineSets =
            [
                new PaylineSet
                {
                    Id = "ps",
                    Paylines = TwentyLines.Select(rows => new Payline
                    {
                        Positions = rows
                    }).ToArray()
                }
            ],
            ReelStrips = strips,
            ReelSets = strips.Length == 0
                ? []
                :
                [
                    new ReelSet
                    {
                        Id = "rs",
                        Name = "Main",
                        StripIds = strips.Select(strip => strip.Id).ToArray()
                    }
                ],
            BoardConfig = new BoardConfig
            {
                Rows = 3,
                Columns = 5
            },
            Nodes = [.. emitter.Nodes],
            Edges = [.. emitter.Edges],
            Expressions = emitter.Expressions,
            InitialState = initial
        };
    }

    // Test-owned authored expressions compose the payouts. The kernel sees only scalar dependencies,
    // sums and products, with no evaluator name or inferred mechanic structure.
    private static Stmt[] AuthoredFacePayout(string kind)
    {
        var statements = new List<Stmt>
        {
            new ModifyStmt("award", N(0))
        };

        Expression Cell(int row, int col)
        {
            return Call("index", F("board"), N(row * 5 + col));
        }

        Expression Matches(Expression cell, string face)
        {
            return Bin(BinaryOp.Or,
                Cmp(CompareOp.Eq, cell, S(face)), Cmp(CompareOp.Eq, cell, S("W")));
        }

        if (kind == "lines")
        {
            foreach (var line in TwentyLines)
            {
                Expression symbol = S("X");
                for (var col = 4; col >= 0; col--)
                {
                    Expression cell = Cell(line[col], col);
                    symbol = If(Cmp(CompareOp.Neq, cell, S("W")), cell, symbol);
                }

                statements.Add(new ModifyStmt("symbol", symbol));
                Expression prefix = B(true);
                Expression count = N(0);
                for (var col = 0; col < 5; col++)
                {
                    Expression cell = Cell(line[col], col);
                    prefix = Bin(BinaryOp.And, prefix, Bin(BinaryOp.Or,
                        Cmp(CompareOp.Eq, cell, F("symbol")), Cmp(CompareOp.Eq, cell, S("W"))));
                    count = If(prefix, N(col + 1), count);
                }

                statements.Add(new ModifyStmt("count", count));
                Expression payout = N(0);
                for (var face = 0; face < 8; face++)
                {
                    Expression pays = N(0);
                    for (var matched = 3; matched <= 5; matched++)
                    {
                        pays = If(Cmp(CompareOp.Eq, F("count"), N(matched)), N(FacePay(face, matched)), pays);
                    }

                    payout = If(Cmp(CompareOp.Eq, F("symbol"), S(Faces[face])), pays, payout);
                }

                statements.Add(new ModifyStmt("award", Bin(BinaryOp.Add, F("award"), payout)));
            }
        }
        else
        {
            for (var face = 0; face < 8; face++)
            {
                for (var col = 0; col < 5; col++)
                {
                    Expression count = N(0);
                    for (var row = 0; row < 3; row++)
                    {
                        count = Bin(BinaryOp.Add, count, If(Matches(Cell(row, col), Faces[face]), N(1), N(0)));
                    }

                    statements.Add(new ModifyStmt($"count{col}", count));
                }

                Expression tail = Bin(BinaryOp.Mul, F("count4"), N(FacePay(face, 5)));
                tail = If(Cmp(CompareOp.Gt, F("count4"), N(0)), tail, N(FacePay(face, 4)));
                tail = If(Cmp(CompareOp.Gt, F("count3"), N(0)), Bin(BinaryOp.Mul, F("count3"), tail),
                    N(FacePay(face, 3)));
                Expression payout = tail;
                for (var col = 0; col < 3; col++)
                {
                    payout = Bin(BinaryOp.Mul, F($"count{col}"), payout);
                }

                statements.Add(new ModifyStmt("award", Bin(BinaryOp.Add, F("award"), payout)));
            }
        }

        return [.. statements];
    }

    private static GraphConfig AuthoredFaceGame(Stmt[] draws, string kind, ReelStrip[] strips,
        Dictionary<string, JsonElement>? initial)
    {
        GraphConfig graph = FaceGame([.. draws, .. AuthoredFacePayout(kind)], strips, initial);
        return graph with
        {
            Nodes = graph.Nodes.Select(node => node is MetricsSinkNode sink
                ? sink with
                {
                    WinStateKey = "award"
                }
                : node).ToArray()
        };
    }

    // Five reels of fifteen stops, three rows, twenty paylines: 759,375 boards, which the reference draws and pays
    // one by one. The structured result is the same rational, from each payline's own five cells.
    [Theory]
    [InlineData("lines")]
    [InlineData("ways")]
    public void FiveReels_FifteenStops_EqualTheReferenceOverEveryBoard(string transform)
    {
        ReelStrip[] strips = Enumerable.Range(0, 5).Select(reel => new ReelStrip
        {
            Id = $"r{reel}",
            Name = $"R{reel}",
            Symbols = Enumerable.Range(0, 15).Select(stop => Faces[(stop * (reel + 2) + stop * stop / 3 + reel) % 9])
                .ToArray()
        }).ToArray();
        CompileResult compiled =
            new GraphCompiler().Compile(AuthoredFaceGame([new SpinStmt()], transform, strips, null));
        Assert.True(compiled.IsValid, compiled.Errors.FirstOrDefault()?.Message);

        StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled);
        output.WriteLine(
            $"structured: succeeded={result.Succeeded} declined={result.DeclinedReason} elapsed={result.Elapsed.TotalMilliseconds:F0}ms work={result.Work} distribution={(result.Distribution is null ? "not built" : "built")}");
        Assert.True(result.Succeeded, result.DeclinedReason);

        // The independent reference uses the existing legacy evaluator, not the authored payout expressions.
        CompileResult reference = new GraphCompiler().Compile(FaceGame(
            [new SpinStmt(), new EvalStmt(transform, null, false)], strips, null));
        Assert.True(reference.IsValid);
        Assert.Equal(compiled.WinScale, reference.WinScale);
        var clock = Stopwatch.StartNew();
        Dist<BigInteger> classic = ExactInterpreter.Evaluate(reference.ReferenceProgram!, new Dict(),
                StateHasher.CanonicalHash,
                new ExactConfig
                {
                    Budget = new Budget
                    {
                        MaxBranches = 2_000_000,
                        MaxTime = TimeSpan.FromMinutes(10)
                    }
                })
            .ValueDistribution();
        BigInteger sum = classic.Entries.Aggregate(BigInteger.Zero, (total, entry) =>
            total + entry.Value * entry.Numerator);
        var expected = new Rational(sum, classic.Denominator);
        output.WriteLine(
            $"reference: {clock.ElapsedMilliseconds}ms over {classic.Denominator} boards, rtp={(expected / new Rational(compiled.WinScale, 1)).ToDouble():R}");
        Assert.Equal(expected, result.ExpectedWin);
        Assert.True(expected > Rational.Zero);
    }

    // Fifteen cells drawn one by one from ten faces of unequal weight: 10^15 boards, which nothing can list. The
    // expected payout of twenty paylines is the sum over the lines of a sum over the 10^5 face combinations of a
    // line, written out here from the rule; that of "ways" is a closed form in each reel's expected match count.
    [Fact]
    public void FifteenIndependentCells_BeyondEnumeration_EqualIndependentFormulas()
    {
        string[] faces = [.. Faces, "X"];

        int Weight(int cell, int face)
        {
            return 1 + (cell * 7 + face * 3) % 5;
        }

        Expression board = F("e");
        var draws = new List<Stmt>();
        for (var cell = 0; cell < 15; cell++)
        {
            var at = cell;
            draws.Add(new DrawStmt($"c{cell}",
                faces.Select((face, index) => (face, Weight(at, index), 0)).ToArray()));
            board = Call("append", board, F($"c{cell}"));
        }

        var initial = new Dictionary<string, JsonElement>
        {
            ["e"] = JsonSerializer.SerializeToElement(Array.Empty<string>()),
            ["rows"] = JsonSerializer.SerializeToElement(3),
            ["cols"] = JsonSerializer.SerializeToElement(5)
        };
        foreach (var cell in Enumerable.Range(0, 15))
        {
            initial[$"c{cell}"] = JsonSerializer.SerializeToElement("X");
        }

        var chance = new Rational[15 * faces.Length];
        for (var cell = 0; cell < 15; cell++)
        {
            var total = Enumerable.Range(0, faces.Length).Sum(face => Weight(cell, face));
            for (var face = 0; face < faces.Length; face++)
            {
                chance[cell * faces.Length + face] = new Rational(Weight(cell, face), total);
            }
        }

        const int wild = 8;
        foreach (var transform in new[] { "lines", "ways" })
        {
            CompileResult compiled =
                new GraphCompiler().Compile(AuthoredFaceGame([.. draws, new ModifyStmt("board", board)], transform, [],
                    initial));
            Assert.True(compiled.IsValid, compiled.Errors.FirstOrDefault()?.Message);
            StructuredExactResult result = StructuredExactEvaluator.Evaluate(compiled);
            output.WriteLine(
                $"{transform}: succeeded={result.Succeeded} declined={result.DeclinedReason} elapsed={result.Elapsed.TotalMilliseconds:F0}ms work={result.Work}");
            Assert.True(result.Succeeded, result.DeclinedReason);

            Rational expected = Rational.Zero;
            if (transform == "lines")
            {
                var on = new int[5];
                foreach (var line in TwentyLines)
                {
                    for (var code = 0; code < 100_000; code++)
                    {
                        Rational probability = Rational.One;
                        for (int col = 0, rest = code; col < 5; col++, rest /= 10)
                        {
                            on[col] = rest % 10;
                            probability *= chance[(line[col] * 5 + col) * faces.Length + on[col]];
                        }

                        // Left to right: a wild continues any run and takes the first face that follows it.
                        var match = on[0] == wild ? -1 : on[0];
                        var count = 1;
                        while (count < 5 && (on[count] == wild || match < 0 || on[count] == match))
                        {
                            match = match < 0 && on[count] != wild ? on[count] : match;
                            count++;
                        }

                        if (match is >= 0 and < 8 && count >= 3)
                        {
                            expected += probability * new Rational(FacePay(match, count), 1);
                        }
                    }
                }
            }
            else
            {
                for (var face = 0; face < 8; face++)
                {
                    // Per reel: the expected number of cells that show the face or the wild, and the chance of none.
                    var mean = new Rational[5];
                    var none = new Rational[5];
                    for (var col = 0; col < 5; col++)
                    {
                        mean[col] = Rational.Zero;
                        none[col] = Rational.One;
                        for (var row = 0; row < 3; row++)
                        {
                            var cell = row * 5 + col;
                            Rational shows = chance[cell * faces.Length + face] + chance[cell * faces.Length + wild];
                            mean[col] += shows;
                            none[col] *= Rational.One - shows;
                        }
                    }

                    // A run of exactly `count` reels pays the product of their match counts.
                    Rational ways = Rational.One;
                    for (var count = 1; count <= 5; count++)
                    {
                        ways *= mean[count - 1];
                        if (count >= 3)
                        {
                            expected += new Rational(FacePay(face, count), 1) * ways *
                                        (count == 5 ? Rational.One : none[count]);
                        }
                    }
                }
            }

            output.WriteLine($"{transform}: rtp={expected.ToDouble():R}");
            Assert.Equal(expected * new Rational(compiled.WinScale, 1), result.ExpectedWin);
            Assert.True(expected > Rational.Zero);
        }
    }

    // ── Generated programs ───────────────────────────────────────────────
    private abstract record Stmt;

    private sealed record DrawStmt(string? Key, (string Id, int Weight, int Value)[] Outcomes) : Stmt;

    private sealed record ModifyStmt(string Key, Expression Expr) : Stmt;

    private sealed record BranchStmt(Expression Cond, Stmt[]? Then, Stmt[]? Else) : Stmt;

    private sealed record LoopStmt(int Max, int IterBound, Expression? Stop, Stmt[] Body) : Stmt;

    private sealed record DataStmt(string Key, string[] Values) : Stmt;

    private sealed record GetStmt(string Key) : Stmt;

    private sealed record PutStmt(string Key) : Stmt;

    /// <summary>A reel-strip draw: the board of the config's reel set.</summary>
    private sealed record SpinStmt : Stmt;

    /// <summary>A map node naming a built-in evaluator (or none), with an optional multiplier port.</summary>
    private sealed record EvalStmt(string? Transform, Expression? Multiplier, bool Share) : Stmt;

    private sealed record FanStmt(Stmt[] First, Stmt[] Second) : Stmt;

    private sealed record SeqStmt(Stmt[] Parts) : Stmt;

    private sealed record Game(
        Stmt[] Main,
        Stmt[]? Second,
        bool WinKey,
        int Cap,
        int X,
        int Y,
        string Text,
        bool Flag,
        string[] Items,
        bool OmitLate);

    // ── Program → graph ──────────────────────────────────────────────────
    private sealed class Emitter
    {
        public readonly List<Edge> Edges = [];
        public readonly Dictionary<string, Expression> Expressions = new();
        public readonly List<Node> Nodes = [];

        /// <summary>Reel games pass boards and wins along their edges, which an edge must be typed for at both ends.</summary>
        public bool Typed;

        private int _next;

        private static Dictionary<string, Port> Ports(params string[] names) =>
            names.ToDictionary(n => n, n => new Port
            {
                Name = n,
                Type = PortType.Number
            });

        private Dictionary<string, Port> In()
        {
            Dictionary<string, Port> ports = Ports("in");
            if (Typed)
            {
                ports["board"] = new Port
                {
                    Name = "board",
                    Type = PortType.Board
                };
                ports["wins"] = new Port
                {
                    Name = "wins",
                    Type = PortType.Wins
                };
            }

            return ports;
        }

        private string Id(string kind) => $"{kind}{_next++}";

        private void Link(string source, string port, string? target)
        {
            if (target is not null)
            {
                Edges.Add(new Edge
                {
                    Id = $"e{Edges.Count}",
                    SourceNodeId = source,
                    SourcePort = port,
                    TargetNodeId = target,
                    TargetPort = Typed && port is "board" or "wins" ? port : "in"
                });
            }
        }

        private string Expr(Expression expression)
        {
            var id = $"x{Expressions.Count}";
            Expressions[id] = expression;
            return id;
        }

        /// <summary>Emit a block that continues at <paramref name="next" /> (null: the chain ends). Returns its first node.</summary>
        public string? Emit(Stmt[] block, string? next)
        {
            for (var i = block.Length - 1; i >= 0; i--)
            {
                next = Emit(block[i], next);
            }

            return next;
        }

        private string Emit(Stmt statement, string? next)
        {
            switch (statement)
            {
                case DrawStmt draw:
                {
                    var id = Id("draw");
                    Nodes.Add(new DrawNode
                    {
                        Id = id,
                        StateWriteKey = draw.Key,
                        Inputs = In(),
                        Outputs = Ports("out"),
                        DrawWeights = draw.Outcomes.Select(o =>
                            new DrawWeight
                            {
                                OutcomeId = o.Id,
                                Weight = o.Weight,
                                Value = o.Value
                            }).ToArray()
                    });
                    Link(id, "out", next);
                    return id;
                }
                case ModifyStmt modify:
                {
                    var id = Id("set");
                    Nodes.Add(new ModifyStateNode
                    {
                        Id = id,
                        OutputKey = modify.Key,
                        ExpressionId = Expr(modify.Expr),
                        Inputs = In(),
                        Outputs = Ports("out")
                    });
                    Link(id, "out", next);
                    return id;
                }
                case DataStmt data:
                {
                    var id = Id("data");
                    Nodes.Add(new DataNode
                    {
                        Id = id,
                        StateKey = data.Key,
                        Values = data.Values,
                        Inputs = In(),
                        Outputs = Ports("out")
                    });
                    Link(id, "out", next);
                    return id;
                }
                case GetStmt get:
                {
                    var id = Id("get");
                    Nodes.Add(new GetStateNode
                    {
                        Id = id,
                        StateKey = get.Key,
                        Inputs = In(),
                        Outputs = Ports("out")
                    });
                    Link(id, "out", next);
                    return id;
                }
                case PutStmt put:
                {
                    var id = Id("put");
                    Nodes.Add(new PutStateNode
                    {
                        Id = id,
                        StateKey = put.Key,
                        Inputs = In(),
                        Outputs = Ports("out")
                    });
                    Link(id, "out", next);
                    return id;
                }
                case BranchStmt branch:
                {
                    var id = Id("if");
                    Nodes.Add(new BranchNode
                    {
                        Id = id,
                        ConditionId = Expr(branch.Cond),
                        Inputs = In(),
                        Outputs = Ports("true", "false")
                    });
                    Link(id, "true", branch.Then is null ? null : Emit(branch.Then, next));
                    Link(id, "false", branch.Else is null ? null : Emit(branch.Else, next));
                    return id;
                }
                case LoopStmt loop:
                {
                    var id = Id("loop");
                    Expression? stop = loop.IterBound switch
                    {
                        0 => loop.Stop,
                        _ when loop.Stop is null => Cmp(CompareOp.Gte, F($"__iter_{id}__"), N(loop.IterBound)),
                        _ => Bin(BinaryOp.Or, Cmp(CompareOp.Gte, F($"__iter_{id}__"), Call("tonumber", F("s"))),
                            loop.Stop)
                    };
                    Nodes.Add(new LoopNode
                    {
                        Id = id,
                        MaxIterations = loop.Max,
                        StopConditionId = stop is null ? null : Expr(stop),
                        Inputs = In(),
                        Outputs = Ports("body", "exit")
                    });
                    Link(id, "body", Emit(loop.Body, null));
                    Link(id, "exit", next);
                    return id;
                }
                case SpinStmt:
                {
                    var id = Id("spin");
                    Nodes.Add(new DrawNode
                    {
                        Id = id,
                        Inputs = In(),
                        Outputs = new Dictionary<string, Port>
                        {
                            ["board"] = new()
                            {
                                Name = "board",
                                Type = PortType.Board
                            }
                        }
                    });
                    Link(id, "board", next);
                    return id;
                }
                case EvalStmt eval:
                {
                    var id = Id("eval");
                    Dictionary<string, Port> inputs = In();
                    if (eval.Multiplier is not null)
                    {
                        inputs["multiplier"] = new Port
                        {
                            Name = "multiplier",
                            Type = PortType.Number,
                            DefaultValue = eval.Multiplier
                        };
                    }

                    Nodes.Add(new MapNode
                    {
                        Id = id,
                        TransformId = eval.Transform,
                        ShareWildAcrossSymbols = eval.Share,
                        Inputs = inputs,
                        Outputs = new Dictionary<string, Port>
                        {
                            ["wins"] = new()
                            {
                                Name = "wins",
                                Type = PortType.Wins
                            }
                        }
                    });
                    Link(id, "wins", next);
                    return id;
                }
                case SeqStmt sequence: return Emit(sequence.Parts, next)!;
                default:
                {
                    var fan = (FanStmt)statement;
                    var id = Id("fan");
                    Nodes.Add(new PutStateNode
                    {
                        Id = id,
                        StateKey = "fan",
                        Inputs = In(),
                        Outputs = Ports("out")
                    });
                    Link(id, "out", Emit(fan.First, next) ?? next);
                    Link(id, "out", Emit(fan.Second, next) ?? next);
                    return id;
                }
            }
        }
    }

    // ── The comparison ───────────────────────────────────────────────────
    private enum Verdict
    {
        Invalid,
        ClassicBudget,
        Equal,
        EqualMeanOnly,
        Enclosed,
        DeclinedWhereClassicFails,
        DeclinedWhereClassicAnswers
    }
}
